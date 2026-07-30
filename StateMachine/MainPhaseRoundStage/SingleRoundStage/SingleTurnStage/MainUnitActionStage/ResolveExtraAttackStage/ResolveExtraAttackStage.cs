using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.SaveLoad;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Utilities;

namespace FDG.Stages
{
    /// <summary>
    /// #197 P16 — the one-shot extra-attack window. Offers any <see cref="Effect.ExtraAttack"/> ability at
    /// <see cref="EHookID.Combat_OnAttackWindow"/> and, if accepted, resolves it as a real attack against the
    /// defender the action has already committed to: Takedown Strike ("once per game, when it's this model's
    /// turn to attack in melee, it may make one attack at Quality 2+ with AP(2), Deadly(3), and Takedown")
    /// and Takedown Shot (the same, "when this model shoots ... against the target").
    ///
    /// <para>Instantiated THREE times, once per direction a unit can attack: in the shoot chain between the
    /// weapon/target choice and FireStage, in the melee chain between in-range determination and the swing
    /// loop, and in <see cref="StrikeBackStage"/> before its own swing loop — which is what makes "when it's
    /// this model's turn to attack in melee" true of a unit that was charged, not only of the charger.
    /// The attack is EXTRA (owner ruling, 2026-07-30): it is injected alongside the action's normal volley or
    /// swings, never in place of them, so the pending attack queue is left untouched here.</para>
    ///
    /// <para>The target is inherited rather than picked. Both source rules say "against the target" / "in
    /// melee", and by this point the defender is fixed and already validated — range and line of sight by
    /// <see cref="ChooseRangedAttackStage"/>, contact by <see cref="DetermineInRangeAttackersStage"/> — so a
    /// selection prompt would only offer a way to break the rule. That is also why the chain below has no
    /// range or occlusion check of its own, exactly as <see cref="StrafingStage"/> has none.</para>
    ///
    /// <para>The whole payload is a SYNTHETIC weapon built from the effect's authored profile, so the rule
    /// needs no modifier vocabulary of its own: "at Quality 2+" is <c>Reliable</c>, and Deadly(X) / Takedown
    /// / anything else in <c>WithRules</c> are weapon rules that fold through the shared hit, save and wound
    /// stages the way they do for a fired volley. The build mirrors
    /// <see cref="BeforeAttackActionStage"/>'s: resolved at dispatch time through the evaluator's shared
    /// resolver, because an ability may have been conferred at runtime by an aura or grant and so has no
    /// army-load site.</para>
    ///
    /// <para>Like <see cref="StrafingStage"/>, the attack operation is enacted stage-side rather than through
    /// the <c>IOperationServices</c> seam: the hit-to-wound resolution is a child-stage pipeline, and the
    /// engine's fire-and-forget transitions only sequence correctly when it runs as a real child.</para>
    /// </summary>
    public abstract class ResolveExtraAttackStage : ParentStage<ICombatActionContext, ICombatMetadata>
    {
        public StageBinding OnExtraAttackResolved;

        /// <summary>
        /// Which chain this instance sits in. A subclass override rather than a constructor flag because
        /// <see cref="PopulateTransitions"/> also reads it - the child chain's shape differs (cover is
        /// ranged-only) - and that runs from the BASE constructor, before any derived field is assigned. A
        /// field would still be false there, silently giving a melee instance the cover stage.
        /// </summary>
        protected abstract bool IsMelee { get; }

        // The synthetic weapon the accepted offer attacks with, and the defender it attacks. Only meaningful
        // between Enter and the child chain running (the StrafingStage pattern).
        private Weapon? _weapon;
        private DataBinding<UnitData> _target;

        protected ResolveExtraAttackStage(IGameContext gameContext,
            IStateMachineLayer<ICombatActionContext> parent) : base(gameContext, parent)
        {
        }

