using FDG.Ai.Tactician;
using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.Stages;
using FDG.Utilities;
using NUnit.Framework;

namespace FDG.Tests
{
    // #191 A4-2 — the (action x macro-action) planner on authored states: objective-seeking beats
    // standing around, favorable charges get taken, the post-move re-entry shoots or passes, and
    // the movement handoff only fires for the planned unit.
    [TestFixture]
    public class TacticianPlannerTests
    {
        private static readonly string[] AllActions =
        {
            ChooseActionStage.MOVEMENT_CHOICE_NAME, ChooseActionStage.CHARGE_CHOICE_NAME,
            ChooseActionStage.SHOOT_CHOICE_NAME, ChooseActionStage.PASS_CHOICE_NAME,
        };

        private GameDataStore _store = null!;
        private TableState _tableState = null!;
        private TacticianPlanner _planner = null!;
        private PlayerID _us;
        private PlayerID _them;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _tableState = new TableState(_store);
            _planner = new TacticianPlanner(_tableState, new RuleEvaluator(new ProbabilisticDiceRoller()));
            _us = new PlayerID(Guid.NewGuid());
            _them = new PlayerID(Guid.NewGuid());
        }

        [Test]
        public void ObjectiveInReach_PlansAMoveThatSeizesIt()
        {
            _store.Create(new ObjectiveData(new Position(30f, 24f), _store));
            var unit = MakeUnit(_us, 3, Rifle(), atX: 20f, atZ: 24f); // 10" away, rush 12
            _planner.BeginActivation(unit);

            string? action = _planner.ChooseAction(AllActions);
            List<StageResolution.Requests.ModelMoveEntry>? move = _planner.TakePlannedMove(unit);

            Assert.That(action, Is.EqualTo(ChooseActionStage.MOVEMENT_CHOICE_NAME));
            Assert.That(move, Is.Not.Null);
            var ends = move!.Select(e => e.Positions[^1]).ToList();
            float centroidDist = Distance(
                new Position(ends.Average(p => p.x), ends.Average(p => p.z)), new Position(30f, 24f));
            Assert.That(centroidDist, Is.LessThanOrEqualTo(TacticalAnalysis.ObjectiveSeizureRadiusInches + 1.5f),
                "the planned move must land on the objective");
        }

        [Test]
        public void FavorableCharge_GetsTaken()
        {
            // A strong melee unit adjacent to a fragile shooting unit, no objectives: charging is
            // clearly the best value trade on the table.
            var brawlers = MakeUnit(_us, 5, Blade(attacks: 3), atX: 20f, atZ: 24f);
            MakeUnit(_them, 3, Rifle(), atX: 26f, atZ: 24f);
            _planner.BeginActivation(brawlers);

            string? action = _planner.ChooseAction(AllActions);
            List<StageResolution.Requests.ModelMoveEntry>? move = _planner.TakePlannedMove(brawlers);

            Assert.That(action, Is.EqualTo(ChooseActionStage.CHARGE_CHOICE_NAME));
            Assert.That(move, Is.Not.Null);
            float gap = MovementPlanner.MinEnemyGap(move!,
                MovementPlanner.LiveEnemyFootprints(_tableState, _us));
            Assert.That(gap, Is.LessThanOrEqualTo(0.25f), "a taken charge ends in contact");
        }

        [Test]
        public void MeleeUnitOutOfChargeReach_ApproachesInsteadOfStanding()
        {
            // The A4 gate failure mechanism (#191, twice): a one-step greedy score gave melee units
            // outside charge reach no reason to close. The approach term fixes it: brawlers 24" from
            // lunch, no objectives - the best plan is a move that closes the charge gap.
            var brawlers = MakeUnit(_us, 5, Blade(attacks: 3), atX: 20f, atZ: 24f);
            var lunch = MakeUnit(_them, 3, Rifle(), atX: 44f, atZ: 24f);
            _planner.BeginActivation(brawlers);

            string? action = _planner.ChooseAction(AllActions);
            List<StageResolution.Requests.ModelMoveEntry>? move = _planner.TakePlannedMove(brawlers);

            Assert.That(action, Is.EqualTo(ChooseActionStage.MOVEMENT_CHOICE_NAME));
            Assert.That(move, Is.Not.Null);
            var ends = move!.Select(e => e.Positions[^1]).ToList();
            var endCentroid = new Position(ends.Average(p => p.x), ends.Average(p => p.z));
            float closed = Distance(new Position(20f, 24f), new Position(44f, 24f))
                - Distance(endCentroid, new Position(44f, 24f));
            Assert.That(closed, Is.GreaterThanOrEqualTo(6f),
                "the approach must spend most of a rush closing toward the charge target");
        }

