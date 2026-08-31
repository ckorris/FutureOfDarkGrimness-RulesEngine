using FDG.Ai.Tactician;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #389 option 2 — the movement-argmax half of the walled-rear-shooter pathology: the
    // projected-threat forecast had a hard band edge (an endpoint one inch beyond the enemy's
    // projected reach paid ZERO, one inch inside paid FULL), so a lateral slide whose endpoint sat
    // just outside the band banked a ~3x "safety" discount over a clipped forward stub just inside
    // it - and won the argmax on approach credit it hadn't earned. The edge is now a ramp: the
    // forecast decays linearly over one more enemy advance (ArrivalRamp), because a threat already
    // priced two moves out arrives half an advance later, not never.
    [TestFixture]
    public class TacticianArrivalRampTests
    {
        private GameDataStore _store = null!;
        private TableState _tableState = null!;
        private RuleEvaluator _evaluator = null!;
        private PlayerID _us;
        private PlayerID _them;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _tableState = new TableState(_store);
            _evaluator = new RuleEvaluator(new ProbabilisticDiceRoller());
            _us = new PlayerID(Guid.NewGuid());
            _them = new PlayerID(Guid.NewGuid());
        }

        // --- the ramp itself ----------------------------------------------------------------------

        [Test]
        public void ArrivalRamp_InsideTheBand_IsFull()
        {
            Assert.That(TacticianPlanner.ArrivalRamp(0f, 6f), Is.EqualTo(1f));
            Assert.That(TacticianPlanner.ArrivalRamp(-5f, 6f), Is.EqualTo(1f));
        }

        [Test]
        public void ArrivalRamp_DecaysLinearlyOverOneClosingStep()
        {
            Assert.That(TacticianPlanner.ArrivalRamp(3f, 6f), Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(TacticianPlanner.ArrivalRamp(6f, 6f), Is.EqualTo(0f).Within(0.001f));
            Assert.That(TacticianPlanner.ArrivalRamp(9f, 6f), Is.EqualTo(0f));
        }

        [Test]
        public void ArrivalRamp_DegenerateClosingStep_FallsBackToOneInch()
        {
            // A zero-mobility enemy still gets a finite (1") edge instead of a division blow-up.
            Assert.That(TacticianPlanner.ArrivalRamp(0.5f, 0f), Is.EqualTo(0.5f).Within(0.001f));
        }

        // --- the Score-level consequence ----------------------------------------------------------

        [Test]
        public void Score_JustOutsideTheProjectedBand_PaysAPartialForecast()
        {
            // One arriving enemy gunline, nothing else on the board: every other Score term is
            // zero, so the score IS the (negated) projected-threat forecast. Pre-#389-option-2 the
            // endpoints at z=17 and z=10 both sat outside the projected band and scored identically
            // (zero forecast); the ramp makes the near-outside endpoint pay a partial premium, so
            // the scores must now be strictly ordered by depth: inside < just-outside < far-outside.
            DataBinding<UnitData> us = MakeUnitAt(_us, 5, Rifle(), i => new Position(24f + i * 1.1f, 20f));
            MakeUnitAt(_them, 3, Rifle(), i => new Position(24f + i * 1.1f, 60f));
            var planner = new TacticianPlanner(_tableState, _evaluator);
            planner.BeginActivation(us);

            float inside = planner.Score(HoldAt(new Position(25f, 26f)));
            float justOutside = planner.Score(HoldAt(new Position(25f, 17f)));
            float farOutside = planner.Score(HoldAt(new Position(25f, 10f)));

            Assert.That(inside, Is.LessThan(justOutside),
                $"deeper inside the band must pay more (inside={inside:F4}, justOutside={justOutside:F4})");
            Assert.That(justOutside, Is.LessThan(farOutside),
                "one inch outside the band is no longer free - the forecast ramps, it does not cliff "
                + $"(justOutside={justOutside:F4}, farOutside={farOutside:F4})");
            Assert.That(farOutside, Is.EqualTo(0f).Within(0.0001f),
                "a full closing-step beyond the band the forecast is spent");
        }

        // --- helpers ------------------------------------------------------------------------------

        private static MacroAction HoldAt(Position end) => new(EMacroIntent.Hold, "test",
            EActionType.Advance, new List<ModelMoveEntry>(), EFeasibility.Reachable, end);

        private static Weapon Rifle() => new Weapon("Rifle", rangeInches: 24f, attacks: 1, armorPenetration: 0);

        private DataBinding<UnitData> MakeUnitAt(PlayerID owner, int modelCount, Weapon weapon,
            Func<int, Position> positionFor)
        {
            var modelBindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon> { weapon }, positionFor(i), _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(owner, $"U{owner.GetHashCode() % 100}-{modelCount}", quality: 4,
                defense: 4, modelBindings: modelBindings);
            var binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(owner, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }
    }
}
