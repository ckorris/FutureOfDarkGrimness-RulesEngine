using FDG.Ai.Tactician;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.StageResolution.Requests;
using FDG.Utilities;
using NUnit.Framework;

namespace FDG.Tests
{
    // #365 Tier 1 - "cover is a habit, not a plan" (Chris). The Tactician should hug cover on its
    // way to a goal the way a squad crosses an urban street: automatically, without anticipating
    // where the enemy will be, and NEVER at the price of the thing it came to do.
    //
    // This replaces #363 facet 3, which discounted incoming fire when a wall cut the lane. That was
    // fact-math on a forecast (a boolean about where a shooter stands NOW, used to price what it
    // does AFTER it moves) and, being a boolean, it put a cliff in the score - and a cliff can only
    // produce "ignore cover" or "hide in it", never "take the slightly bent route". Threat is now
    // priced through walls again; cover earns a BOUNDED bonus instead.
    //
    // These tests are the specification of the exchange rate, deliberately: cases 4 and 5 below
    // jointly define TacticianWeights.MoveCoverHabit, so the trade is reviewable in one screen
    // rather than emergent from six weights.
    // Mutates TacticianWeights.MoveCoverHabit (a process-global static), so it must not run
    // alongside anything that scores.
    [TestFixture, NonParallelizable]
    public class TacticianCoverHabitTests
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

        // Captured ONCE, before any test mutates it - a per-test capture would restore whatever the
        // last scene happened to leave behind, and a hard-coded literal would silently rot the day
        // the weight is retuned. In OneTimeSetUp rather than a static field initialiser: this type
        // is beforefieldinit, so an initialiser fires on FIRST ACCESS - which is inside TearDown,
        // after case 15 has already set the weight to 0 - and would capture 0 as the default.
        private static float ShippedCoverHabit;

        [OneTimeSetUp]
        public void CaptureShippedWeights() => ShippedCoverHabit = TacticianWeights.MoveCoverHabit;

        [TearDown]
        public void TearDown() => TacticianWeights.MoveCoverHabit = ShippedCoverHabit;

        // --- Case 1: the reflex itself. ---

        [Test]
        public void Score_ShadowedEndpoint_OutscoresOpenGroundAtEqualProgress()
        {
            Wall(27f, 48f, 20.5f, 21.5f);
            DataBinding<UnitData> us = Squad(_us, Carbine(), new Position(24f, 10f));
            Squad(_them, Rifle(), new Position(50f, 25f));

            (float shadowed, float exposed) = ScorePair(us, new Position(24f, 20f), new Position(24f, 22f));

            Assert.That(shadowed, Is.GreaterThan(exposed),
                $"two endpoints inside the same gunline's envelope, one behind a wall: the covered " +
                $"one must price safer even after paying for 2 fewer inches of progress. " +
                $"shadowed={shadowed:F4} exposed={exposed:F4}");
        }

        // --- Case 2: a blocker toward nobody is worth nothing (Chris's empty street). ---

        [Test]
        public void Score_BlockerTowardEmptyTable_EarnsNothing()
        {
            // Wall SOUTH of both endpoints; the only enemy is north-east, with a clear lane to both.
            Wall(20f, 40f, 6f, 7f);
            DataBinding<UnitData> us = Squad(_us, Carbine(), new Position(24f, 10f));
            Squad(_them, Rifle(), new Position(50f, 25f));

            (float behindWall, float inTheOpen) = ScorePair(us, new Position(24f, 12f), new Position(30f, 12f));

            Assert.That(behindWall, Is.EqualTo(inTheOpen).Within(0.02f),
                $"the wall shields these endpoints from an EMPTY edge of the table, so it must earn " +
                $"nothing - you hug the wall the gunfire is on, not just any wall. " +
                $"behindWall={behindWall:F4} open={inTheOpen:F4}");
        }

        // --- Case 3: no terrain, no effect. This is why the open-field bench pool cannot move. ---

