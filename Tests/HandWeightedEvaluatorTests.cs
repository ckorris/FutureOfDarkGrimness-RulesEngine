using FDG.Ai.Tactician.Search;
using FDG.Data;
using FDG.Rules.Dispatch;
using NUnit.Framework;

namespace FDG.Tests
{
    // #191 B3 (campaign step 7, docs/tactician-bc-campaign.md): the hand-weighted evaluator's own
    // pins, on top of the shared two-side constraint test in TacticianActionSpaceTests. Losing a
    // unit lowers own value; seizing an objective raises it; a 1v1 board and its reduced 2v2 form
    // (a zero-unit ally on each side) evaluate identically (G13's shape invariant).
    [TestFixture]
    public class HandWeightedEvaluatorTests
    {
        private GameDataStore _store = null!;
        private TableState _tableState = null!;
        private RuleEvaluator _evaluator = null!;
        private HandWeightedEvaluator _handWeighted = null!;
        private PlayerID _us;
        private PlayerID _them;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _tableState = new TableState(_store);
            _evaluator = new RuleEvaluator(new ProbabilisticDiceRoller());
            _handWeighted = new HandWeightedEvaluator();
            _us = new PlayerID(Guid.NewGuid());
            _them = new PlayerID(Guid.NewGuid());
        }

        [Test]
        public void LosingAUnit_LowersOwnValue()
        {
            MakeUnit(_us, 3, atX: 10f, atZ: 10f);
            DataBinding<UnitData> ourCasualty = MakeUnit(_us, 3, atX: 10f, atZ: 20f);
            MakeUnit(_them, 3, atX: 40f, atZ: 10f);

            SideMap sides = SideMap.FromSlots(new[] { (_us, 0), (_them, 1) });
            float before = _handWeighted.Evaluate(_tableState, _evaluator, sides)[sides.SideOf(_us)];

            Kill(ourCasualty);

            float after = _handWeighted.Evaluate(_tableState, _evaluator, sides)[sides.SideOf(_us)];
            Assert.That(after, Is.LessThan(before),
                $"losing a unit must lower own value: before={before:F4} after={after:F4}");
        }

        [Test]
        public void SeizingAnObjective_RaisesValue()
        {
            DataBinding<UnitData> ours = MakeUnit(_us, 3, atX: 10f, atZ: 10f);
            MakeUnit(_them, 3, atX: 40f, atZ: 10f);
            _store.Create(new ObjectiveData(new Position(30f, 30f), _store));

            SideMap sides = SideMap.FromSlots(new[] { (_us, 0), (_them, 1) });
            float before = _handWeighted.Evaluate(_tableState, _evaluator, sides)[sides.SideOf(_us)];

            // Move onto the objective (seizure radius 3", TacticalAnalysis.ObjectiveSeizureRadiusInches).
            foreach (DataBinding<ModelData> model in ours.GetValue().ModelBindings)
                model.GetValue().PositionBinding.SetValue(new Position(30f, 30f));

            float after = _handWeighted.Evaluate(_tableState, _evaluator, sides)[sides.SideOf(_us)];
            Assert.That(after, Is.GreaterThan(before),
                $"seizing an objective must raise value: before={before:F4} after={after:F4}");
        }

        [Test]
        public void OneVOne_AndTheReducedTwoVTwo_EvaluateIdentically()
        {
            MakeUnit(_us, 3, atX: 10f, atZ: 10f);
            MakeUnit(_them, 3, atX: 40f, atZ: 10f);
            _store.Create(new ObjectiveData(new Position(15f, 10f), _store));

            SideMap oneVOne = SideMap.FromSlots(new[] { (_us, 0), (_them, 1) });
            SideValues oneVOneValues = _handWeighted.Evaluate(_tableState, _evaluator, oneVOne);

            // The reduced 2v2 shape (G13): a teammate and an opposing ally with NO living units on
            // the table - neither has an ArmyData, so every LivingUnits/RosterCount scan over them
            // contributes exactly zero (PositionEncoder.ComputeBlock, verified by construction).
            var emptyAlly = new PlayerID(Guid.NewGuid());
            var emptyEnemyAlly = new PlayerID(Guid.NewGuid());
            SideMap twoVTwo = SideMap.FromSlots(new[]
                { (_us, 0), (emptyAlly, 0), (_them, 1), (emptyEnemyAlly, 1) });
            SideValues twoVTwoValues = _handWeighted.Evaluate(_tableState, _evaluator, twoVTwo);

            Assert.That(twoVTwoValues[twoVTwo.SideOf(_us)],
                Is.EqualTo(oneVOneValues[oneVOne.SideOf(_us)]).Within(1e-6f),
                "a zero-unit ally on each side must not change the evaluation (G13 shape invariant)");
        }

        [Test]
        public void Evaluate_PerLeafCost_IsReportedAndSane()
        {
            // A moderate board (10 units/side, ~2k-scale unit count) so the number is representative
            // of B4's leaf cost, not an empty-board floor. #191 B3: Evaluate calls EncodeSideBlock
            // once PER SIDE (two, here), unlike step 4's exporter which called it once per boundary.
            for (int i = 0; i < 10; i++) MakeUnit(_us, 3, atX: 10f + i, atZ: 10f + i * 2f);
            for (int i = 0; i < 10; i++) MakeUnit(_them, 3, atX: 40f + i, atZ: 10f + i * 2f);
            for (int i = 0; i < 3; i++) _store.Create(new ObjectiveData(new Position(20f + i * 8f, 15f), _store));
            SideMap sides = SideMap.FromSlots(new[] { (_us, 0), (_them, 1) });

            _handWeighted.Evaluate(_tableState, _evaluator, sides); // warm up (JIT, first-call caches)
            const int reps = 50;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < reps; i++) _handWeighted.Evaluate(_tableState, _evaluator, sides);
            sw.Stop();

            double meanMs = sw.Elapsed.TotalMilliseconds / reps;
            TestContext.WriteLine(
                $"HandWeightedEvaluator.Evaluate per-leaf (2 sides, 20 units, 3 objectives): {meanMs:F3}ms");
            Assert.That(meanMs, Is.LessThan(50f),
                "sanity bound only - see the ledger for the real per-leaf budget discussion");
        }

        // --- helpers -------------------------------------------------------------------------------

        private static void Kill(DataBinding<UnitData> unit)
        {
            // GetIsAlive is WoundsDealt < TotalWounds, i.e. RemainingWounds > 0 - zero it to kill.
            foreach (DataBinding<ModelData> model in unit.GetValue().ModelBindings)
                model.GetValue().RemainingWoundsBinding.SetValue(0f);
        }

        private DataBinding<UnitData> MakeUnit(PlayerID owner, int modelCount, float atX, float atZ,
            int quality = 4, int defense = 4)
        {
            var weapon = new Weapon("Rifle", rangeInches: 24f, attacks: 1, armorPenetration: 0);
            var modelBindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon> { weapon },
                    new Position(atX + (i % 2) * 1.1f, atZ + (i / 2) * 1.1f), _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(owner, $"U{owner}_{atX}_{atZ}", quality, defense, modelBindings: modelBindings);
            var binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(owner, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }
    }
}
