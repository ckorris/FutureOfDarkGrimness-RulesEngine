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
    /// Two effect archetypes:
    /// <list type="bullet">
    ///   <item><b>Non-damage</b> (<see cref="Effect.AddRule"/> &amp; other token effects, plus the imperative
    ///         <see cref="Effect.TriggeredMove"/>): the effect is applied to each target via the polymorphic
    ///         <see cref="Effect.Apply"/>; the resulting token operations are committed (the "gets RULE once
    ///         (next time)" buff/debuff shape) and any <see cref="ExecutableOperation"/> is run through the
    ///         <c>IOperationServices</c> seam — #034's "reposition an enemy unit" spell moves the target with
    ///         the caster directing. No child pipeline.</item>
    ///   <item><b>Damage</b> (<see cref="Effect.DealHits"/>): resolved through the looped child
    ///         <see cref="ResolveSpellDamageStage"/> — once per chosen target, each with its own fresh
    ///         <see cref="CombatMetadata"/> (the <see cref="ShootStage"/>/<see cref="FireStage"/> pattern).
    ///         Each target's hits run the hit-complete fold (Blast multiply, on-6 extra hits, Rending AP)
    ///         then the shared save→wound→assign→apply pipeline. AP + the spell's pre-resolved weapon rules
    ///         ride the synthetic weapon; #034 single-model spells confine wounds to one chosen model.</item>
    /// </list>
    ///
    /// DEFERRED (recorded in #033/#034): the ±1 friendly-Caster assist (#103, slots in before the roll).
    /// </summary>
    public class CastSpellStage : ParentStage<IUnitActionContext, SpellDamageRunContext>
    {
        public StageBinding OnFinished;

        private const string CANCEL_OPTION = "Cancel";
        private const int CAST_SUCCESS_THRESHOLD = 4;

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

            // 4. Cast roll: one die, 4+ succeeds. RollDecisive so it's a real outcome under the
            //    probabilistic roller. (±1 friendly-Caster assist — #103 — would adjust here.)
            bool success = GameContext.DiceRoller.RollDecisive().AtOrAbove(CAST_SUCCESS_THRESHOLD) >= 1f;
            if (!success)
            {
                GameContext.Log($"{caster.Name} failed to cast {chosen.Name} (spent {chosen.Threshold} tokens).");
                await OnFinished.Activate(context);
                return;
            }

            GameContext.Log($"{caster.Name} cast {chosen.Name} (spent {chosen.Threshold} tokens).");

            // 5a. Damage spell → resolve each chosen target through the looped child pipeline.
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

            // 5b. Non-damage spell → apply the effect to each target (no child pipeline): token grants
            //     (buff/debuff) and inline engine operations (forced enemy move, #034) both flow through here.
            await ApplyNonDamageEffect(caster, chosen, targets);
            await OnFinished.Activate(context);
        }

        protected override SpellDamageRunContext GetNewChildContext(IUnitActionContext contextSelf)
        {
            // Only the damage path enters children, and it set _pendingRun before base.Enter.
            return _pendingRun;
        }

        // Applies a non-damage spell effect to each target by running the effect's polymorphic Apply against
        // a per-target invocation, then enacting the resulting operations. Two disjoint operation kinds come
        // out: token grants (AddRule "gets RULE once", StatModifier buffs) committed by ApplyTokenOperations,
        // and imperative ExecutableOperations (TriggeredMove — #034's forced enemy move) run by
        // OperationExecutor. The two filters don't overlap, so applying both is safe for any effect. The
        // caster is the bearer, so a TriggeredMove targeting an enemy routes the move request to the caster.
        private async Task ApplyNonDamageEffect(IUnit caster, RuntimeSpell spell,
            IReadOnlyList<DataBinding<UnitData>> targets)
        {
            foreach (DataBinding<UnitData> target in targets)
            {
                RuleInvocation invocation = new RuleInvocation(
                    Hook: null, Bearer: caster, Arguments: System.Array.Empty<RuleArgument>(),
                    Target: target.GetValue(), DiceRoller: GameContext.DiceRoller);

                List<RuleOperation> operations = new List<RuleOperation>();
                spell.Effect.Apply(invocation, operations);
                OperationApplier.ApplyTokenOperations(operations);
                await OperationExecutor.Execute(operations, new GameOperationServices(GameContext));
            }
            GameContext.Log($"{spell.Name} affected {targets.Count} unit(s).");
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

        private static string SpellOptionLabel(RuntimeSpell spell) => $"{spell.Name} ({spell.Threshold})";
    }
}