        [Test]
        public void Score_WithoutTerrain_IsIndependentOfTheCoverWeight()
        {
            DataBinding<UnitData> us = Squad(_us, Carbine(), new Position(24f, 10f));
            Squad(_them, Rifle(), new Position(50f, 25f));
            var planner = new TacticianPlanner(_tableState, _evaluator);
            planner.BeginActivation(us);
            MacroAction candidate = Endpoint(new Position(24f, 20f));

            TacticianWeights.MoveCoverHabit = 0f;
            float off = planner.Score(candidate);
            TacticianWeights.MoveCoverHabit = 10f;
            float absurdlyHigh = planner.Score(candidate);

            Assert.That(absurdlyHigh, Is.EqualTo(off).Within(1e-6f),
                "with no Blocking terrain on the table nothing is ever shadowed, so the cover term " +
                "must be exactly inert - a 640-game open-field pool cannot move because of it");
        }

        // --- Cases 4 and 5: the exchange rate. These two DEFINE MoveCoverHabit. ---

        [Test]
        public void Score_TwoInchDetourForFullCover_IsWorthIt()
        {
            (float twelveExposed, float tenShadowed, float fourShadowed) = ExchangeRateScene();

            Assert.That(tenShadowed, Is.GreaterThan(twelveExposed),
                $"giving up 2 of 12 inches of progress to break every firing lane is a good trade " +
                $"(Chris). 12\"-exposed={twelveExposed:F4} 10\"-shadowed={tenShadowed:F4} " +
                $"4\"-shadowed={fourShadowed:F4}");
        }

        [Test]
        public void Score_EightInchDetourForFullCover_IsNotWorthIt()
        {
            (float twelveExposed, float tenShadowed, float fourShadowed) = ExchangeRateScene();

            Assert.That(twelveExposed, Is.GreaterThan(fourShadowed),
                $"giving up 8 of 12 inches for the same cover is not (Chris) - the habit must never " +
                $"outrank the goal. 12\"-exposed={twelveExposed:F4} 10\"-shadowed={tenShadowed:F4} " +
                $"4\"-shadowed={fourShadowed:F4}");
        }

        /// <summary>
        /// Unit at (24,10) walking to a marker at (24,40), with a gunline east at (50,18). All
        /// three endpoints sit on a circle of radius 26 around that gunline, so incoming fire is
        /// PROVABLY identical at each (EstimateShooting has no falloff inside a weapon's range - it
        /// only drops weapons that cannot reach), and the only live differences are objective
        /// progress and cover. A wall at x 29..31, z 14..20 cuts the gunline's lane to the 10" and
        /// 4" endpoints and not to the 12" one. Progress is measured along z from the start.
        /// </summary>
        private (float TwelveExposed, float TenShadowed, float FourShadowed) ExchangeRateScene()
        {
            Wall(29f, 31f, 14f, 20f);
            DataBinding<UnitData> us = Squad(_us, Carbine(), new Position(24f, 10f));
            Squad(_them, Rifle(), new Position(50f, 18f));
            _store.Create(new ObjectiveData(new Position(24f, 40f), _store));

            var planner = new TacticianPlanner(_tableState, _evaluator);
            planner.BeginActivation(us);
            return (planner.Score(Endpoint(new Position(24.3f, 22f))),
                    planner.Score(Endpoint(new Position(24.08f, 20f))),
                    planner.Score(Endpoint(new Position(24.3f, 14f))));
        }

