using FDG.Ai.Tactician;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.StageResolution.Requests;
using FDG.Utilities;
using NUnit.Framework;

namespace FDG.Tests
{
    // #365 Tier 2 (slice 2c) - the lethality VETO. Tier 1 is a habit that shapes HOW a unit
    // travels; this is the only term allowed to decide WHETHER it goes at all:
    //
    //     penalty = MoveLethality x P(destroyed outright) x (what this unit would still have done)
    //
    // P is a wipeout-only ramp: identically ZERO until the ranked threat estimate nears the unit's
    // remaining wounds. The shape is the hard-won part. Two continuous formulations (a morale-knee
    // curve over three different aggregations, W 0.4-1.7) lost 4-14pp on the 640-game pool,
    // monotonically in how much threat they perceived - because in an argmax over one unit's
    // candidates, f(threat) x candidate-constant is just a second retaliation term, whatever the
    // curve. "Goals dominate except at certain death" (Chris) is expressible additively only as a
    // term that is zero almost everywhere. The morale knee and quality scaling died with that
    // finding (pin 9 cut, see the #365 ledger); pricing the FORFEITED CONTRIBUTION survived it,
    // and is still what makes the doomed remnant rush instead of freeze (case 12).
    //
    // Cases that isolate the veto score the same scene with the gate on and off - asserting the
    // outcome alone would let retaliation pass the pin on its own and leave the veto untested.
    // Mutates process-global statics, so it must not run alongside anything that scores.
    [TestFixture, NonParallelizable]
    public class TacticianLethalityGateTests
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

        // Captured in OneTimeSetUp, NOT a static field initialiser. This type is beforefieldinit, so
        // an initialiser runs on FIRST ACCESS of the field - and the first access is inside TearDown
        // (or Calibrate's restore), by which point a test has already zeroed the weight. That
        // captures 0 as "the shipped default" and silently disables the gate for every later test in
        // the run. Cost me a full calibration pass; OneTimeSetUp is ordered before any test body.
        private static float ShippedLethality;

        [OneTimeSetUp]
        public void CaptureShippedWeights() => ShippedLethality = TacticianWeights.MoveLethality;

        [TearDown]
        public void TearDown() => TacticianWeights.MoveLethality = ShippedLethality;

        // --- Case 6: the exchange rate. Chris: "it should happily walk through an open field if it
        // means taking an objective next turn and it's going to lose 2 out of 10 models." ---

        [Test]
        public void Score_ObjectiveWorthTwoOfTenModels_RushesAnyway()
        {
            (float rush, float hold) = RushScene(enemyModels: 8);

            Assert.That(rush, Is.GreaterThan(hold),
                $"a volley that costs about 2 of 10 models sits below the half-strength knee, where " +
                $"the gate is flat ZERO by construction - taking the marker must stay free. A gate " +
                $"that charges for ordinary casualties turns every objective into a stand-off. " +
                $"rush={rush:F4} hold={hold:F4}");
        }

        // --- Case 7: and the other end of it - certain death is not an exchange rate. ---

        [Test]
        public void Score_ExpectedWipeout_Balks()
        {
            (float rush, float hold) = RushScene(enemyModels: 40);
            TacticianWeights.MoveLethality = 0f;
            (float rushUngated, float holdUngated) = RushScene(enemyModels: 40);

            Assert.That(rushUngated, Is.GreaterThan(holdUngated),
                $"scene check: with the gate off the marker must still look worth taking, otherwise " +
                $"this pin proves nothing about the gate. rush={rushUngated:F4} hold={holdUngated:F4}");
            Assert.That(hold, Is.GreaterThan(rush),
                $"the same marker against a volley that wipes the unit out: the gate is the one term " +
                $"allowed to override a goal, and this is what it is for. " +
                $"rush={rush:F4} hold={hold:F4}");
        }

        // --- Case 8: same volley, and only the unit already pushed past half strength balks.
        // Under slice 2b this was the morale knee; under the veto it survives for a plainer
        // reason - five remaining wounds are twice as easy to WIPE as ten, so wipeout proximity
        // tracks being worn. The behaviour Chris asked for outlived the mechanism built for it. ---

