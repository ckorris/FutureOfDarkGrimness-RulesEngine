using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.StageResolution.Requests;

namespace FDG.Stages
{
    /// <summary>
    /// #033 — the Caster "Cast" action. Reached from <see cref="ChooseActionStage"/> when the activating
    /// unit carries Caster(X) and its army has an affordable spell. The player picks a spell from the army's
    /// list, picks target(s) within the spell's range / line of sight / affinity, spends the spell's token
    /// cost (paid on the *attempt*, whether or not it succeeds), and rolls one die — on a 4+ the spell is
    /// cast and its effect applied. Then it loops back to Choose Action via <see cref="OnFinished"/>,
    /// **layered** like a custom action (it never sets HasMoved/HasAttacked), so the unit may still
    /// Move/Shoot and may cast again while it can afford another spell.
    ///
    /// The ±1 friendly-Caster assist (a tracked #033 follow-up) slots in between target selection and the
    /// roll. Spell-effect application (<see cref="ApplySpellEffect"/>) is wired in the next slice; this stage
    /// owns the casting control flow.
    /// </summary>
    public class CastSpellStage : StageBase<IUnitActionContext>
    {
        public StageBinding OnFinished;

        private const string CANCEL_OPTION = "Cancel";
        private const int CAST_SUCCESS_THRESHOLD = 4;

        public CastSpellStage(IGameContext gameContext, IStateMachineLayer<IUnitActionContext> parent)
            : base(gameContext, parent)
        {
            OnFinished = new StageBinding(this);
        }

        public override async Task Enter(IUnitActionContext context)
        {
            IUnit caster = context.ActivatingUnit.GetValue();
            PlayerID player = context.ActivatingPlayer();

            IReadOnlyList<RuntimeSpell> affordable = GetAffordableSpells(player,
                caster.Tokens.GetTokenCount(TokenType.SpellTokens));
            if (affordable.Count == 0)
            {
                GameContext.Log($"{caster.Name} has no affordable spell to cast.");
                await OnFinished.Activate(context);
                return;
            }

            // 1. Pick a spell (or cancel back to Choose Action).
            RuntimeSpell? chosen = await PickSpell(player, affordable);
            if (chosen == null)
            {
                await OnFinished.Activate(context);
                return;
            }

            // 2. Build the eligible targets and let the player pick (up to the spell's MaxCount).
            List<DataBinding<UnitData>> candidates = GetEligibleTargets(context.ActivatingUnit, player, chosen.Target);
            if (candidates.Count == 0)
            {
                GameContext.Log($"{chosen.Name} has no valid target in range or line of sight.");
                await OnFinished.Activate(context);
                return;
            }

            IReadOnlyList<DataBinding<UnitData>> targets = await PickTargets(player, chosen, candidates);
            if (targets.Count == 0)
            {
                // Cancelled before meeting the minimum target count — nothing spent.
                await OnFinished.Activate(context);
                return;
            }

            // 3. Spend the spell's token cost to attempt (spent whether or not the cast succeeds).
            caster.Tokens.RemoveTokens(TokenType.SpellTokens, chosen.Threshold);

            // 4. Cast roll: one die, 4+ succeeds. RollDecisive so it's a real outcome under the
            //    probabilistic roller. (±1 friendly-Caster assist — a #033 follow-up — would adjust here.)
            bool success = GameContext.DiceRoller.RollDecisive().AtOrAbove(CAST_SUCCESS_THRESHOLD) >= 1f;
            if (success)
            {
                GameContext.Log($"{caster.Name} cast {chosen.Name} (spent {chosen.Threshold} tokens).");
                ApplySpellEffect(context.ActivatingUnit, chosen, targets);
            }
            else
            {
                GameContext.Log($"{caster.Name} failed to cast {chosen.Name} (spent {chosen.Threshold} tokens).");
            }

            // 5. Layered: back to Choose Action. Move/Shoot are untouched; another cast is offered if the
            //    caster can still afford one.
            await OnFinished.Activate(context);
        }

        private IReadOnlyList<RuntimeSpell> GetAffordableSpells(PlayerID player, int tokens)
        {
            ArmyData? army = GameContext.GameDataStore().GetAllValues<ArmyData>()
                .FirstOrDefault(a => a.PlayerID == player);
            if (army == null)
            {
                return System.Array.Empty<RuntimeSpell>();
            }
            return army.Spells.Where(s => s.Threshold > 0 && s.Threshold <= tokens).ToList();
        }

        private async Task<RuntimeSpell?> PickSpell(PlayerID player, IReadOnlyList<RuntimeSpell> spells)
        {
            List<string> options = spells.Select(SpellOptionLabel).ToList();
            options.Add(CANCEL_OPTION);

            StringSelectionRequest request = new StringSelectionRequest(player, "Choose a spell to cast",
                options, System.Array.Empty<StringSelectionRequest.InvalidOption>());

            string choice = await GameContext.PlayerRequester
                .RequestDecision<StringSelectionRequest, string>(request);

            if (choice == CANCEL_OPTION)
            {
                return null;
            }
            return spells.First(s => SpellOptionLabel(s) == choice);
        }