        // Regenerates the bracket quoted in TacticianWeights.MoveCoverHabit's comment. Run it
        // (dotnet test --filter Calibrate) whenever a scoring weight moves and the exchange rate
        // needs re-reading; the two pins above are what actually enforce it.
        [Test, Explicit("calibration harness")]
        public void Calibrate()
        {
            Wall(29f, 31f, 14f, 20f);
            DataBinding<UnitData> us = Squad(_us, Carbine(), new Position(24f, 10f));
            Squad(_them, Rifle(), new Position(50f, 18f));
            _store.Create(new ObjectiveData(new Position(24f, 40f), _store));
            var planner = new TacticianPlanner(_tableState, _evaluator);
            planner.BeginActivation(us);
            MacroAction a = Endpoint(new Position(24.3f, 22f));
            MacroAction b = Endpoint(new Position(24.08f, 20f));
            MacroAction c = Endpoint(new Position(24.3f, 14f));

            TacticianWeights.MoveCoverHabit = 0f;
            (float a0, float b0, float c0) = (planner.Score(a), planner.Score(b), planner.Score(c));
            TacticianWeights.MoveCoverHabit = 1f;
            (float a1, float b1, float c1) = (planner.Score(a), planner.Score(b), planner.Score(c));
            Console.WriteLine($"CAL share    12ex={a1 - a0:F5} 10sh={b1 - b0:F5} 4sh={c1 - c0:F5}");
            Console.WriteLine($"CAL progress 12->10={a0 - b0:F5}  12->4={a0 - c0:F5}");

            // What a real 5-rifle volley is worth, for the pin-11 upper bound.
            SetUp();
            Wall(22f, 26f, 17f, 18f);
            DataBinding<UnitData> shooters = Squad(_us, Rifle(), new Position(24f, 10f));
            Squad(_them, Rifle(), new Position(24f, 30f));
            var p2 = new TacticianPlanner(_tableState, _evaluator);
            p2.BeginActivation(shooters);
            TacticianWeights.MoveCoverHabit = 0f;
            float shot = p2.Score(Endpoint(new Position(33f, 14f)));
            float hide = p2.Score(Endpoint(new Position(24f, 14f)));
            Console.WriteLine($"CAL volley   shooting={shot:F5} shadowed={hide:F5} edge={shot - hide:F5}");
        }

        // --- Case 14: cover from bullets must not walk the unit into swords (Chris's corridor). ---

        [Test]
        public void Score_CoveredSideWithinChargeReach_LosesToOpenSideOutOfIt()
        {
            (float shadowedNearSwords, float openAwayFromSwords) = CorridorScene(new Position(18f, 12f));

            Assert.That(openAwayFromSwords, Is.GreaterThan(shadowedNearSwords),
                $"the left wall shadows this endpoint from the gunline, but a pack of swordsmen can " +
                $"charge it and the right-hand endpoint is out of their reach. A habit that only " +
                $"knows about bullets picks the covered side and dies to the charge - retaliation " +
                $"takes a MAX over enemies, so it barely distinguishes the two on its own. " +
                $"shadowed+charged={shadowedNearSwords:F4} open+safe={openAwayFromSwords:F4}");
        }

        [Test]
        public void Score_MeleeReach_CancelsTheCoverBonusItWouldOtherwiseEarn()
        {
            (float shadowedNearSwords, _) = CorridorScene(new Position(18f, 12f));
            TacticianWeights.MoveCoverHabit = 0f;
            (float habitOff, _) = CorridorScene(new Position(18f, 12f));

            Assert.That(shadowedNearSwords, Is.EqualTo(habitOff).Within(1e-5f),
                $"the cover this endpoint buys is worthless against the swords that can reach it, so " +
                $"the habit must come out at exactly zero here - it withholds its bonus rather than " +
                $"inventing a second melee penalty on top of the one retaliation already charges. " +
                $"with-habit={shadowedNearSwords:F5} habit-off={habitOff:F5}");
        }

