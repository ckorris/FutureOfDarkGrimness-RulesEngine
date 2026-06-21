using System.Collections.Generic;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
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
    /// Two effect archetypes:
    /// <list type="bullet">
    ///   <item><b>Buff/debuff</b> (<see cref="Effect.AddRule"/> &amp; other token effects): the effect is
    ///         applied to each target via the polymorphic <see cref="Effect.Apply"/> and the resulting token
    ///         operations are committed — the "gets RULE once (next time)" shape.</item>
    ///   <item><b>Damage</b> (<see cref="Effect.DealHits"/>): the hits run through the shared
    ///         save→wound→assign→apply child pipeline against the target, seeded as a synthetic AP-carrying
    ///         attack — the same mechanism <see cref="StrafingStage"/>/ResolveImpactHitsStage use. AP rides
    ///         the synthetic weapon; the spell's pre-resolved weapon rules (Bane, Deadly, …) are attached so
    ///         they fire in that pipeline.</item>
    /// </list>
    ///
    /// DEFERRED (recorded in #033): the ±1 friendly-Caster assist (slots in before the roll); multi-target
    /// damage (only the first target is hit — the synthetic pipeline runs once per cast, as Strafing's does);
    /// single-model targeting ("a unit of [1]"); and pre-save weapon rules on spell hits (Blast's hit
    /// multiply, Surge) — the synthetic pipeline starts at the save stage, so only save/wound-phase rules
    /// (AP, Bane, Deadly, Regeneration) fire.
    /// </summary>
    public class CastSpellStage : ParentStage<IUnitActionContext, ICombatMetadata>
    {
        public StageBinding OnFinished;

        private const string CANCEL_OPTION = "Cancel";
        private const int CAST_SUCCESS_THRESHOLD = 4;

        // Damage-spell pipeline parameters: set in Enter when a successful damage cast routes into the
        // save→wound children, read by GetNewChildContext when the child pipeline builds its metadata.
        private DataBinding<UnitData> _damageTarget;
        private int _damageHits;
        private int _damageArmorPenetration;
        private string _damageSpellName = "Spell";
        private IReadOnlyList<ResolvedRule> _damageWeaponRules = System.Array.Empty<ResolvedRule>();

        public CastSpellStage(IGameContext gameContext, IStateMachineLayer<IUnitActionContext> parent)
            : base(gameContext, parent)
        {
        }

        public override async Task Enter(IUnitActionContext context)
        {
            _damageTarget = default;
            _damageHits = 0;
            _damageArmorPenetration = 0;
            _damageWeaponRules = System.Array.Empty<ResolvedRule>();

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

            // 4. Cast roll: one die, 4+ succeeds. RollDecisive so it's a real outcome under the
            //    probabilistic roller. (±1 friendly-Caster assist — a #033 follow-up — would adjust here.)
            bool success = GameContext.DiceRoller.RollDecisive().AtOrAbove(CAST_SUCCESS_THRESHOLD) >= 1f;
            if (!success)
            {
                GameContext.Log($"{caster.Name} failed to cast {chosen.Name} (spent {chosen.Threshold} tokens).");
                await OnFinished.Activate(context);
                return;
            }

            GameContext.Log($"{caster.Name} cast {chosen.Name} (spent {chosen.Threshold} tokens).");

            // 5a. Damage spell → run the synthetic-hit save→wound child pipeline against the target.
            if (chosen.Effect is Effect.DealHits dealHits)
            {
                if (targets.Count > 1)
                {
                    GameContext.Log($"{chosen.Name} chose multiple targets, but spell damage is single-target " +
                        $"for now — only {targets[0].GetValue().Name} is hit (#034).");
                }
                _damageTarget = targets[0];
                _damageHits = dealHits.Count;
                _damageArmorPenetration = dealHits.ArmorPenetration;
                _damageWeaponRules = chosen.WeaponRules;
                _damageSpellName = chosen.Name;
                await base.Enter(context); // child pipeline resolves the hits, then transitions to OnFinished
                return;
            }

            // 5b. Buff/debuff spell → apply the effect's token operations to each target.
            ApplyTokenEffect(caster, chosen, targets);
            await OnFinished.Activate(context);
        }

        // Applies a non-damage spell effect (e.g. AddRule "gets RULE once") to each target by running the
        // effect's polymorphic Apply against a per-target invocation, then committing the token operations.
        private void ApplyTokenEffect(IUnit caster, RuntimeSpell spell, IReadOnlyList<DataBinding<UnitData>> targets)
        {
            foreach (DataBinding<UnitData> target in targets)
            {
                RuleInvocation invocation = new RuleInvocation(
                    Hook: null, Bearer: caster, Arguments: System.Array.Empty<RuleArgument>(),
                    Target: target.GetValue(), DiceRoller: GameContext.DiceRoller);

                List<RuleOperation> operations = new List<RuleOperation>();
                spell.Effect.Apply(invocation, operations);
                OperationApplier.ApplyTokenOperations(operations);
            }
            GameContext.Log($"{spell.Name} affected {targets.Count} unit(s).");
        }

        protected override ICombatMetadata GetNewChildContext(IUnitActionContext contextSelf)
        {
            // The spell's hits ride a synthetic attack carrying the spell's AP, so the shared save/wound
            // stages resolve them; the spell's pre-resolved weapon rules (Bane, Deadly, …) are attached so
            // they fire in that pipeline. (Pre-save rules like Blast don't fire — see the class remarks.)
            Weapon spellWeapon = new Weapon(_damageSpellName, rangeInches: 0f, attacks: 0,
                armorPenetration: _damageArmorPenetration);
            foreach (ResolvedRule rule in _damageWeaponRules)
            {
                spellWeapon.AttachRuleDefinition(rule);
            }

            CombatMetadata metadata = new CombatMetadata(GameContext, contextSelf.ActivatingUnit,
                _damageTarget, spellWeapon, weaponCount: 1, isMelee: false);

            metadata.AddResult(new RollToHitResults(
                new List<SuccessfulHitInfo>() { new SuccessfulHitInfo(SyntheticHits(_damageHits)) },
                new List<FailedHitInfo>()));
            // No cover check runs for a synthetic spell hit; seed a zero bonus so the save stage won't throw.
            metadata.AddResult(new CoverCheckResults(0));

            return metadata;
        }

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<ICombatMetadata> startingChild)
        {
            OnFinished = new StageBinding(this);

            Dictionary<string, Transition> dictionary = new TransitionSetBuilder(this)
                .AddChild(new DetermineSaveRollsNeededStage<ICombatMetadata>(GameContext, this), out var determineSaveRollsNeeded)
                .AddChild(new RollToSaveStage<ICombatMetadata>(GameContext, this), out var rollToSave)
                .AddChild(new AssignWoundsStage<ICombatMetadata>(GameContext, this), out var assignWounds)
                .AddChild(new ApplyWoundsStage<ICombatMetadata>(GameContext, this), out var applyWounds)
                .AddSibling(nameof(OnFinished), OnFinished, out string finishedEvent)
                .Build();

            startingChild = determineSaveRollsNeeded;

            determineSaveRollsNeeded.BindNextStage(rollToSave)
                .BindNextStage(assignWounds)
                .BindNextStage(applyWounds)
                .BindToEvent(finishedEvent);

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
                $"Choose a spell to cast — {casterName} has {tokens} spell token{(tokens == 1 ? "" : "s")}";

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

        // Bridges a scalar hit count into the IDiceResults the save flow consumes (mirrors
        // StrafingStage.SyntheticHits / ResolveImpactHitsStage). The face is cosmetic — saves count by total.
        private static IDiceResults SyntheticHits(float count)
        {
            float[] perSide = new float[IDiceRollerExtensions.DEFAULT_SIDE_COUNT];
            perSide[perSide.Length - 1] = count;
            return new DiceResults(perSide);
        }

        private static string SpellOptionLabel(RuntimeSpell spell) => $"{spell.Name} ({spell.Threshold})";
    }
}