        private async Task<IReadOnlyList<DataBinding<UnitData>>> PickTargets(PlayerID player, RuntimeSpell spell,
            List<DataBinding<UnitData>> candidates)
        {
            List<DataBinding<UnitData>> chosen = new List<DataBinding<UnitData>>();
            List<DataBinding<UnitData>> remaining = new List<DataBinding<UnitData>>(candidates);

            for (int picked = 0; picked < spell.Target.MaxCount && remaining.Count > 0; picked++)
            {
                List<SelectionRequest<UnitData>.ValidOption> validOptions = remaining
                    .Select(u => new SelectionRequest<UnitData>.ValidOption(u, u.GetValue().Name))
                    .ToList();

                SelectionRequest<UnitData> request = new SelectionRequest<UnitData>(player,
                    $"Choose target for {spell.Name} ({chosen.Count + 1} of up to {spell.Target.MaxCount})",
                    validOptions, System.Array.Empty<SelectionRequest<UnitData>.InvalidOption>(),
                    allowCancel: true);

                DataBinding<UnitData> target = await GameContext.PlayerRequester
                    .RequestDecision<SelectionRequest<UnitData>, DataBinding<UnitData>>(request);

                if (target == null)
                {
                    // Cancel stops target selection: proceed with what's chosen if the minimum is met,
                    // otherwise the caller treats it as cancelling the cast (nothing spent).
                    break;
                }

                chosen.Add(target);
                remaining.RemoveAll(u => u.Reference.Equals(target.Reference));
            }

            return chosen.Count >= spell.Target.MinCount ? chosen : new List<DataBinding<UnitData>>();
        }

        /// <summary>
        /// The target units a spell may legally pick: filtered by affinity (friend/foe/any), then by whether
        /// any living caster model is within the spell's range of any living target model (base-to-base, 3D)
        /// and — when the spell requires line of sight — has line of sight to it. Mirrors the per-model
        /// range/LoS test <see cref="ChooseRangedAttackStage"/> uses for shooting.
        /// </summary>
        private List<DataBinding<UnitData>> GetEligibleTargets(DataBinding<UnitData> caster, PlayerID casterPlayer,
            TargetSelector selector)
        {
            TeamData? team = GameContext.GameDataStore().GetAllValues<TeamData>()
                .FirstOrDefault(t => t.IsPlayerOnTeam(casterPlayer));
            bool IsFriendly(PlayerID p) => team != null ? team.IsPlayerOnTeam(p) : p == casterPlayer;

            List<ITerrain> terrain = GameContext.TableState.Terrain.Objects.ToList();
            List<DataBinding<UnitData>> candidates = new List<DataBinding<UnitData>>();

            IEnumerable<DataBinding<UnitData>> allUnits = GameContext.GameDataStore().GetAllValues<ArmyData>()
                .SelectMany(a => a.UnitBindings)
                .Where(u => u.GetValue().GetIsAlive() && u.GetValue().GetIsOnBattlefield());

            foreach (DataBinding<UnitData> unit in allUnits)
            {
                if (!MatchesAffinity(selector.TargetAffinity, caster, unit, IsFriendly)) continue;
                if (!WithinRangeAndSight(caster, unit, selector, terrain)) continue;
                candidates.Add(unit);
            }
            return candidates;
        }

        private static bool MatchesAffinity(ETargetAffinity affinity, DataBinding<UnitData> caster,
            DataBinding<UnitData> candidate, System.Func<PlayerID, bool> isFriendly)
        {
            bool friendly = isFriendly(candidate.GetValue().PlayerID);
            bool self = candidate.Reference.Equals(caster.Reference);
            return affinity switch
            {
                ETargetAffinity.Self => self,
                ETargetAffinity.Friend => friendly,
                ETargetAffinity.Foe => !friendly,
                ETargetAffinity.Any => true,
                _ => false,
            };
        }

        private bool WithinRangeAndSight(DataBinding<UnitData> caster, DataBinding<UnitData> target,
            TargetSelector selector, IReadOnlyList<ITerrain> terrain)
        {
            IReadOnlyList<ITerrain> blockers = selector.RequireLineOfSight
                ? terrain.Concat(LineOfSightUtilities.BuildModelBlockers(GameContext.TableState, caster, target)).ToList()
                : terrain;

            foreach (DataBinding<ModelData> casterModel in caster.GetValue().ModelBindings.Where(m => m.GetValue().GetIsAlive()))
            {
                ModelData cm = casterModel.GetValue();
                Position casterPos = cm.PositionBinding.GetValue();
                foreach (DataBinding<ModelData> targetModel in target.GetValue().ModelBindings.Where(m => m.GetValue().GetIsAlive()))
                {
                    ModelData tm = targetModel.GetValue();
                    Position targetPos = tm.PositionBinding.GetValue();
                    float distance = DistanceUtilities.GetBaseToBaseDistanceInches_3D(
                        casterPos, targetPos, cm.BaseRadiusInches, tm.BaseRadiusInches);
                    if (distance > selector.RangeInches) continue;
                    if (!selector.RequireLineOfSight) return true;
                    if (LineOfSightUtilities.HasLineOfSight(casterPos, targetPos, blockers)) return true;
                }
            }
            return false;
        }

        // Spell-effect application (buff token / damage pipeline) is wired in the next slice; this stage
        // owns the casting control flow up to and including the 4+ roll.
        private void ApplySpellEffect(DataBinding<UnitData> caster, RuntimeSpell spell,
            IReadOnlyList<DataBinding<UnitData>> targets)
        {
            GameContext.Log($"(spell effect for {spell.Name} on {targets.Count} target(s) — applied in slice 3)");
        }

        private static string SpellOptionLabel(RuntimeSpell spell) => $"{spell.Name} ({spell.Threshold})";
    }
}