        [Test]
        public void Score_MeleeReachingBothSides_StillPrefersTheShadowedOne()
        {
            // Swordsmen equidistant from both endpoints: the charge is coming either way, so it is
            // not a reason to stop caring which side gets shot at (Chris).
            (float shadowed, float exposed) = CorridorScene(new Position(25f, 14f));

            Assert.That(shadowed, Is.GreaterThan(exposed),
                $"a melee threat that reaches every candidate alike is a CONSTANT - it must cancel in " +
                $"the argmax and leave the shooting-cover signal intact, not swamp it. Sharing a " +
                $"denominator diluted this, and clamping the habit at zero destroyed it outright. " +
                $"shadowed={shadowed:F4} exposed={exposed:F4}");
        }

        // --- Case 17 (#365 slice 2a): melee mass that cannot reach must not dilute the melee half.

        [Test]
        public void Score_DistantMeleeBlob_DoesNotDiluteTheReachablePenalty()
        {
            // Three more sword squads massed near the far table edge - a melee-heavy army. None is
            // within a charge of either endpoint, and all three sit on x=25, equidistant from the
            // two endpoints, so whatever they DO contribute (projected pressure) is identical for
            // both and cannot decide the comparison on its own.
            (float shadowedNearSwords, float openAwayFromSwords) = CorridorScene(new Position(18f, 12f),
                new[] { new Position(25f, 42f), new Position(25f, 45f), new Position(25f, 48f) });

            Assert.That(openAwayFromSwords, Is.GreaterThan(shadowedNearSwords),
                $"case 14 must not depend on how many OTHER melee units the enemy owns. The melee " +
                $"half's denominator counts only threat that could reach us this activation - summed " +
                $"over the whole table it shrinks the reachable pack's share toward 1/N, the cover " +
                $"bonus survives almost intact, and the habit walks the unit into the swords it was " +
                $"added to avoid. shadowed+charged={shadowedNearSwords:F4} " +
                $"open+safe={openAwayFromSwords:F4}");
        }

        /// <summary>
        /// A corridor between two walls. Both endpoints sit at the same z, so objective progress is
        /// identical (ObjectiveApproach projects onto the route and discards lateral offset). The
        /// left-hand one is shadowed from the gunline at (4,34) by the left wall; the right-hand one
        /// has a clear lane. Where the swordsmen stand decides which endpoints they can charge.
        /// </summary>
        private (float ShadowedNearSwords, float OpenAwayFromSwords) CorridorScene(Position swordsmen,
            IReadOnlyList<Position>? distantMelee = null)
        {
            SetUp();
            Wall(14f, 16f, 18f, 28f);
            Wall(32f, 34f, 18f, 30f);
            DataBinding<UnitData> us = Squad(_us, Carbine(), new Position(24f, 10f));
            Squad(_them, Rifle(), new Position(4f, 34f));
            Squad(_them, GreatSword(), swordsmen);
            foreach (Position far in distantMelee ?? Array.Empty<Position>())
                Squad(_them, GreatSword(), far);
            _store.Create(new ObjectiveData(new Position(24f, 40f), _store));

            var planner = new TacticianPlanner(_tableState, _evaluator);
            planner.BeginActivation(us);
            return (planner.Score(Endpoint(new Position(20f, 24f))),
                    planner.Score(Endpoint(new Position(30f, 24f))));
        }

        // --- Case 11: a real shot always beats hiding. Chris's "will it refuse to shoot?" worry. ---

        [Test]
        public void Score_EndpointWithAShot_OutscoresShadowWithout()
        {
            Wall(22f, 26f, 17f, 18f);
            DataBinding<UnitData> us = Squad(_us, Rifle(), new Position(24f, 10f));
            Squad(_them, Rifle(), new Position(24f, 30f));

            var planner = new TacticianPlanner(_tableState, _evaluator);
            planner.BeginActivation(us);
            // (33,14) has a clear lane around the wall and is in rifle range; (24,14) is shadowed.
            float canShoot = planner.Score(Endpoint(new Position(33f, 14f)));
            float shadowed = planner.Score(Endpoint(new Position(24f, 14f)));

            Assert.That(canShoot, Is.GreaterThan(shadowed),
                $"the cover habit is bounded precisely so a real volley always outbids hiding - a " +
                $"unit that will not shoot because it prefers a wall is the failure this bound " +
                $"exists to prevent. shooting={canShoot:F4} shadowed={shadowed:F4}");
        }

