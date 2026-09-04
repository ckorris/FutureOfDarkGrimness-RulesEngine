using FDG.Ai.Tactician;
using FDG.Ai.Tactician.Resolvers;
using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.Stages;
using FDG.StageResolution.Requests;
using FDG.Utilities;
using NUnit.Framework;

namespace FDG.Tests
{
    // #191 B1 step 5b - the prescription seam, and the pin B0 asked for.
    //
    // B0's finding 4: injecting a decision at the registry/wire boundary reproduces natural play
    // byte-identically under SoloRules but NOT under the Tactician, because the activation resolver
    // calls TacticianPlanner.BeginActivation as a side effect of picking. Bypass the resolver and
    // every later request in that activation is answered by a planner that was never told which unit
    // is acting - a silent corruption, not a fault, which under search would mean exploring branches
    // whose continuation was computed by a mis-initialised planner.
    //
    // So B1 prescribes THROUGH the policy. These tests pin the two halves of that contract:
    //   (a) prescribing what the planner would itself have chosen leaves it in the SAME state as
    //       letting it choose - the control that used to diverge;
    //   (b) prescribing something else actually steers, and does so without the planner scoring.
    [TestFixture]
    public class TacticianPrescriptionTests
    {
        private static readonly string[] AllActions =
        {
            ChooseActionStage.MOVEMENT_CHOICE_NAME, ChooseActionStage.CHARGE_CHOICE_NAME,
            ChooseActionStage.SHOOT_CHOICE_NAME, ChooseActionStage.PASS_CHOICE_NAME,
        };

        private GameDataStore _store = null!;
        private TableState _tableState = null!;
        private RuleEvaluator _evaluator = null!;
        private TacticianPlanner _planner = null!;
        private PlayerID _us;
        private PlayerID _them;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _tableState = new TableState(_store);
            _evaluator = new RuleEvaluator(new ProbabilisticDiceRoller());
            _planner = new TacticianPlanner(_tableState, _evaluator);
            _us = new PlayerID(Guid.NewGuid());
            _them = new PlayerID(Guid.NewGuid());
        }

        // --- (a) the control: prescription reproduces natural play ---------------------------------

        [Test]
        public void PrescribingThePlannersOwnChoice_ReproducesTheNaturalPlanExactly()
        {
            // The B0 control, at planner level: ask what it would do, then make a fresh planner take
            // that same decision by prescription. Same action, same cached move, same macro label.
            _store.Create(new ObjectiveData(new Position(30f, 24f), _store));
            DataBinding<UnitData> unit = MakeUnit(_us, 3, Rifle(), atX: 20f, atZ: 24f);

            _planner.BeginActivation(unit);
            string? naturalAction = _planner.ChooseAction(AllActions);
            string? naturalMacro = _planner.LastMacroLabel;
            List<ModelMoveEntry>? naturalMove = _planner.TakePlannedMove(unit);
            MacroAction naturalPlan = PlanFor(unit, naturalAction!);

            var prescribed = new TacticianPlanner(_tableState, _evaluator);
            prescribed.Prescribe(unit, naturalAction, naturalPlan);
            prescribed.BeginActivation(unit);
            string? prescribedAction = prescribed.ChooseAction(AllActions);
            List<ModelMoveEntry>? prescribedMove = prescribed.TakePlannedMove(unit);

            Assert.That(naturalAction, Is.Not.Null, "fixture must produce a real plan to compare against");
            Assert.That(prescribedAction, Is.EqualTo(naturalAction),
                "prescribing the policy's own pick must reproduce it - this is B0's control");
            Assert.That(prescribed.LastMacroLabel, Is.EqualTo(naturalMacro));
            Assert.That(prescribedMove, Is.Not.Null);
            AssertSameMove(prescribedMove!, naturalMove!);
        }

        [Test]
        public void PrescriptionSurvivesBeginActivation_SoUnitAndActionCanBePrescribedTogether()
        {
            // Search prescribes the whole edge at once, and BeginActivation runs BETWEEN the two
            // (the activation resolver calls it). If it cleared the pending action, every prescribed
            // activation would silently fall back to scoring.
            DataBinding<UnitData> unit = MakeUnit(_us, 3, Rifle(), atX: 20f, atZ: 24f);
            MacroAction hold = HoldPlan(unit);

            _planner.Prescribe(unit, ChooseActionStage.PASS_CHOICE_NAME, hold);
            Assert.That(_planner.HasPrescription, Is.True);
            _planner.BeginActivation(unit);

            Assert.That(_planner.ChooseAction(AllActions), Is.EqualTo(ChooseActionStage.PASS_CHOICE_NAME));
        }

        // --- (b) prescription steers, and skips the scorer ----------------------------------------