        [Test]
        public void ShooterFarFromObjective_ClosesOnIt_InsteadOfFreezing()
        {
            // The a5-2-gate loss mechanism (#191 A5-3): an uncontested objective two moves away
            // and a scary horde out of rifle reach - the on-marker-only objective term scored Hold
            // best (offense 0, retaliation punishes closing) and the army froze while the horde
            // took the marker race. The objective gradient must make the unit start walking.
            _store.Create(new ObjectiveData(new Position(44f, 24f), _store));
            var shooters = MakeUnit(_us, 3, Rifle(), atX: 20f, atZ: 24f); // 24" out
            MakeUnit(_them, 6, Blade(attacks: 3), atX: 44f, atZ: 4f);     // looming, out of range
            _planner.BeginActivation(shooters);

            string? action = _planner.ChooseAction(new[]
            {
                ChooseActionStage.MOVEMENT_CHOICE_NAME, ChooseActionStage.CHARGE_CHOICE_NAME,
                ChooseActionStage.PASS_CHOICE_NAME, // no Shoot: nothing is in range, engine gates it
            });
            List<StageResolution.Requests.ModelMoveEntry>? move = _planner.TakePlannedMove(shooters);

            Assert.That(action, Is.EqualTo(ChooseActionStage.MOVEMENT_CHOICE_NAME),
                "standing still concedes the marker race");
            Assert.That(move, Is.Not.Null);
            var ends = move!.Select(e => e.Positions[^1]).ToList();
            var endCentroid = new Position(ends.Average(p => p.x), ends.Average(p => p.z));
            float closed = Distance(new Position(20f, 24f), new Position(44f, 24f))
                - Distance(endCentroid, new Position(44f, 24f));
            Assert.That(closed, Is.GreaterThanOrEqualTo(4f),
                "the move must make real progress toward the objective");
        }

        [Test]
        public void CheapUnit_ScreensTheValuableShooters_FromTheHorde()
        {
            // A5-4: the M8/M9 interposition candidates always existed - nothing paid them. A cheap
            // unarmed body between a melee horde and our expensive gunline must move ONTO the
            // charge lane (its own retaliation price is what keeps casters from doing the same).
            var screen = MakeUnit(_us, 3, Unarmed(), atX: 34f, atZ: 32f);
            MakeUnit(_us, 5, Rifle(), atX: 40f, atZ: 24f);              // the ward
            MakeUnit(_them, 6, Blade(attacks: 3), atX: 20f, atZ: 24f);  // the horde, 20" out
            _planner.BeginActivation(screen);

            string? action = _planner.ChooseAction(AllActions);
            List<StageResolution.Requests.ModelMoveEntry>? move = _planner.TakePlannedMove(screen);

            Assert.That(action, Is.EqualTo(ChooseActionStage.MOVEMENT_CHOICE_NAME));
            Assert.That(move, Is.Not.Null);
            var ends = move!.Select(e => e.Positions[^1]).ToList();
            var endCentroid = new Position(ends.Average(p => p.x), ends.Average(p => p.z));
            float toLane = DistanceToSegment(endCentroid,
                new Position(20f, 24f), new Position(40f, 24f));
            Assert.That(toLane, Is.LessThanOrEqualTo(3.5f),
                "the screen must end on the horde->gunline charge lane");
        }

        [Test]
        public void ObjectiveUrgency_RisesThroughTheGame()
        {
            Assert.That(TacticianPlanner.ObjectiveUrgency(1, 4),
                Is.LessThan(TacticianPlanner.ObjectiveUrgency(3, 4)));
            Assert.That(TacticianPlanner.ObjectiveUrgency(4, 4), Is.EqualTo(1.3f).Within(0.001f),
                "the final round is when ownership becomes the score");
            Assert.That(TacticianPlanner.ObjectiveUrgency(1, 4), Is.GreaterThanOrEqualTo(0.5f),
                "early markers still matter - ownership persists");
        }

        [Test]
        public void NoScreenIsThrown_ForAWardThatWinsTheExchange()
        {
            // A5-4b: the ward's threat is the exchange MARGIN - a counter-wall of blades that eats
            // chargers needs no bodyguard, so the cheap unit must not be pulled onto the lane.
            var screen = MakeUnit(_us, 3, Unarmed(), atX: 34f, atZ: 32f);
            MakeUnit(_us, 5, Blade(attacks: 4), atX: 40f, atZ: 24f);    // powerhouse "ward"
            MakeUnit(_them, 4, Blade(attacks: 1), atX: 20f, atZ: 24f);  // weak chaff "threat"
            _planner.BeginActivation(screen);

            _planner.ChooseAction(AllActions);
            List<StageResolution.Requests.ModelMoveEntry>? move = _planner.TakePlannedMove(screen);

            if (move != null)
            {
                var ends = move.Select(e => e.Positions[^1]).ToList();
                var endCentroid = new Position(ends.Average(p => p.x), ends.Average(p => p.z));
                float toLane = DistanceToSegment(endCentroid,
                    new Position(20f, 24f), new Position(40f, 24f));
                Assert.That(toLane, Is.GreaterThan(3f),
                    "no screen credit should pull the unit onto a lane the ward dominates");
            }
        }