        // --- Case 13: exposure to the unit you came to shoot is chosen, so it is not taxed. ---

        [Test]
        public void Score_EngagedTarget_IsExcludedFromTheCoverShare()
        {
            // Target north with a clear lane; flanker east, shadowed by the wall. Same endpoint,
            // scored with and without declaring the target - only the exclusion differs.
            Wall(27f, 48f, 20.5f, 21.5f);
            DataBinding<UnitData> us = Squad(_us, Carbine(), new Position(24f, 10f));
            Squad(_them, Rifle(), new Position(24f, 30f));
            DataBinding<UnitData> flanker = Squad(_them, Rifle(), new Position(50f, 25f));

            var planner = new TacticianPlanner(_tableState, _evaluator);
            planner.BeginActivation(us);
            var end = new Position(24f, 20f);
            float untargeted = planner.Score(Endpoint(end));
            float engagingTheExposedOne = planner.Score(Endpoint(end) with
            {
                Intent = EMacroIntent.EngageAtRange,
                TargetEnemy = _tableState.Units.Objects.First(u =>
                    u.PlayerID.Equals(_them) && !ReferenceEquals(u, flanker.GetValue())),
            });

            Assert.That(engagingTheExposedOne, Is.GreaterThan(untargeted),
                $"the flanker is behind the wall and the target is not, so declaring the target " +
                $"must RAISE the share - you chose that exposure and retaliation already charges " +
                $"you for it. Taxing it twice makes the habit fight the action it rides on. " +
                $"engaging={engagingTheExposedOne:F4} untargeted={untargeted:F4}");
        }

        // --- Helpers. ---

        private static Weapon Rifle() => new Weapon("Rifle", rangeInches: 24f, attacks: 1, armorPenetration: 0);

        private static Weapon GreatSword() =>
            new Weapon("Great Sword", rangeInches: 0f, attacks: 4, armorPenetration: 3);

        // Too short to reach anything in these scenes, so the offense and approach terms stay inert
        // and the only live differences between endpoints are objective progress and cover.
        private static Weapon Carbine() => new Weapon("Carbine", rangeInches: 6f, attacks: 1, armorPenetration: 0);

        private void Wall(float x0, float x1, float z0, float z1) =>
            _store.Create(new TerrainData(ETerrainType.Blocking | ETerrainType.Impassible,
                new RectangularZone(x0, x1, z0, z1)));

        private DataBinding<UnitData> Squad(PlayerID owner, Weapon weapon, Position centre)
        {
            var models = new List<DataBinding<ModelData>>(5);
            for (int i = 0; i < 5; i++)
            {
                var at = new Position(centre.x - 1.1f + (i % 3) * 1.1f, centre.z - 0.55f + (i / 3) * 1.1f);
                models.Add(_store.GetDataBinding<ModelData>(
                    _store.Create(new ModelData(0.5f, new List<Weapon> { weapon }, at, _store))));
            }
            var unit = new UnitData(owner, $"U{Guid.NewGuid().ToString()[..4]}", quality: 4, defense: 4,
                modelBindings: models);
            var binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(owner, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        private (float A, float B) ScorePair(DataBinding<UnitData> us, Position a, Position b)
        {
            var planner = new TacticianPlanner(_tableState, _evaluator);
            planner.BeginActivation(us);
            return (planner.Score(Endpoint(a)), planner.Score(Endpoint(b)));
        }

        private static MacroAction Endpoint(Position end) =>
            new MacroAction(EMacroIntent.AdvanceOnObjective, $"test end=({end.x:F1},{end.z:F1})",
                EActionType.Advance, new List<ModelMoveEntry>(), EFeasibility.Reachable, end);
    }
}