        [Test]
        public void Score_SameVolley_OnlyTheUnitPushedPastHalfStrength_Balks()
        {
            int fresh = BalkThreshold(casualties: 0, quality: 4);
            int worn = BalkThreshold(casualties: 5, quality: 4);

            Assert.That(worn, Is.LessThan(fresh),
                $"the same marker, the same gunline, and the only difference is that one squad has " +
                $"already lost half its models: what wipes 5 wounds is far less than what wipes 10, " +
                $"so the worn squad must lose its nerve at a strictly smaller volley. " +
                $"balks at {worn} guns worn vs {fresh} guns fresh");

            (float freshRush, float freshHold) = RushScene(worn, ourCasualties: 0);
            (float wornRush, float wornHold) = RushScene(worn, ourCasualties: 5);
            TacticianWeights.MoveLethality = 0f;
            (float ungatedRush, float ungatedHold) = RushScene(worn, ourCasualties: 5);

            Assert.That(ungatedRush, Is.GreaterThan(ungatedHold),
                $"scene check: with the veto off the worn squad takes the marker too, so the flip " +
                $"below belongs to the veto and not to retaliation. " +
                $"rush={ungatedRush:F4} hold={ungatedHold:F4}");
            Assert.That(freshRush, Is.GreaterThan(freshHold),
                $"{worn} guns is nowhere near wiping a full-strength squad - the veto must be " +
                $"exactly silent. rush={freshRush:F4} hold={freshHold:F4}");
            Assert.That(wornHold, Is.GreaterThan(wornRush),
                $"the identical volley wipes the halved squad outright. " +
                $"rush={wornRush:F4} hold={wornHold:F4}");
        }

        // --- Case 9 (CUT with slice 2c, recorded in the #365 ledger): quality scaling at the
        // morale knee. A 3+ vs 5+ distinction only exists BELOW wipeout, and sub-wipeout pricing
        // is precisely what the pool forbade at every weight that let this pin resolve (the pin
        // demanded W >= 1.0; no aggregation was pool-neutral at 1.0). If quality-aware caution
        // ever returns, its home is retaliation's response curve, not a goal-overriding term. ---

        // --- Case 10: round decay, and it must be EMERGENT from the horizon, not its own scalar.
        // Objective-free scene, so the forfeiture is the attrition half alone - the half that
        // decays. Asserted on the veto's PRICE rather than as a behavioural flip: in an
        // objective-free scene, any gunline big enough to wipe the unit is priced so heavily by
        // retaliation that the ungated argmax already balks, so there is no window where "ungated
        // goes AND the veto fires" - recorded in the ledger, not hidden. The price form pins the
        // same mechanism exactly: a round-4 death of an off-objective unit forfeits NOTHING
        // (NUMBER_OF_ROUNDS = 4, every unit dies at the end anyway), so the veto must vanish
        // bit-for-bit, not merely shrink. ---

        [Test]
        public void Score_LethalTrade_VetoPricesItInRoundOne_AndIsFreeInTheFinalRound()
        {
            float earlyCost = VetoCostOnTheAdvance(round: 1);
            float lateCost = VetoCostOnTheAdvance(round: 4);

            Assert.That(earlyCost, Is.GreaterThan(0.15f),
                $"in round 1 a unit that dies for one volley forfeits three more rounds of work, " +
                $"and the veto must charge for them. cost={earlyCost:F4}");
            Assert.That(lateCost, Is.EqualTo(0f).Within(1e-6f),
                $"in the final round an off-objective death forfeits nothing, so the veto must be " +
                $"EXACTLY zero - the round scaling lives in the forfeiture, never in a weight. " +
                $"cost={lateCost:F4}");
        }