        public override async Task Enter(ICombatActionContext context)
        {
            _weapon = null;
            _target = default;

            if (context.DefendingUnit == default)
            {
                await OnExtraAttackResolved.Activate(context);
                return;
            }

            IUnit attacker = context.AttackingUnit.GetValue();
            IUnit defender = context.DefendingUnit.GetValue();

            // Only extra-attack abilities are ours. Other abilities authored at this hook (none today) would
            // be some other stage's business, and paying their cost here would leave them doing nothing.
            IReadOnlyList<AbilityOffer> offers = GameContext.RuleEvaluator
                .GatherOffers(new AttackWindowContext(attacker, defender, IsMelee))
                .Where(o => o.Ability.Effect is Effect.ExtraAttack)
                .ToList();

            foreach (AbilityOffer offer in offers)
            {
                if (!await Accepted(offer, attacker, defender))
                {
                    continue;
                }

                // Resolved only AFTER the yes, so declining costs nothing: ResolveAbility emits the
                // once-per-game marker along with the effect.
                IReadOnlyList<RuleOperation> ops = GameContext.RuleEvaluator
                    .ResolveAbility(offer, new[] { defender });

                // The cost marker rides the shared token applier; OperationExecutor runs only
                // ExecutableOperations, so it never applies cost tokens - both are needed to cover the queue.
                OperationApplier.ApplyTokenOperations(ops);
                await OperationExecutor.Execute(ops, new GameOperationServices(GameContext));

                RuleOperation.InvokeExtraAttack? extra =
                    ops.OfType<RuleOperation.InvokeExtraAttack>().FirstOrDefault();
                if (extra == null || extra.Attacks <= 0)
                {
                    continue;
                }

                _weapon = BuildProfileWeapon(offer.RuleName, extra);
                _target = context.DefendingUnit;
                GameContext.Log($"{attacker.Name} used {offer.RuleName}: one extra attack against " +
                    $"{defender.Name} with {_weapon.Name}.");

                // Run the attack sub-pipeline; its terminal event fires OnExtraAttackResolved. Only one
                // extra attack resolves per window - a second would need the child chain to re-enter, and no
                // corpus unit carries two of these rules.
                await base.Enter(context);
                return;
            }

            await OnExtraAttackResolved.Activate(context);
        }

        /// <summary>
        /// "It MAY make one attack" — a yes/no, defaulted to yes. One question, not two: the target is not
        /// the player's to choose here, so accepting is the only decision the rule offers.
        /// </summary>
        private async Task<bool> Accepted(AbilityOffer offer, IUnit attacker, IUnit defender)
        {
            string how = IsMelee ? "in melee" : "shooting";
            var question = new YesNoRequest(attacker.PlayerID,
                $"Use {offer.RuleName} (once per game): {attacker.Name} makes one extra attack " +
                $"{how} against {defender.Name}?",
                defaultAnswer: true);

            return await GameContext.PlayerRequester.RequestDecision<YesNoRequest, bool>(question);
        }

        /// <summary>
        /// The synthetic weapon the extra attack is made with: the profile's attack count and AP, plus its
        /// rule names resolved to weapon-scoped rules so Reliable / Deadly(X) / Takedown fold in. A null
        /// resolver (bare harness, or a resume before #095 rehydration) degrades to a bare profile rather
        /// than throwing, matching the ability-weapon build in <see cref="BeforeAttackActionStage"/>.
        /// </summary>
        private Weapon BuildProfileWeapon(string ruleName, RuleOperation.InvokeExtraAttack extra)
        {
            Weapon weapon = new Weapon(extra.WeaponName, rangeInches: 0f, attacks: extra.Attacks,
                armorPenetration: extra.ArmorPenetration);

            IRuleResolver? resolver = GameContext.RuleEvaluator.RuleResolver;
            if (extra.WithRules.Count == 0 || resolver == null)
            {
                return weapon;
            }

            foreach (ResolvedRule rule in ArmyListSpellResolution.ResolveWeaponRuleNames(
                extra.WithRules, resolver, $"rule '{ruleName}'"))
            {
                weapon.AttachRuleDefinition(rule);
            }
            return weapon;
        }

