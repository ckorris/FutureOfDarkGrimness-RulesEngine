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

        // --- step 10 (2026-09-05) pins for the revised evaluator ----------------------------------
        // Each of these FAILS on the 0.55/0.30/0.15 three-feature version and passes on the revision;
        // together they are the B-gate failure analysis's measured defect, turned into a contract.

        [Test]
        public void EarlyInTheGame_KillingAThirdOfTheEnemy_IsWorthAMeaningfulFractionOfAMarker()
        {
            // 10 units a side, 3 markers well away from everyone. Old evaluator: this kill moves value
            // by 0.026 and the marker by 0.092 (ratio 0.29). Revised: 0.049 vs 0.041 (1.2). The bar
            // sits between, so it separates the two cleanly in both directions.
            var ours = new List<DataBinding<UnitData>>();
            var theirs = new List<DataBinding<UnitData>>();
            for (int i = 0; i < 10; i++) ours.Add(MakeUnit(_us, 3, atX: 10f + i, atZ: 10f + i * 2f));
            for (int i = 0; i < 10; i++) theirs.Add(MakeUnit(_them, 3, atX: 60f + i, atZ: 10f + i * 2f));
            for (int i = 0; i < 3; i++) _store.Create(new ObjectiveData(new Position(20f + i * 15f, 44f), _store));
            SideMap sides = SideMap.FromSlots(new[] { (_us, 0), (_them, 1) });
            int us = sides.SideOf(_us);

            float baseline = _handWeighted.Evaluate(_tableState, _evaluator, sides)[us];

            foreach (DataBinding<ModelData> model in ours[0].GetValue().ModelBindings)
                model.GetValue().PositionBinding.SetValue(new Position(20f, 44f));
            float markerGain = _handWeighted.Evaluate(_tableState, _evaluator, sides)[us] - baseline;
            foreach (DataBinding<ModelData> model in ours[0].GetValue().ModelBindings)
                model.GetValue().PositionBinding.SetValue(new Position(10f, 10f));

            for (int i = 0; i < 3; i++) Kill(theirs[i]);
            float killGain = _handWeighted.Evaluate(_tableState, _evaluator, sides)[us] - baseline;

            Assert.That(markerGain, Is.GreaterThan(0f), "seizing a marker must still count");
            Assert.That(killGain / markerGain, Is.GreaterThan(0.6f),
                $"early in the game a 30% kill must be worth a real fraction of a marker: kill={killGain:F4} marker={markerGain:F4} ratio={killGain / markerGain:F2}");
        }

        [Test]
        public void ApproachingAMarker_RaisesValue_BeforeReachingIt()
        {
            // The flat-landscape defect: between markers the old evaluator's objective term did not
            // move at all, so this delta was exactly zero and the search had nothing to climb.
            DataBinding<UnitData> ours = MakeUnit(_us, 3, atX: 10f, atZ: 10f);
            MakeUnit(_them, 3, atX: 60f, atZ: 40f);
            _store.Create(new ObjectiveData(new Position(40f, 10f), _store));
            SideMap sides = SideMap.FromSlots(new[] { (_us, 0), (_them, 1) });
            int us = sides.SideOf(_us);

            float far = _handWeighted.Evaluate(_tableState, _evaluator, sides)[us]; // 30" away
            foreach (DataBinding<ModelData> model in ours.GetValue().ModelBindings)
                model.GetValue().PositionBinding.SetValue(new Position(25f, 10f)); // 15" away, still outside 3"
            float nearer = _handWeighted.Evaluate(_tableState, _evaluator, sides)[us];

            Assert.That(nearer, Is.GreaterThan(far),
                $"closing on a marker without reaching it must raise value: far={far:F4} nearer={nearer:F4}");
        }

        [Test]
        public void LateInTheGame_MarkersOutweighMaterial_MoreThanEarly()
        {
            // Same board and same two perturbations at round 1 and at round 4. Material's weight
            // decays with the clock, so kill/marker at round 4 must be well under kill/marker at round 1,
            // and by round 4 a marker must simply be worth more than the kill.
            float RatioAtRound(int round)
            {
                SetUp();
                var ours = new List<DataBinding<UnitData>>();
                var theirs = new List<DataBinding<UnitData>>();
                for (int i = 0; i < 10; i++) ours.Add(MakeUnit(_us, 3, atX: 10f + i, atZ: 10f + i * 2f));
                for (int i = 0; i < 10; i++) theirs.Add(MakeUnit(_them, 3, atX: 60f + i, atZ: 10f + i * 2f));
                for (int i = 0; i < 3; i++) _store.Create(new ObjectiveData(new Position(20f + i * 15f, 44f), _store));
                SetRound(round);
                SideMap sides = SideMap.FromSlots(new[] { (_us, 0), (_them, 1) });
                int us = sides.SideOf(_us);
                float baseline = _handWeighted.Evaluate(_tableState, _evaluator, sides)[us];
                foreach (DataBinding<ModelData> model in ours[0].GetValue().ModelBindings)
                    model.GetValue().PositionBinding.SetValue(new Position(20f, 44f));
                float markerGain = _handWeighted.Evaluate(_tableState, _evaluator, sides)[us] - baseline;
                foreach (DataBinding<ModelData> model in ours[0].GetValue().ModelBindings)
                    model.GetValue().PositionBinding.SetValue(new Position(10f, 10f));
                for (int i = 0; i < 3; i++) Kill(theirs[i]);
                float killGain = _handWeighted.Evaluate(_tableState, _evaluator, sides)[us] - baseline;
                TestContext.WriteLine($"round {round}: marker={markerGain:F4} kill={killGain:F4} ratio={killGain / markerGain:F2}");
                return killGain / markerGain;
            }

            float early = RatioAtRound(1);
            float late = RatioAtRound(4);
            Assert.That(late, Is.LessThan(early * 0.5f), "material must matter much less at round 4 than at round 1");
            Assert.That(late, Is.LessThan(1f), "by round 4 a marker must outweigh a 30% kill");
        }

        [Test]
        public void KillingTheUnitThreateningOurMarker_IsWorthFarMoreThanKillingADistantOne()
        {
            // v2 (Chris's round-4 point): material is otherwise fungible. Two identical enemy units;
            // one sits 10" from the marker we hold, one 60" away. Killing the near one must be worth
            // several times killing the far one - on the old evaluator they were worth the same.
            MakeUnit(_us, 3, atX: 30f, atZ: 30f); // holds the marker
            MakeUnit(_us, 3, atX: 10f, atZ: 10f);
            _store.Create(new ObjectiveData(new Position(30f, 30f), _store));
            DataBinding<UnitData> near = MakeUnit(_them, 3, atX: 30f, atZ: 40f);
            DataBinding<UnitData> far = MakeUnit(_them, 3, atX: 30f, atZ: 90f);
            SetRound(4);
            SideMap sides = SideMap.FromSlots(new[] { (_us, 0), (_them, 1) });
            int us = sides.SideOf(_us);
            float baseline = _handWeighted.Evaluate(_tableState, _evaluator, sides)[us];

            Kill(far);
            float farGain = _handWeighted.Evaluate(_tableState, _evaluator, sides)[us] - baseline;
            SetUp(); // rebuild the identical board
            MakeUnit(_us, 3, atX: 30f, atZ: 30f); MakeUnit(_us, 3, atX: 10f, atZ: 10f);
            _store.Create(new ObjectiveData(new Position(30f, 30f), _store));
            near = MakeUnit(_them, 3, atX: 30f, atZ: 40f); far = MakeUnit(_them, 3, atX: 30f, atZ: 90f);
            SetRound(4);
            sides = SideMap.FromSlots(new[] { (_us, 0), (_them, 1) }); us = sides.SideOf(_us);
            baseline = _handWeighted.Evaluate(_tableState, _evaluator, sides)[us];
            Kill(near);
            float nearGain = _handWeighted.Evaluate(_tableState, _evaluator, sides)[us] - baseline;

            TestContext.WriteLine($"round 4: kill far={farGain:F4} kill near(threatening our marker)={nearGain:F4}");
            Assert.That(farGain, Is.GreaterThan(0f));
            // The premium is the threatened-marker discount coming off: half a held marker at round
            // 4's objective weight (0.66 x 0.70 x 0.5 / 2 ~ 0.116 for a single marker). Old evaluator:
            // near == far exactly.
            Assert.That(nearGain - farGain, Is.GreaterThan(0.08f),
                $"the premium for the unit that can take our marker must be about half a marker: near={nearGain:F4} far={farGain:F4}");
            Assert.That(nearGain, Is.GreaterThan(farGain * 2f),
                $"and at least double the distant twin: near={nearGain:F4} far={farGain:F4}");
        }

        [Test]
        public void GameProgress_ReadsRoundAndActivationFraction()
        {
            MakeUnit(_us, 3, atX: 10f, atZ: 10f);
            MakeUnit(_them, 3, atX: 40f, atZ: 10f);
            Assert.That(HandWeightedEvaluator.GameProgress(_tableState), Is.EqualTo(0f).Within(1e-6f),
                "no progress record reads as the start of round 1");
            SetRound(3);
            Assert.That(HandWeightedEvaluator.GameProgress(_tableState), Is.EqualTo(0.5f).Within(1e-6f),
                "start of round 3 of 4 is halfway");
        }

        private void SetRound(int round)
        {
            var progress = new GameProgressData(EResumeStage.MainPhase, round,
                new List<int>(), new List<int>(), 0, new Dictionary<int, int>(),
                new List<DataBinding<UnitData>>(), GameSettings.GetDefault());
            GameProgressUtilities.WriteProgress(_store, progress);
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
