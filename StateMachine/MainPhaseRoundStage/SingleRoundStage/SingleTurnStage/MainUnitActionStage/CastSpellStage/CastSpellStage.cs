using System.Collections.Generic;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.StageResolution.Requests;
using FDG.Utilities;

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
    /// Three effect archetypes:
    /// <list type="bullet">
    ///   <item><b>Non-damage</b> (<see cref="Effect.AddRule"/> &amp; other token effects, plus the imperative
    ///         <see cref="Effect.TriggeredMove"/>): the effect is applied to each target via the polymorphic
    ///         <see cref="Effect.Apply"/>; the resulting token operations are committed (the "gets RULE once
    ///         (next time)" buff/debuff shape) and any <see cref="ExecutableOperation"/> is run through the
    ///         <c>IOperationServices</c> seam — #034's "reposition an enemy unit" spell moves the target with
    ///         the caster directing. No child pipeline.</item>
    ///   <item><b>Conditional</b> (<see cref="Effect.MoraleTestThen"/>, #034 #5): each target takes a morale
    ///         test and the nested on-fail effect (a forced move or fatigue) lands only on a failure. Enacted
    ///         stage-side because the test is async; the on-fail effect reuses the non-damage application
    ///         path above. No child pipeline.</item>
    ///   <item><b>Damage</b> (<see cref="Effect.DealHits"/>): resolved through the looped child
    ///         <see cref="ResolveSpellDamageStage"/> — once per chosen target, each with its own fresh
    ///         <see cref="CombatMetadata"/> (the <see cref="ShootStage"/>/<see cref="FireStage"/> pattern).
    ///         Each target's hits run the hit-complete fold (Blast multiply, on-6 extra hits, Rending AP)
    ///         then the shared save→wound→assign→apply pipeline. AP + the spell's pre-resolved weapon rules
    ///         ride the synthetic weapon; #034 single-model spells confine wounds to one chosen model.</item>
    /// </list>
    ///
    /// #103 — before the roll, other Caster units within 18" may spend their own spell tokens to sway the
    /// cast: friendly Casters add +1 each, enemy Casters subtract 1 each. The net modifier shifts the 4+
    /// success threshold (clamped to [1, 6]); assisters' tokens are spent whether or not the cast succeeds.
    /// </summary>
    public class CastSpellStage : ParentStage<IUnitActionContext, SpellDamageRunContext>
    {
        public StageBinding OnFinished;

        private const string CANCEL_OPTION = "Cancel";
        private const int CAST_SUCCESS_THRESHOLD = 4;

        // #103 assist text-beat colors — match the GUI highlight: blue for a friendly boost (+), orange for
        // an enemy disruption (-).
        private static readonly TextColor AssistBannerColor = new TextColor(77, 153, 255, 255);
        private static readonly TextColor HinderBannerColor = new TextColor(255, 153, 38, 255);

        // The damage run set up in Enter (synthetic weapon + chosen targets + base hits + any single-model
        // pick) and handed to the looped child pipeline by GetNewChildContext. Null on the buff path, which
        // applies its effect inline and never enters the children.
        private SpellDamageRunContext _pendingRun;

        public CastSpellStage(IGameContext gameContext, IStateMachineLayer<IUnitActionContext> parent)
            : base(gameContext, parent)
        {
        }

        public override async Task Enter(IUnitActionContext context)
        {
            _pendingRun = null;

            IUnit caster = context.ActivatingUnit.GetValue();
            PlayerID player = context.ActivatingPlayer();

            // Only castable spells are offered: affordable AND with at least one legal target. Filtering by
            // target here (and gating the Cast action the same way in ChooseActionStage.GetCanCast) is what
            // keeps a no-target cast from looping forever under a deterministic resolver — the same reason
            // ChooseRangedAttackStage filters weapons to those with a fireable target.
            int tokens = caster.Tokens.GetTokenCount(TokenType.SpellTokens);
            IReadOnlyList<RuntimeSpell> castable = GetCastableSpells(context.ActivatingUnit, player, tokens);
            if (castable.Count == 0)
            {
                GameContext.Log($"{caster.Name} has no castable spell (none affordable with a legal target).");
                await OnFinished.Activate(context);
                return;
            }

            // 1. Pick a spell (or cancel back to Choose Action).
            RuntimeSpell? chosen = await PickSpell(player, castable, tokens, caster.Name);
            if (chosen == null)
            {
                await OnFinished.Activate(context);
                return;
            }

            // 2. Build the eligible targets and let the player pick (up to the spell's MaxCount).
            List<DataBinding<UnitData>> candidates = SpellTargeting.GetEligibleTargets(
                GameContext, context.ActivatingUnit, player, chosen.Target);
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

            // 4. #103 — other Caster units within 18" may spend their own tokens to sway the cast: friendly
            //    Casters add +1 each, enemy Casters subtract 1 each. Their tokens are spent regardless of the
            //    cast's outcome (like the cast cost above). The net modifier shifts the success threshold.
            int assist = await CollectCastAssist(context.ActivatingUnit, player, chosen.Name);

            // 5. Cast roll: one die, base 4+ succeeds, shifted by the assist. RollDecisive so it's a real
            //    outcome under the probabilistic roller; a threshold shift (not a post-roll adjustment) keeps
            //    it a single decisive comparison. Clamp to [1, 6] so a big swing still leaves a 6 succeeding /
            //    a 1 failing rather than asking for an impossible face.
            int threshold = System.Math.Clamp(CAST_SUCCESS_THRESHOLD - assist, 1, IDiceRollerExtensions.DEFAULT_SIDE_COUNT);
            IDiceResults castRoll = GameContext.DiceRoller.RollDecisive();
            bool success = castRoll.AtOrAbove(threshold) >= 1f;

            // Spell out the roll so the assist is visible: what came up, what it needed, and (when assisted)
            // how the net ±1 shifted the base 4+. Assisters' own contributions were logged as they spent.
            string breakdown = assist != 0
                ? $" (base {CAST_SUCCESS_THRESHOLD}+, net {(assist > 0 ? "+" : "")}{assist} assist)"
                : "";
            string rollDesc = $"rolled {DecisiveFace(castRoll)}, needed {threshold}+{breakdown}; spent {chosen.Threshold} token{(chosen.Threshold == 1 ? "" : "s")}";

            if (!success)
            {
                GameContext.Log($"{caster.Name} failed to cast {chosen.Name} — {rollDesc}.");
                await OnFinished.Activate(context);
                return;
            }

            GameContext.Log($"{caster.Name} cast {chosen.Name} — {rollDesc}.");

            // 6a. Damage spell → resolve each chosen target through the looped child pipeline.
            if (chosen.Effect is Effect.DealHits dealHits)
            {
                // Synthetic spell weapon: the spell's AP + its pre-resolved weapon rules (shared across targets).
                Weapon spellWeapon = new Weapon(chosen.Name, rangeInches: 0f, attacks: 0,
                    armorPenetration: dealHits.ArmorPenetration);
                foreach (ResolvedRule rule in chosen.WeaponRules)
                {
                    spellWeapon.AttachRuleDefinition(rule);
                }

                // #034 single-model targeting: pick the one model now (the cast is committed, so it's
                // mandatory). Single-model spells use MaxCount = 1, so there is exactly one target.
                DataBinding<ModelData> individualModel = null;
                if (chosen.Target.SingleModel)
                {
                    individualModel = await PickIndividualModel(player, chosen.Name, targets[0]);
                }

                _pendingRun = new SpellDamageRunContext(context.ActivatingUnit, spellWeapon, dealHits.Count,
                    targets, individualModel);

                await base.Enter(context); // loops ResolveSpellDamageStage per target, then OnFinished
                return;
            }

            // 6b. Conditional spell (#034 #5) → each target takes a morale test; the on-fail effect lands
            //     only on a failure. Stage-side because the test is async and rolls against live state.
            if (chosen.Effect is Effect.MoraleTestThen conditional)
            {
                await ResolveConditionalSpell(caster, chosen, conditional, targets);
                await OnFinished.Activate(context);
                return;
            }

            // 6c. Non-damage spell → apply the effect to each target (no child pipeline): token grants
            //     (buff/debuff) and inline engine operations (forced enemy move, #034) both flow through here.
            foreach (DataBinding<UnitData> target in targets)
            {
                await ApplyEffectToTarget(caster, chosen.Effect, target);
            }
            GameContext.Log($"{chosen.Name} affected {targets.Count} unit(s).");
            await OnFinished.Activate(context);
        }

        protected override SpellDamageRunContext GetNewChildContext(IUnitActionContext contextSelf)
        {
            // Only the damage path enters children, and it set _pendingRun before base.Enter.
            return _pendingRun;
        }

        // #034 #5 — each target takes a morale test (its morale Quality, rule-aware via MoraleUtilities);
        // the spell's on-fail effect is applied only to targets that fail. Deep Hypnosis (move on fail) and
        // Terrifying Fury (fatigue on fail) share this shape.
        private async Task ResolveConditionalSpell(IUnit caster, RuntimeSpell spell,
            Effect.MoraleTestThen conditional, IReadOnlyList<DataBinding<UnitData>> targets)
        {
            foreach (DataBinding<UnitData> target in targets)
            {
                UnitData targetUnit = target.GetValue();
                MoraleUtilities.MoraleTestOutcome outcome = await MoraleUtilities.TakeMoraleTest(
                    GameContext, targetUnit, HeroStatRules.GetMoraleQuality(targetUnit));

                if (outcome.Passed)
                {
                    GameContext.Log($"{targetUnit.Name} passed {spell.Name}'s morale test — no effect.");
                    continue;
                }

                GameContext.Log($"{targetUnit.Name} failed {spell.Name}'s morale test.");
                await ApplyEffectToTarget(caster, conditional.OnFailure, target);
            }
        }

        // Applies one spell effect to one target by running the effect's polymorphic Apply against a
        // per-target invocation (caster = bearer), then enacting the resulting operations. Two disjoint
        // operation kinds come out: token grants (AddRule "gets RULE once", StatModifier buffs) committed by
        // ApplyTokenOperations, and imperative ExecutableOperations (TriggeredMove — #034's forced enemy
        // move; ApplyFatigue) run by OperationExecutor. The two filters don't overlap, so applying both is
        // safe for any effect. The caster is the bearer, so a TriggeredMove targeting an enemy routes the
        // move request to the caster. Shared by the plain non-damage path and the conditional on-fail branch.
        private async Task ApplyEffectToTarget(IUnit caster, Effect effect, DataBinding<UnitData> target)
        {
            RuleInvocation invocation = new RuleInvocation(
                Hook: null, Bearer: caster, Arguments: System.Array.Empty<RuleArgument>(),
                Target: target.GetValue(), DiceRoller: GameContext.DiceRoller);

            List<RuleOperation> operations = new List<RuleOperation>();
            effect.Apply(invocation, operations);
            OperationApplier.ApplyTokenOperations(operations);
            await OperationExecutor.Execute(operations, new GameOperationServices(GameContext));
        }

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<SpellDamageRunContext> startingChild)
        {
            OnFinished = new StageBinding(this);

            Dictionary<string, Transition> dictionary = new TransitionSetBuilder(this)
                .AddChild(new ResolveSpellDamageStage(GameContext, this), out var resolveSpellDamage)
                .AddChild(new DetermineMoreSpellTargetsStage(GameContext, this), out var determineMoreTargets)
                .AddSibling(nameof(OnFinished), OnFinished, out string finishedEvent)
                .Build();

            startingChild = resolveSpellDamage;

            // resolve one target → ask whether more remain → loop back to resolve the next, or finish.
            resolveSpellDamage.OnFinished.Bind(determineMoreTargets);
            determineMoreTargets.ResolveNextTarget.Bind(resolveSpellDamage);
            determineMoreTargets.ToFinished.Bind(finishedEvent);

            return dictionary;
        }

        private IReadOnlyList<RuntimeSpell> GetCastableSpells(DataBinding<UnitData> caster, PlayerID player, int tokens)
        {
            ArmyData army = GameContext.GameDataStore().GetAllValues<ArmyData>()
                .FirstOrDefault(a => a.PlayerID == player);
            if (army == null)
            {
                return System.Array.Empty<RuntimeSpell>();
            }
            return army.Spells
                .Where(s => s.Threshold > 0 && s.Threshold <= tokens
                    && SpellTargeting.HasAnyEligibleTarget(GameContext, caster, player, s.Target))
                .ToList();
        }

        private async Task<RuntimeSpell?> PickSpell(PlayerID player, IReadOnlyList<RuntimeSpell> spells,
            int tokens, string casterName)
        {
            List<string> options = spells.Select(SpellOptionLabel).ToList();
            options.Add(CANCEL_OPTION);

            // Subtext under each spell: what it does. The Cancel option carries none.
            Dictionary<string, string> descriptions = new Dictionary<string, string>();
            foreach (RuntimeSpell spell in spells)
            {
                descriptions[SpellOptionLabel(spell)] = SpellText.Describe(spell.Definition);
            }

            string instructions =
                $"Choose a spell to cast - {casterName} has {tokens} spell token{(tokens == 1 ? "" : "s")}";

            StringSelectionRequest request = new StringSelectionRequest(player, instructions,
                options, System.Array.Empty<StringSelectionRequest.InvalidOption>(), descriptions);

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

        // #034 single-model targeting: pick one living model in the target unit ("a unit of [1]"). The cast
        // has already succeeded and its tokens are spent, so the pick is mandatory (no cancel) — mirroring
        // Takedown's BuildTargetListStage.MaybePickIndividualTarget.
        private async Task<DataBinding<ModelData>> PickIndividualModel(PlayerID player, string spellName,
            DataBinding<UnitData> targetUnit)
        {
            List<DataBinding<ModelData>> living = targetUnit.ModelBindings()
                .Where(model => model.GetIsAlive())
                .ToList();

            // A legal damage target always has at least one living model; guard anyway so a degenerate
            // case falls back to whole-unit allocation rather than raising an option-less request.
            if (living.Count == 0)
            {
                return null;
            }

            List<SelectionRequest<ModelData>.ValidOption> validOptions = new List<SelectionRequest<ModelData>.ValidOption>();
            for (int i = 0; i < living.Count; i++)
            {
                validOptions.Add(new SelectionRequest<ModelData>.ValidOption(living[i], $"Model {i + 1}"));
            }

            SelectionRequest<ModelData> request = new SelectionRequest<ModelData>(player,
                $"{spellName}: choose the target model",
                validOptions, System.Array.Empty<SelectionRequest<ModelData>.InvalidOption>(), allowCancel: false);

            return await GameContext.PlayerRequester
                .RequestDecision<SelectionRequest<ModelData>, DataBinding<ModelData>>(request);
        }

        // #103 — offer every eligible Caster within 18" the chance to spend tokens on this cast, sum the
        // result into a net roll modifier (friendly +1/token, enemy -1/token) and spend the tokens. Friendly
        // helpers declare first — the casting side commits support, then the enemy responds. Tokens are spent
        // whether or not the cast then succeeds, matching the cast cost. Returns 0 when no one assists, so a
        // game with no nearby Casters sees no prompts and no behaviour change.
        private async Task<int> CollectCastAssist(DataBinding<UnitData> casterBinding, PlayerID casterPlayer,
            string spellName)
        {
            string casterName = casterBinding.GetValue().Name;
            int net = 0;
            foreach ((DataBinding<UnitData> unitBinding, bool friendly) in FindEligibleAssisters(casterBinding, casterPlayer))
            {
                IUnit assister = unitBinding.GetValue();
                int available = assister.Tokens.GetTokenCount(TokenType.SpellTokens);
                if (available <= 0) continue;

                int spent = await AskAssistCount(unitBinding, casterBinding, friendly, available, spellName);
                if (spent <= 0) continue;

                assister.Tokens.RemoveTokens(TokenType.SpellTokens, spent);
                net += friendly ? spent : -spent;

                // Text beat: announce who assisted/hindered and by how much — an on-screen banner plus the
                // log line, blue for a friendly boost and orange for an enemy disruption (matches the GUI
                // highlight). Only fires when a Caster actually spends (declines skip via the guard above).
                await GameContext.Announce(
                    $"{assister.Name} {(friendly ? "assists" : "hinders")} {casterName}'s cast of {spellName} " +
                    $"({(friendly ? "+" : "-")}{spent}).",
                    friendly ? AssistBannerColor : HinderBannerColor);
            }
            return net;
        }

        // Living, on-battlefield Caster units (other than the caster) that hold at least one spell token and
        // whose unit is within CASTER_ASSIST_RANGE_INCHES (base-to-base, 3D) of the casting unit. Friendly
        // Casters (same team, or same player when no team is registered — mirroring SpellTargeting) come
        // first, then enemy Casters, each group in store order for a deterministic prompt sequence.
        private List<(DataBinding<UnitData> unit, bool friendly)> FindEligibleAssisters(
            DataBinding<UnitData> casterBinding, PlayerID casterPlayer)
        {
            TeamData team = GameContext.GameDataStore().GetAllValues<TeamData>()
                .FirstOrDefault(t => t.IsPlayerOnTeam(casterPlayer));
            bool IsFriendly(PlayerID p) => team != null ? team.IsPlayerOnTeam(p) : p == casterPlayer;

            IUnit castingUnit = casterBinding.GetValue();
            List<(DataBinding<UnitData>, bool)> friends = new List<(DataBinding<UnitData>, bool)>();
            List<(DataBinding<UnitData>, bool)> enemies = new List<(DataBinding<UnitData>, bool)>();

            IEnumerable<DataBinding<UnitData>> allUnits = GameContext.GameDataStore().GetAllValues<ArmyData>()
                .SelectMany(a => a.UnitBindings);

            foreach (DataBinding<UnitData> unitBinding in allUnits)
            {
                if (unitBinding.Reference.Equals(casterBinding.Reference)) continue; // not the casting unit
                UnitData unit = unitBinding.GetValue();
                if (!unit.GetIsAlive() || !unit.GetIsOnBattlefield()) continue;
                if (!SpellTargeting.IsCaster(unit)) continue;
                if (unit.Tokens.GetTokenCount(TokenType.SpellTokens) <= 0) continue;

                float distance = UnitCompareUtilities.MinDistanceBetweenUnits(
                    castingUnit, unit, out _, out _, includeVertical: true);
                if (distance > GameWideConstants.CASTER_ASSIST_RANGE_INCHES) continue;

                bool friendly = IsFriendly(unit.PlayerID);
                (friendly ? friends : enemies).Add((unitBinding, friendly));
            }

            friends.AddRange(enemies);
            return friends;
        }

        // Ask one Caster's controller how many tokens to spend, via a CastAssistRequest that carries both
        // units so the GUI can highlight the assister and draw a line to the caster (blue friendly / orange
        // enemy) and show the token count. The reply is clamped to what the assister actually holds. CLI and
        // AI resolvers default to spending nothing.
        private async Task<int> AskAssistCount(DataBinding<UnitData> assistingUnit, DataBinding<UnitData> castingUnit,
            bool friendly, int available, string spellName)
        {
            CastAssistRequest request = new CastAssistRequest(
                assistingUnit.GetValue().PlayerID, assistingUnit, castingUnit, friendly, available, spellName);

            int spent = await GameContext.PlayerRequester
                .RequestDecision<CastAssistRequest, int>(request);

            return System.Math.Clamp(spent, 0, available);
        }

        private static string SpellOptionLabel(RuntimeSpell spell) => $"{spell.Name} ({spell.Threshold})";

        // The single face that came up on a decisive (one-die) roll, for logging. A decisive roll has exactly
        // one face with weight, so this returns it; falls back to the minimum face if none is set.
        private static int DecisiveFace(IDiceResults roll)
        {
            for (int face = roll.SideMin; face <= roll.SideMax; face++)
            {
                if (roll.At(face) > 0f) return face;
            }
            return roll.SideMin;
        }
    }
}