        [Test]
        public void PrescribedAction_OverridesWhatTheScorerWouldHavePicked()
        {
            // An objective in reach: the planner's own pick is a move onto it. Prescribing Pass must
            // win anyway - otherwise search cannot explore a branch the heuristic dislikes.
            _store.Create(new ObjectiveData(new Position(30f, 24f), _store));
            DataBinding<UnitData> unit = MakeUnit(_us, 3, Rifle(), atX: 20f, atZ: 24f);

            _planner.BeginActivation(unit);
            _planner.Prescribe(null, ChooseActionStage.PASS_CHOICE_NAME, HoldPlan(unit));

            Assert.That(_planner.ChooseAction(AllActions), Is.EqualTo(ChooseActionStage.PASS_CHOICE_NAME),
                "a prescribed action must beat the scorer's argmax");
        }

        [Test]
        public void PrescribedAction_DoesNotRunTheScorer()
        {
            // Where 5c's budget comes from: a prescribed activation must not enumerate and score
            // macro-actions. The decision log is written only by the scoring path, so its silence is
            // the observable.
            _store.Create(new ObjectiveData(new Position(30f, 24f), _store));
            var log = new List<string>();
            var logged = new TacticianPlanner(_tableState, _evaluator, log.Add);
            DataBinding<UnitData> unit = MakeUnit(_us, 3, Rifle(), atX: 20f, atZ: 24f);

            logged.BeginActivation(unit);
            logged.ChooseAction(AllActions);
            Assert.That(log, Is.Not.Empty, "the natural path narrates its scored candidate table");

            log.Clear();
            logged.BeginActivation(unit);
            logged.Prescribe(null, ChooseActionStage.PASS_CHOICE_NAME, HoldPlan(unit));
            logged.ChooseAction(AllActions);

            Assert.That(log, Is.Empty, "a prescribed action must skip enumeration and scoring entirely");
        }

        [Test]
        public void PrescribedActivation_GoesThroughTheResolverAndAnnouncesTheUnitToThePlanner()
        {
            // The heart of B0 finding 4: the prescribed unit must reach BeginActivation. If it does
            // not, ActiveUnit stays null and ChooseAction declines for the whole activation.
            DataBinding<UnitData> first = MakeUnit(_us, 3, Rifle(), atX: 20f, atZ: 24f);
            DataBinding<UnitData> second = MakeUnit(_us, 3, Rifle(), atX: 24f, atZ: 30f);
            MakeUnit(_them, 3, Rifle(), atX: 40f, atZ: 24f);
            var resolver = new TacticianActivationResolver(_tableState, _evaluator, _planner);

            _planner.Prescribe(second);
            DataBinding<UnitData> picked = resolver.Resolve(Request(first, second)).Result;

            Assert.That(picked.Reference, Is.EqualTo(second.Reference), "the prescribed unit must be taken");
            Assert.That(_planner.ActiveUnit, Is.Not.Null, "prescription must go THROUGH the planner");
            Assert.That(_planner.ActiveUnit!.Reference, Is.EqualTo(second.Reference));
        }

        [Test]
        public void NoPrescription_LeavesNaturalActivationUntouched()
        {
            // The neutrality half: unprescribed play must be bit-for-bit what it was, which is what
            // the DOP-1 hash cell checks at game scale.
            DataBinding<UnitData> first = MakeUnit(_us, 3, Rifle(), atX: 20f, atZ: 24f);
            DataBinding<UnitData> second = MakeUnit(_us, 3, Rifle(), atX: 24f, atZ: 30f);
            MakeUnit(_them, 3, Rifle(), atX: 40f, atZ: 24f);

            var scored = new TacticianActivationResolver(_tableState, _evaluator,
                new TacticianPlanner(_tableState, _evaluator));
            DataBinding<UnitData> natural = scored.Resolve(Request(first, second)).Result;

            var withSeam = new TacticianActivationResolver(_tableState, _evaluator, _planner);
            DataBinding<UnitData> unprescribed = withSeam.Resolve(Request(first, second)).Result;

            Assert.That(unprescribed.Reference, Is.EqualTo(natural.Reference));
        }

        // --- fall-through discipline (G3: never fault, never half-state) ---------------------------

        [Test]
        public void StalePrescribedUnit_FallsBackToScoringInsteadOfFaulting()
        {
            // A search branch built against a different situation: the prescribed unit is not among
            // the options the engine is offering now.
            DataBinding<UnitData> offered = MakeUnit(_us, 3, Rifle(), atX: 20f, atZ: 24f);
            DataBinding<UnitData> absent = MakeUnit(_us, 3, Rifle(), atX: 24f, atZ: 30f);
            MakeUnit(_them, 3, Rifle(), atX: 40f, atZ: 24f);
            var resolver = new TacticianActivationResolver(_tableState, _evaluator, _planner);

            _planner.Prescribe(absent);
            DataBinding<UnitData> picked = resolver.Resolve(Request(offered)).Result;

            Assert.That(picked.Reference, Is.EqualTo(offered.Reference));
            Assert.That(_planner.ActiveUnit!.Reference, Is.EqualTo(offered.Reference),
                "the fallback pick must still be announced to the planner");
            Assert.That(_planner.HasPrescription, Is.False, "a stale prescription must not linger");
        }

