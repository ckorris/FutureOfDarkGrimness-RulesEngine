using FDG.Ai.Tactician;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.StageResolution.Requests;
using FDG.Utilities;
using NUnit.Framework;

namespace FDG.Tests
{
    // #365 Tier 2 - the lethality gate. Tier 1 is a habit that shapes HOW a unit travels; this is
    // the only term allowed to decide WHETHER it goes at all:
    //
    //     penalty = MoveLethality x P(effectively lost) x (what this unit would still have done)
    //
    // Pricing the FORFEITED CONTRIBUTION rather than the death is the whole design (Chris). A gate
    // keyed on DANGER peaks exactly when death is certain, so a doomed unit sees a huge penalty
    // everywhere, picks the least-bad option and freezes in cover contributing nothing. Priced as
    // forfeiture, a unit that dies whatever it does sees a near-CONSTANT, the term cancels in the
    // argmax, and the goal wins - case 12 below.
    //
    // Every case scores the same scene twice, once with the gate switched off, and asserts the
    // decision FLIPS (or pointedly does not). Asserting the outcome alone would let retaliation or
    // the objective terms pass the pin on their own and leave the gate untested.
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

        // --- Case 8: the knee is about STRENGTH, not raw casualties - it is the engine's own
        // mechanic (a unit at half strength or less is the one a failed morale test can finish). ---

        [Test]
        public void Score_SameVolley_OnlyTheUnitPushedPastHalfStrength_Balks()
        {
            int fresh = BalkThreshold(casualties: 0, quality: 4);
            int worn = BalkThreshold(casualties: 5, quality: 4);

            Assert.That(worn, Is.LessThan(fresh),
                $"the same marker, the same gunline, and the only difference is that one squad has " +
                $"already lost half its models: that one must lose its nerve at a strictly smaller " +
                $"volley. balks at {worn} guns worn vs {fresh} guns fresh");

            // At a volley between the two thresholds the claim is a single decision, not a trend.
            (float freshRush, float freshHold) = RushScene(worn, ourCasualties: 0);
            (float wornRush, float wornHold) = RushScene(worn, ourCasualties: 5);
            TacticianWeights.MoveLethality = 0f;
            (float ungatedRush, float ungatedHold) = RushScene(worn, ourCasualties: 5);

            Assert.That(ungatedRush, Is.GreaterThan(ungatedHold),
                $"scene check: with the gate off the worn squad takes the marker too, so the flip " +
                $"below belongs to the gate and not to retaliation. " +
                $"rush={ungatedRush:F4} hold={ungatedHold:F4}");
            Assert.That(freshRush, Is.GreaterThan(freshHold),
                $"{worn} guns leaves a full-strength squad above half strength - no knee, no reason " +
                $"to stop. rush={freshRush:F4} hold={freshHold:F4}");
            Assert.That(wornHold, Is.GreaterThan(wornRush),
                $"the identical volley onto the halved squad crosses the line where a failed test " +
                $"stops being a nuisance. rush={wornRush:F4} hold={wornHold:F4}");
        }

        // --- Case 9: Chris - "that could also scale with the unit quality, because a 3+ unit has
        // less to worry about than a 5+ one." Fresh squads, because quality only bites AT the knee:
        // a unit already deep past it is near-certain to break whatever its Quality. ---

        [Test]
        public void Score_AtTheKnee_TheWorseQualityUnitBalksFirst()
        {
            int veteran = BalkThreshold(casualties: 0, quality: 3);
            int militia = BalkThreshold(casualties: 0, quality: 5);

            Assert.That(militia, Is.LessThan(veteran),
                $"same squad, same marker, same guns - a 5+ unit fails the morale test that follows " +
                $"about twice as often as a 3+ one, so it must balk at a strictly smaller volley. " +
                $"5+ balks at {militia} guns, 3+ at {veteran}");

            (float veteranRush, float veteranHold) = RushScene(militia, quality: 3);
            (float militiaRush, float militiaHold) = RushScene(militia, quality: 5);

            Assert.That(veteranRush, Is.GreaterThan(veteranHold),
                $"the 3+ squad shrugs that test off often enough to be worth the marker. " +
                $"rush={veteranRush:F4} hold={veteranHold:F4}");
            Assert.That(militiaHold, Is.GreaterThan(militiaRush),
                $"the 5+ squad does not. rush={militiaRush:F4} hold={militiaHold:F4}");
        }

        // --- Case 10: round decay, and it must be EMERGENT from the horizon, not its own scalar.
        // Deliberately an objective-free scene: with no marker in play the forfeited contribution is
        // the attrition half alone, which is the half that decays (a contesting unit's objective
        // half correctly does NOT, and would mask the contrast). ---

        [Test]
        public void Score_LethalTrade_BalksInRoundOne_TakesItInTheFinalRound()
        {
            (float earlyAdvance, float earlyHold) = TradeScene(round: 1, enemyModels: 20);
            (float lateAdvance, float lateHold) = TradeScene(round: 4, enemyModels: 20);
            TacticianWeights.MoveLethality = 0f;
            (float ungatedAdvance, float ungatedHold) = TradeScene(round: 1, enemyModels: 20);

            Assert.That(ungatedAdvance, Is.GreaterThan(ungatedHold),
                $"scene check: ungated this trade looks good in every round, so the round-1 refusal " +
                $"below is the gate's doing. advance={ungatedAdvance:F4} hold={ungatedHold:F4}");
            Assert.That(earlyHold, Is.GreaterThan(earlyAdvance),
                $"in round 1 a unit that dies for one volley forfeits three more rounds of work. " +
                $"advance={earlyAdvance:F4} hold={earlyHold:F4}");
            Assert.That(lateAdvance, Is.GreaterThan(lateHold),
                $"in the final round it forfeits almost nothing - every unit dies when the game ends " +
                $"anyway (NUMBER_OF_ROUNDS = 4), so the same trade is now free. Round decay falls out " +
                $"of 'expected remaining contribution' being horizon-limited; it is never its own " +
                $"weight. advance={lateAdvance:F4} hold={lateHold:F4}");
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

        [Test, Explicit("calibration harness - prints the bracket, asserts nothing")]
        public void Calibrate()
        {
            float shipped = ShippedLethality;
            TestContext.Out.WriteLine("smallest gunline (models) that makes the unit refuse the marker:");
            foreach (float w in new[] { 1.0f, 1.5f, 2.0f, 3.0f, 4.0f, 6.0f })
            {
                TacticianWeights.MoveLethality = w;
                TestContext.Out.WriteLine($"  W={w:F1}  freshQ4={BalkThreshold(0, 4),4} " +
                    $"wornQ4={BalkThreshold(5, 4),4} freshQ3={BalkThreshold(0, 3),4} " +
                    $"freshQ5={BalkThreshold(0, 5),4}");
            }
            TacticianWeights.MoveLethality = shipped;
            foreach (int enemies in new[] { 10, 20, 30, 40 })
            {
                foreach (int round in new[] { 1, 4 })
                {
                    (float a, float h) = TradeScene(round, enemies);
                    TestContext.Out.WriteLine($"  trade {enemies,2} guns r{round}: " +
                        $"margin={a - h:+0.0000;-0.0000} -> {(a > h ? "GOES" : "BALKS")}");
                }
            }
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
            var bindings = new List<DataBinding<ModelData>>(models);
            for (int i = 0; i < models; i++)
            {
                var at = new Position(centre.x - 2.2f + (i % 5) * 1.1f, centre.z - 1.1f + (i / 5) * 1.1f);
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