        /// <summary>The veto's price on the lethal advance, isolated by differencing gate-on and
        /// gate-off scores of the same scene.</summary>
        private float VetoCostOnTheAdvance(int round)
        {
            float shipped = ShippedLethality;
            TacticianWeights.MoveLethality = 0f;
            (float offAdvance, _) = TradeScene(round);
            TacticianWeights.MoveLethality = shipped;
            (float onAdvance, _) = TradeScene(round);
            return offAdvance - onAdvance;
        }

        // --- Case 12: the one Chris was most worried about. "You've got 2 models left of 10 and
        // it's surrounded by bad guys. But it can rush the objective. It still should, even if it's
        // GOING to get destroyed." ---

        [Test]
        public void Score_DoomedRemnant_RushesTheObjectiveInsteadOfFreezing()
        {
            (float rush, float hold) = SurroundedRemnantScene();

            Assert.That(rush, Is.GreaterThan(hold),
                $"this unit dies whichever candidate it picks, so P is ~1 everywhere and the gate is " +
                $"a CONSTANT that must cancel in the argmax and leave the goal deciding. Pricing the " +
                $"danger instead of the forfeiture would peak the penalty exactly here and freeze " +
                $"the unit in cover, contributing nothing, when it could have contested a marker and " +
                $"soaked a volley that would otherwise hit something that matters. " +
                $"rush={rush:F4} hold={hold:F4}");
        }

        // --- Case 19: the veto's threat estimate must see CONVERGENT fire without believing in
        // FOCUSED fire. Ranked decay: worst enemy at full weight, next at half, next at a quarter.
        // Each assertion kills one of the aggregations that was tried or considered - and they are
        // only observable near the wipeout line, because the veto is zero everywhere else. ---

        [Test]
        public void Score_VetoThreatEstimate_SeesConvergenceButNotMassedFocus()
        {
            // 28-model gunlines put ~7 expected wounds on the 10-wound squad - below the ramp
            // alone, past wipeout when two converge under decay (7 + 3.5 = 10.5).
            float oneBig = VetoCostOnTheRush(gunlines: 1, modelsEach: 28);
            float twoBig = VetoCostOnTheRush(gunlines: 2, modelsEach: 28);
            // 12-model gunlines put ~3 wounds each: a plain SUM of five reads 15 - "wiped out" -
            // while decay reads 5.8 and correctly stays silent.
            float fiveSmall = VetoCostOnTheRush(gunlines: 5, modelsEach: 12);

            Assert.That(oneBig, Is.EqualTo(0f).Within(1e-6f),
                $"one gunline that cannot wipe the squad is not a veto matter at all - the term is " +
                $"zero, not small. cost={oneBig:F4}");
            Assert.That(twoBig, Is.GreaterThan(0.1f),
                $"two of them CONVERGING is a wipeout, and the veto must see it - a MAX over " +
                $"enemies reads the pair as 7 wounds and walks the unit into the crossfire. " +
                $"one={oneBig:F4} two={twoBig:F4}");
            Assert.That(fiveSmall, Is.EqualTo(0f).Within(1e-6f),
                $"five small squads whose wounds SUM past wipeout must not veto - decay reads 5.8 " +
                $"of 8 needed. Believing every gun on the table fires at whoever moves is exactly " +
                $"the belief that cost -9.8pp on the pool. cost={fiveSmall:F4}");
        }

        /// <summary>The veto's price on the rush candidate, isolated by differencing gate-on and
        /// gate-off scores of the same scene.</summary>
        private float VetoCostOnTheRush(int gunlines, int modelsEach)
        {
            float shipped = ShippedLethality;
            TacticianWeights.MoveLethality = 0f;
            (float offRush, _) = CrossfireScene(gunlines, modelsEach);
            TacticianWeights.MoveLethality = shipped;
            (float onRush, _) = CrossfireScene(gunlines, modelsEach);
            return offRush - onRush;
        }

        // --- Scenes. ---