        [Test]
        public void StalePrescribedAction_FallsBackToScoring()
        {
            _store.Create(new ObjectiveData(new Position(30f, 24f), _store));
            DataBinding<UnitData> unit = MakeUnit(_us, 3, Rifle(), atX: 20f, atZ: 24f);

            _planner.BeginActivation(unit);
            _planner.Prescribe(null, "Not An Offered Action", HoldPlan(unit));
            string? action = _planner.ChooseAction(AllActions);

            Assert.That(action, Is.EqualTo(ChooseActionStage.MOVEMENT_CHOICE_NAME),
                "an action the engine is not offering must fall through to the scorer");
        }

        [Test]
        public void PlanBearingActionWithoutItsMacroAction_IsRefusedRatherThanLeavingAHalfState()
        {
            // Movement with no MacroAction would leave the movement resolver with no cached move and
            // the re-entry branch undecided - a state natural play never produces. Refuse it.
            _store.Create(new ObjectiveData(new Position(30f, 24f), _store));
            DataBinding<UnitData> unit = MakeUnit(_us, 3, Rifle(), atX: 20f, atZ: 24f);

            _planner.BeginActivation(unit);
            _planner.Prescribe(null, ChooseActionStage.MOVEMENT_CHOICE_NAME, macroAction: null);
            string? action = _planner.ChooseAction(AllActions);

            Assert.That(action, Is.EqualTo(ChooseActionStage.MOVEMENT_CHOICE_NAME));
            Assert.That(_planner.TakePlannedMove(unit), Is.Not.Null,
                "the scorer must have supplied a real plan, not the empty prescription");
        }

        [Test]
        public void ClearPrescription_RestoresNaturalScoring()
        {
            _store.Create(new ObjectiveData(new Position(30f, 24f), _store));
            DataBinding<UnitData> unit = MakeUnit(_us, 3, Rifle(), atX: 20f, atZ: 24f);

            _planner.BeginActivation(unit);
            _planner.Prescribe(null, ChooseActionStage.PASS_CHOICE_NAME, HoldPlan(unit));
            _planner.ClearPrescription();

            Assert.That(_planner.HasPrescription, Is.False);
            Assert.That(_planner.ChooseAction(AllActions), Is.EqualTo(ChooseActionStage.MOVEMENT_CHOICE_NAME));
        }

        // --- helpers -------------------------------------------------------------------------------

        // The planner's own winning candidate for this activation, recovered from the generator the
        // same way ChooseAction builds it - what a search edge would carry.
        private MacroAction PlanFor(DataBinding<UnitData> unit, string action)
        {
            List<MacroAction> candidates = MacroActionGenerator.Enumerate(_evaluator, _tableState, unit);
            var scorer = new TacticianPlanner(_tableState, _evaluator);
            scorer.BeginActivation(unit);
            MacroAction? best = null;
            float bestScore = float.NegativeInfinity;
            foreach (MacroAction candidate in candidates)
            {
                float score = scorer.Score(candidate);
                if (score <= bestScore) continue;
                bestScore = score;
                best = candidate;
            }
            Assert.That(best, Is.Not.Null, $"no candidate for the {action} plan under test");
            return best!;
        }

        private MacroAction HoldPlan(DataBinding<UnitData> unit) =>
            new(EMacroIntent.Hold, "test prescription", Rules.Definitions.EActionType.Advance,
                new List<ModelMoveEntry>(), EFeasibility.Reachable, Centroid(unit));

        private static void AssertSameMove(List<ModelMoveEntry> actual, List<ModelMoveEntry> expected)
        {
            Assert.That(actual.Count, Is.EqualTo(expected.Count), "same number of moved models");
            for (int i = 0; i < expected.Count; i++)
            {
                Position a = actual[i].Positions[^1], e = expected[i].Positions[^1];
                Assert.That(a.x, Is.EqualTo(e.x).Within(0.0001f));
                Assert.That(a.z, Is.EqualTo(e.z).Within(0.0001f));
            }
        }

        private ChooseUnitToActivateRequest Request(params DataBinding<UnitData>[] options) =>
            new(_us, options.Select(o => new SelectionRequest<UnitData>.ValidOption(o, o.GetValue().Name)).ToList(),
                new List<SelectionRequest<UnitData>.InvalidOption>());

        private static Position Centroid(DataBinding<UnitData> unit)
        {
            var alive = unit.GetValue().Models.Where(m => m.GetIsAlive()).ToList();
            return alive.Count == 0
                ? new Position(0f, 0f)
                : new Position(alive.Average(m => m.Position.x), alive.Average(m => m.Position.z));
        }

        private static Weapon Rifle() => new Weapon("Rifle", rangeInches: 24f, attacks: 1, armorPenetration: 0);

        private DataBinding<UnitData> MakeUnit(PlayerID owner, int modelCount, Weapon weapon,
            float atX, float atZ, int quality = 4, int defense = 4)
        {
            var modelBindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon> { weapon },
                    new Position(atX + (i % 2) * 1.1f, atZ + (i / 2) * 1.1f), _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(owner, $"U{atX}_{atZ}", quality, defense, modelBindings: modelBindings);
            var binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(owner, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }
    }
}