        [Test]
        public void EmbarkedCargo_Disembarks_WhenTheTransportArrives_AndRidesOtherwise()
        {
            // A5-5: cargo rode until the transport died (zero voluntary disembarks in the
            // DE-vs-Orks logs). Near a marker we do not own: get out. In the middle of nowhere:
            // keep riding (never Disembark).
            var transport = MakeUnit(_us, 1, Rifle(), atX: 30f, atZ: 24f);
            var cargo = MakeUnit(_us, 4, Blade(), atX: 30f, atZ: 24f);
            TransportUtilities.Embark(cargo.GetValue(), transport.GetValue());
            string[] options = { CoreRuleCatalog.DisembarkRuleName, ChooseActionStage.PASS_CHOICE_NAME };

            _planner.BeginActivation(cargo);
            Assert.That(_planner.ChooseAction(options),
                Is.Not.EqualTo(CoreRuleCatalog.DisembarkRuleName),
                "nothing worth fighting for in reach: keep riding");

            _store.Create(new ObjectiveData(new Position(35f, 24f), _store));
            _planner.BeginActivation(cargo);
            Assert.That(_planner.ChooseAction(options), Is.EqualTo(CoreRuleCatalog.DisembarkRuleName),
                "a marker in the cargo's post-drop reach: get out and take it");
        }

        private static Weapon Unarmed() => new Weapon("Fists", 0f, 1, 0);

        private static float DistanceToSegment(Position p, Position a, Position b)
        {
            float abx = b.x - a.x, abz = b.z - a.z;
            float lengthSq = abx * abx + abz * abz;
            float t = Math.Clamp(((p.x - a.x) * abx + (p.z - a.z) * abz) / lengthSq, 0f, 1f);
            var q = new Position(a.x + t * abx, a.z + t * abz);
            return Distance(p, q);
        }

        [Test]
        public void SecondChooseAction_AfterTheMove_ShootsThenPasses()
        {
            _store.Create(new ObjectiveData(new Position(30f, 24f), _store));
            var unit = MakeUnit(_us, 3, Rifle(), atX: 20f, atZ: 24f);
            _planner.BeginActivation(unit);
            _planner.ChooseAction(AllActions);

            Assert.That(_planner.ChooseAction(new[]
            {
                ChooseActionStage.SHOOT_CHOICE_NAME, ChooseActionStage.PASS_CHOICE_NAME,
            }), Is.EqualTo(ChooseActionStage.SHOOT_CHOICE_NAME));
            Assert.That(_planner.ChooseAction(new[] { ChooseActionStage.PASS_CHOICE_NAME }),
                Is.EqualTo(ChooseActionStage.PASS_CHOICE_NAME));
        }

        [Test]
        public void PlannedMove_IsHandedToThePlannedUnitOnly_AndOnlyOnce()
        {
            _store.Create(new ObjectiveData(new Position(30f, 24f), _store));
            var unit = MakeUnit(_us, 3, Rifle(), atX: 20f, atZ: 24f);
            var other = MakeUnit(_us, 3, Rifle(), atX: 5f, atZ: 5f);
            _planner.BeginActivation(unit);
            _planner.ChooseAction(AllActions);

            Assert.That(_planner.TakePlannedMove(other), Is.Null, "another unit's request falls back to solo");
            Assert.That(_planner.TakePlannedMove(unit), Is.Not.Null);
        }

        [Test]
        public void NoActiveUnit_DeclinesSoTheCallerFallsBack()
        {
            Assert.That(_planner.ChooseAction(AllActions), Is.Null);
        }

        // --- fixtures ---------------------------------------------------------------------------

        private static Weapon Rifle() => new Weapon("Rifle", rangeInches: 24f, attacks: 1, armorPenetration: 0);
        private static Weapon Blade(int attacks = 2) => new Weapon("Blade", 0f, attacks, 0);

        private DataBinding<UnitData> MakeUnit(PlayerID owner, int modelCount, Weapon weapon,
            float atX, float atZ)
        {
            var modelBindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon> { weapon },
                    new Position(atX + (i % 2) * 1.1f, atZ + (i / 2) * 1.1f), _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(owner, $"U{atX}", quality: 4, defense: 4, modelBindings: modelBindings);
            var binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(owner, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        private static float Distance(Position a, Position b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return MathF.Sqrt(dx * dx + dz * dz);
        }
    }
}