        /// <summary>
        /// A marker 12" upfield, a gunline beyond it, and a unit that can either step onto the
        /// marker (in range of the gunline) or stand still (out of it). The gunline's size is the
        /// only dial: it converts directly into expected wounds on the endpoint.
        /// </summary>
        private (float Rush, float Hold) RushScene(int enemyModels, int ourCasualties = 0,
            int quality = 4, int round = 1, float markerZ = 18f)
        {
            SetUp();
            SetRound(round);
            DataBinding<UnitData> us = Squad(_us, Rifle(), new Position(36f, 6f), models: 10,
                quality: quality);
            Squad(_them, Rifle(), new Position(36f, 44f), models: enemyModels);
            _store.Create(new ObjectiveData(new Position(36f, markerZ), _store));
            Wound(us, ourCasualties);

            var planner = new TacticianPlanner(_tableState, _evaluator);
            planner.BeginActivation(us);
            return (planner.Score(Endpoint(new Position(36f, 17f))),
                    planner.Score(Endpoint(new Position(36f, 6f))));
        }

        /// <summary>
        /// No objectives anywhere: a pure attrition trade. Advancing buys a shot at a gunline that
        /// will kill us for it; holding keeps us out of range and does nothing. Only the round
        /// changes between the two calls, so only the horizon can explain a different answer.
        /// </summary>
        private (float Advance, float Hold) TradeScene(int round, int enemyModels = 40)
        {
            SetUp();
            SetRound(round);
            DataBinding<UnitData> us = Squad(_us, Rifle(), new Position(36f, 12f), models: 10);
            Squad(_them, Rifle(), new Position(36f, 44f), models: enemyModels);

            var planner = new TacticianPlanner(_tableState, _evaluator);
            planner.BeginActivation(us);
            // (36,22) is inside OUR rifle range of the gunline and inside theirs; (36,12) is outside
            // both, so advancing buys a volley and pays for it, and holding does neither.
            return (planner.Score(Endpoint(new Position(36f, 22f))),
                    planner.Score(Endpoint(new Position(36f, 12f))));
        }

        /// <summary>
        /// Two models left of ten, enemies on every side so both candidates are lethal, and a marker
        /// within reach. The gate must come out equal on both and let the objective decide.
        /// </summary>
        private (float Rush, float Hold) SurroundedRemnantScene()
        {
            SetUp();
            SetRound(3);
            DataBinding<UnitData> us = Squad(_us, Rifle(), new Position(36f, 24f), models: 10);
            Squad(_them, Rifle(), new Position(36f, 40f), models: 20);
            Squad(_them, Rifle(), new Position(20f, 24f), models: 20);
            Squad(_them, Rifle(), new Position(52f, 24f), models: 20);
            Squad(_them, Rifle(), new Position(36f, 10f), models: 20);
            _store.Create(new ObjectiveData(new Position(36f, 32f), _store));
            Wound(us, 8);

            var planner = new TacticianPlanner(_tableState, _evaluator);
            planner.BeginActivation(us);
            return (planner.Score(Endpoint(new Position(36f, 31f))),
                    planner.Score(Endpoint(new Position(36f, 24f))));
        }

        // --- Calibration harness: prints the bracket MoveLethality has to sit inside. ---

        /// <summary>
        /// The same marker rush, but the fire comes from N separate gunlines of EQUAL size spread
        /// on an arc at roughly equal range, so every one of them bears on the endpoint and none is
        /// nearer than another. Each added gunline is genuinely more incoming, which is what makes
        /// "does it add up, and does it stop adding up" a meaningful question to ask.
        /// </summary>
        private (float Rush, float Hold) CrossfireScene(int gunlines, int modelsEach)
        {
            SetUp();
            SetRound(1);
            DataBinding<UnitData> us = Squad(_us, Rifle(), new Position(36f, 6f), models: 10);
            Position[] arc =
            {
                new Position(36f, 40f), new Position(22f, 36f), new Position(50f, 36f),
                new Position(14f, 28f), new Position(58f, 28f),
            };
            for (int i = 0; i < gunlines && i < arc.Length; i++)
                Squad(_them, Rifle(), arc[i], models: modelsEach);
            _store.Create(new ObjectiveData(new Position(36f, 18f), _store));

            var planner = new TacticianPlanner(_tableState, _evaluator);
            planner.BeginActivation(us);
            return (planner.Score(Endpoint(new Position(36f, 17f))),
                    planner.Score(Endpoint(new Position(36f, 6f))));
        }