        protected override ICombatMetadata GetNewChildContext(ICombatActionContext contextSelf)
        {
            CombatMetadata metadata = new CombatMetadata(GameContext, contextSelf.AttackingUnit,
                _target, _weapon!, weaponCount: 1, attackerMoved: false, isMelee: IsMelee);

            if (IsMelee)
            {
                // Melee runs no cover check (cover is a ranged-only mechanic) but the shared
                // DetermineSaveRollsNeededStage still reads CoverCheckResults - seed a zero bonus, as
                // SwingMeleeWeaponStage does.
                metadata.AddResult(new CoverCheckResults(0));
            }

            return metadata;
        }

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<ICombatMetadata> startingChild)
        {
            OnExtraAttackResolved = new StageBinding(this);

            // The shared attack chain, minus range and occlusion (see the class remarks). CoverCheckStage
            // runs for a shot and not for a swing, matching what the two real chains do.
            TransitionSetBuilder builder = new TransitionSetBuilder(this)
                .AddChild(new BuildTargetListStage<ICombatMetadata>(GameContext, this), out var buildTargetList);

            CoverCheckStage? coverCheck = null;
            if (!IsMelee)
            {
                coverCheck = new CoverCheckStage(GameContext, this);
                builder = builder.AddChild(coverCheck);
            }

            Dictionary<string, Transition> dictionary = builder
                .AddChild(new DetermineHitRollStage<ICombatMetadata>(GameContext, this), out var determineHitRoll)
                .AddChild(new RollToHitStage<ICombatMetadata>(GameContext, this), out var rollToHit)
                .AddChild(new DetermineSaveRollsNeededStage<ICombatMetadata>(GameContext, this), out var determineSaveRollsNeeded)
                .AddChild(new RollToSaveStage<ICombatMetadata>(GameContext, this), out var rollToSave)
                .AddChild(new AssignWoundsStage<ICombatMetadata>(GameContext, this), out var assignWounds)
                .AddChild(new ApplyWoundsStage<ICombatMetadata>(GameContext, this), out var applyWounds)
                .AddSibling(nameof(OnExtraAttackResolved), OnExtraAttackResolved, out string resolvedEvent)
                .Build();

            startingChild = buildTargetList;

            if (coverCheck != null)
            {
                buildTargetList.BindNextStage(coverCheck).BindNextStage(determineHitRoll);
            }
            else
            {
                buildTargetList.BindNextStage(determineHitRoll);
            }

            determineHitRoll.BindNextStage(rollToHit)
                .BindNextStage(determineSaveRollsNeeded)
                .BindNextStage(rollToSave)
                .BindNextStage(assignWounds)
                .BindNextStage(applyWounds)
                .BindToEvent(resolvedEvent);

            return dictionary;
        }
    }

    /// <summary>
    /// The melee instance of the extra-attack window: wired into <see cref="MeleeStage"/> before the swing
    /// loop and into <see cref="StrikeBackStage"/> before its own, so "when it's this model's turn to attack
    /// in melee" is true for the charger and the striker-back alike.
    /// </summary>
    public sealed class ResolveMeleeExtraAttackStage : ResolveExtraAttackStage
    {
        protected override bool IsMelee => true;

        public ResolveMeleeExtraAttackStage(IGameContext gameContext,
            IStateMachineLayer<ICombatActionContext> parent) : base(gameContext, parent)
        {
        }
    }

    /// <summary>
    /// The shooting instance of the extra-attack window: wired into <see cref="ShootStage"/> between the
    /// weapon/target choice and the volley, so "when this model shoots" fires against the chosen target.
    /// </summary>
    public sealed class ResolveRangedExtraAttackStage : ResolveExtraAttackStage
    {
        protected override bool IsMelee => false;

        public ResolveRangedExtraAttackStage(IGameContext gameContext,
            IStateMachineLayer<ICombatActionContext> parent) : base(gameContext, parent)
        {
        }
    }
}
