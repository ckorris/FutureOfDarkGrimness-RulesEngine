using System.Collections.Generic;
using FDG.Presentation;
using FDG.Presentation.Beats;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Utilities;

namespace FDG.Stages
{
    /// <summary>
    /// #042 Impact: when a charging unit reaches combat, it rolls X impact dice (each 2+ a hit) that
    /// resolve through save + wound application BEFORE the normal melee swings. Fired at charge contact,
    /// so only the charger triggers it (melee is only ever entered via Charge, and strike-back never
    /// runs this stage). The impact hits have no weapon — they ride a synthetic AP-0 attack seeded as a
    /// RollToHitResults, then run the existing save→wound sub-pipeline.
    ///
    /// The charge-contact "when" is evaluated for BOTH combatants: the charger (Actor) emits its Impact(X)
    /// dice, and the defender (Subject) emits Counter's "-1 impact die per Counter model" reduction — both
    /// fold into one ImpactSink, so the stage rolls the net dice.
    ///
    /// DEFERRED (Appendix C): the "not fatigued" gate (fatigue is absent), so Impact always rolls.
    /// </summary>
    public class ResolveImpactHitsStage : ParentStage<ICombatActionContext, ICombatMetadata>
    {
        public StageBinding OnImpactResolved;

        // The number of impact HITS (2+ results) rolled this charge, computed in Enter and seeded into
        // the child metadata. Only meaningful between Enter and the child pipeline running.
        private float _impactHitCount;

        public ResolveImpactHitsStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent)
            : base(gameContext, parent)
        {
        }

        public override async Task Enter(ICombatActionContext context)
        {
            IUnit attacker = context.AttackingUnit.GetValue();
            IUnit defender = context.DefendingUnit.GetValue();

            // The defender's melee weapons join as carriers so weapon-scoped Counter's
            // impact-dice reduction fires (#027); the charger's Impact(X) is a unit rule.
            var participants = new List<(IUnit, ERuleSeat, IWeapon?)> { (attacker, ERuleSeat.Actor, null) };
            participants.AddRange(DetermineStrikeOrderStage.SubjectWithMeleeWeapons(defender));

            IReadOnlyList<RuleOperation> operations = GameContext.RuleEvaluator.EvaluateAll(
                new ChargeContactContext(attacker, defender),
                participants.ToArray());

            ImpactSink impact = new ImpactSink();
            impact.ApplyFrom(operations);

            if (impact.TotalDice <= 0)
            {
                // No Impact rule on the charger, or a Counter defender reduced the dice to zero — skip
                // straight to the normal melee flow.
                await OnImpactResolved.Activate(context);
                return;
            }

            IDiceResults rolled = GameContext.DiceRoller.Roll(impact.TotalDice);
            _impactHitCount = rolled.AtOrAbove(2); // each 2+ is a hit (float under the probabilistic roller)

            GameContext.Log($"{attacker.Name}'s Impact rolled {impact.TotalDice} dice -> {_impactHitCount} hit(s) on the charge.");

            await GameContext.Presenter.Present(
                DiceRolledBeat.From(rolled, 2, GameContext.Settings.RandomnessType, "Impact Hits",
                    $"{_impactHitCount:0.##} impact hits"));

            if (_impactHitCount <= 0f)
            {
                await OnImpactResolved.Activate(context);
                return;
            }

            // Run the save→wound sub-pipeline against the impact hits.
            await base.Enter(context);
        }

        protected override ICombatMetadata GetNewChildContext(ICombatActionContext contextSelf)
        {
            // Impact hits come from a weaponless charge — model them as a synthetic AP-0 attack so the
            // shared save/wound stages can consume them. The histogram face is cosmetic (only TotalRolls
            // matters downstream).
            Weapon impactWeapon = new Weapon("Impact", rangeInches: 0f, attacks: 0, armorPenetration: 0);

            CombatMetadata metadata = new CombatMetadata(GameContext, contextSelf.AttackingUnit,
                contextSelf.DefendingUnit, impactWeapon, weaponCount: 1, isMelee: true);

            metadata.AddResult(new RollToHitResults(
                new List<SuccessfulHitInfo>() { new SuccessfulHitInfo(SyntheticHits(_impactHitCount)) },
                new List<FailedHitInfo>()));
            // Melee never runs CoverCheckStage; seed a zero bonus so the shared save stage doesn't throw.
            metadata.AddResult(new CoverCheckResults(0));

            return metadata;
        }

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<ICombatMetadata> startingChild)
        {
            OnImpactResolved = new StageBinding(this);

            Dictionary<string, Transition> dictionary = new TransitionSetBuilder(this)
                .AddChild(new DetermineSaveRollsNeededStage<ICombatMetadata>(GameContext, this), out var determineSaveRollsNeeded)
                .AddChild(new RollToSaveStage<ICombatMetadata>(GameContext, this), out var rollToSave)
                .AddChild(new AssignWoundsStage<ICombatMetadata>(GameContext, this), out var assignWounds)
                .AddChild(new ApplyWoundsStage<ICombatMetadata>(GameContext, this), out var applyWounds)
                .AddSibling(nameof(OnImpactResolved), OnImpactResolved, out string impactResolvedEvent)
                .Build();

            startingChild = determineSaveRollsNeeded;

            determineSaveRollsNeeded.BindNextStage(rollToSave)
                .BindNextStage(assignWounds)
                .BindNextStage(applyWounds)
                .BindToEvent(impactResolvedEvent);

            return dictionary;
        }

        // Bridges a scalar hit count into the IDiceResults the save flow consumes (mirrors
        // RollToHitStage.SyntheticHits). The face is cosmetic — saves count by TotalRolls.
        private static IDiceResults SyntheticHits(float count)
        {
            float[] perSide = new float[IDiceRollerExtensions.DEFAULT_SIDE_COUNT];
            perSide[perSide.Length - 1] = count;
            return new DiceResults(perSide);
        }
    }
}