        [Test, Explicit("calibration harness - prints the bracket, asserts nothing")]
        public void Calibrate()
        {
            float shipped = ShippedLethality;
            TestContext.Out.WriteLine("smallest gunline (models) that makes the unit refuse the marker:");
            foreach (float w in new[] { 0.4f, 0.5f, 0.6f, 0.8f, 1.0f, 1.5f })
            {
                TacticianWeights.MoveLethality = w;
                TestContext.Out.WriteLine($"  W={w:F1}  freshQ4={BalkThreshold(0, 4),4} " +
                    $"wornQ4={BalkThreshold(5, 4),4}");
            }
            TacticianWeights.MoveLethality = shipped;
            foreach (int round in new[] { 1, 2, 3, 4 })
                TestContext.Out.WriteLine($"  trade veto cost r{round}: {VetoCostOnTheAdvance(round):F4}");
            (float dRush, float dHold) = SurroundedRemnantScene();
            TestContext.Out.WriteLine($"  doomed remnant: margin={dRush - dHold:+0.0000;-0.0000} " +
                $"-> {(dRush > dHold ? "RUSHES" : "FREEZES")}");
        }

        /// <summary>Smallest gunline (in models) that makes this unit refuse the marker.</summary>
        private int BalkThreshold(int casualties, int quality)
        {
            for (int guns = 2; guns <= 120; guns += 2)
            {
                (float rush, float hold) = RushScene(guns, casualties, quality);
                if (hold > rush) return guns;
            }
            return int.MaxValue;
        }

        // --- Helpers. ---

        private static Weapon Rifle() => new Weapon("Rifle", rangeInches: 24f, attacks: 1, armorPenetration: 0);

        private void SetRound(int round) => _store.Create(new GameProgressData(
            EResumeStage.MainPhase, round, new List<int>(), new List<int>(), 0,
            new Dictionary<int, int>(), new List<DataBinding<UnitData>>(), GameSettings.GetDefault()));

        /// <summary>Kills whole models off the front of the unit (each carries one wound).</summary>
        private static void Wound(DataBinding<UnitData> unit, int models)
        {
            List<DataBinding<ModelData>> alive = unit.GetValue().ModelBindings;
            for (int i = 0; i < models && i < alive.Count; i++) alive[i].GetValue().DealWounds(1f);
        }

        private DataBinding<UnitData> Squad(PlayerID owner, Weapon weapon, Position centre,
            int models, int quality = 4)
        {
            // Laid out SYMMETRICALLY about `centre`, so the unit's centroid IS `centre` whatever the
            // model count. A one-directional block walks the centroid away as the squad grows - at
            // 60 models it drifted ~5", which put a gunline out of range of the very endpoint the
            // scene existed to threaten, and the scene silently scored nothing at all.
            const int cols = 5;
            int rows = (models + cols - 1) / cols;
            var bindings = new List<DataBinding<ModelData>>(models);
            for (int i = 0; i < models; i++)
            {
                var at = new Position(
                    centre.x + ((i % cols) - (cols - 1) / 2f) * 1.1f,
                    centre.z + ((i / cols) - (rows - 1) / 2f) * 1.1f);
                bindings.Add(_store.GetDataBinding<ModelData>(
                    _store.Create(new ModelData(0.5f, new List<Weapon> { weapon }, at, _store))));
            }
            var unit = new UnitData(owner, $"U{Guid.NewGuid().ToString()[..4]}", quality: quality,
                defense: 4, modelBindings: bindings);
            var binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(owner, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        private static MacroAction Endpoint(Position end) =>
            new MacroAction(EMacroIntent.AdvanceOnObjective, $"test end=({end.x:F1},{end.z:F1})",
                EActionType.Advance, new List<ModelMoveEntry>(), EFeasibility.Reachable, end);
    }
}
