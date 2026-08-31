using FDG.Ai.Tactician;
using FDG.Ai.Tactician.Resolvers;
using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.StageResolution.Requests;
using FDG.Utilities;
using NUnit.Framework;

namespace FDG.Tests
{
    // #389 — the activation-ordering half of the walled-rear-shooter pathology: the urgency kill
    // term priced "advance straight at the enemy and shoot" with no idea whether the advance was
    // physically possible, so a shooter walled in by a deep friendly mass carried a phantom volley
    // score and activated BEFORE its own lane-blockers (then, forced to move with no room, slid
    // laterally - the WarriorSistersMovedLaterally save). The fix grounds the assumed closure in
    // standing room: FreeStraightAdvance caps it at the farthest point on the lane where the
    // closest model could legally END a move (#205: friendlies never block passage, only standing).
    [TestFixture]
    public class TacticianPhantomVolleyTests
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

        // --- FreeStraightAdvance geometry ---------------------------------------------------------

        [Test]
        public void FreeStraightAdvance_OpenLane_UsesTheFullBudget()
        {
            float free = TacticalAnalysis.FreeStraightAdvance(new Position(24f, 20f),
                new Position(24f, 48f), moverRadius: 0.5f, budget: 6f, blockers: new List<IModel>());
            Assert.That(free, Is.EqualTo(6f).Within(0.001f));
        }

        [Test]
        public void FreeStraightAdvance_ThinScreen_RoomBehindItCostsNothing()
        {
            // One friendly base squarely on the lane 3" out: the mover may pass THROUGH it (#205)
            // and the spot at full budget depth is clear, so the screen must not discount at all.
            List<IModel> screen = MakeBlockers((24f, 23f));
            float free = TacticalAnalysis.FreeStraightAdvance(new Position(24f, 20f),
                new Position(24f, 48f), moverRadius: 0.5f, budget: 6f, blockers: screen);
            Assert.That(free, Is.EqualTo(6f).Within(0.001f));
        }

        [Test]
        public void FreeStraightAdvance_DeepMass_CapsAtItsNearEdge()
        {
            // A packed 2x5 block (columns x=23.45/24.55, rows 1.1" apart from z=22.5): the occupied
            // spans overlap into one run that contains the budget-depth spot, so the farthest free
            // stand-point is the mass's near edge (~1.7"), not the full 6".
            List<IModel> wall = MakeWall();
            float free = TacticalAnalysis.FreeStraightAdvance(new Position(24f, 20f),
                new Position(24f, 48f), moverRadius: 0.5f, budget: 6f, blockers: wall);
            Assert.That(free, Is.LessThan(2f), $"expected the near edge of the mass, got {free:F2}");
            Assert.That(free, Is.GreaterThan(1f), $"expected the near edge of the mass, got {free:F2}");
        }

        [Test]
        public void FreeStraightAdvance_MassOffTheLane_DoesNotCap()
        {
            // The same block shifted 4" east: nothing occupies the lane corridor itself.
            List<IModel> wall = MakeWall(xShift: 4f);
            float free = TacticalAnalysis.FreeStraightAdvance(new Position(24f, 20f),
                new Position(24f, 48f), moverRadius: 0.5f, budget: 6f, blockers: wall);
            Assert.That(free, Is.EqualTo(6f).Within(0.001f));
        }

        // --- Urgency integration ------------------------------------------------------------------

        [Test]
        public void Urgency_WalledInShooter_DoesNotPriceThePhantomVolley()
        {
            // Rifles at 27" base-to-base: a full 6" advance brings the melee-only enemy into 24"
            // range, so the OPEN shooter carries a real kill term. Walled behind a deep friendly
            // mass the realizable closure is ~1.7" (reach 25.3" - out of range), so the volley is
            // phantom and the urgency must collapse; the same mass parked BEHIND the shooter (off
            // the lane) must change nothing.
            DataBinding<UnitData> shooter = MakeUnitAt(_us, 5, Rifle(), i => new Position(24f + i * 1.1f, 20f));
            MakeUnitAt(_them, 3, Claws(), i => new Position(24f + i * 1.1f, 48f));
            var resolver = new TacticianActivationResolver(_tableState, _evaluator);
            float open = resolver.Urgency(shooter);
            Assert.That(open, Is.GreaterThan(0.05f), "scene check: the open shooter must price a real volley");

            DataBinding<UnitData> wallBehind = MakeUnitAt(_us, 10, Claws(),
                i => new Position(23.45f + (i % 2) * 1.1f, 13f + (i / 2) * 1.1f));
            Assert.That(resolver.Urgency(shooter), Is.EqualTo(open).Within(0.0001f),
                "a friendly mass BEHIND the shooter is not on the lane and must not discount");
            wallBehind.GetValue().Models.ToList().ForEach(m => m.SetPosition(
                new Position(m.Position.x, m.Position.z + 9.5f)));

            float walled = resolver.Urgency(shooter);
            Assert.That(walled, Is.LessThan(0.01f),
                $"walled in, the volley is phantom - urgency must collapse (open={open:F4}, walled={walled:F4})");
        }

        [Test]
        public void Urgency_InRangeButSightBlockedByFriendlyWall_DoesNotPriceTheVolley()
        {
            // The WarriorSistersMovedLaterally geometry proper: the enemy is close enough that even
            // the CAPPED closure keeps it within rifle range (the 2.27"-free / 24"-gun shape where
            // pure distance never kills the volley), but the wall's bases stand on the sight line
            // (#384, official rules) - the planner's offense term refused this shot and the urgency
            // term must too.
            DataBinding<UnitData> shooter = MakeUnitAt(_us, 5, Rifle(), i => new Position(24f + i * 1.1f, 20f));
            MakeUnitAt(_them, 3, Claws(), i => new Position(24f + i * 1.1f, 44f));
            var open = new TacticianActivationResolver(_tableState, _evaluator).Urgency(shooter);
            Assert.That(open, Is.GreaterThan(0.05f), "scene check: the open shooter must price a real volley");

            // The 2x5 block plus one model squarely on the lane at (24,22.5): standing room caps at
            // ~1.5" (reach ~21.5" - still in range) and the on-lane base blocks the sight ray.
            MakeUnitAt(_us, 11, Claws(), i => i == 10 ? new Position(24f, 22.5f)
                : new Position(23.45f + (i % 2) * 1.1f, 22.5f + (i / 2) * 1.1f));
            float walled = new TacticianActivationResolver(_tableState, _evaluator).Urgency(shooter);
            Assert.That(walled, Is.LessThan(0.01f),
                $"in range but sightless through the wall - the volley is phantom (open={open:F4}, walled={walled:F4})");
        }

        [Test]
        public void Urgency_ScreenTheMoverCanStepPast_StillPricesTheVolley()
        {
            // A single friendly model on the lane 3" out is a screen, not a wall: the mover may
            // pass through it (#205) and stand at full budget depth, AHEAD of the screen - so the
            // sight test must run from that advanced position and the volley must keep its price.
            DataBinding<UnitData> shooter = MakeUnitAt(_us, 5, Rifle(), i => new Position(24f + i * 1.1f, 20f));
            MakeUnitAt(_them, 3, Claws(), i => new Position(24f + i * 1.1f, 48f));
            MakeUnitAt(_us, 1, Claws(), _ => new Position(24f, 23f));
            float urgency = new TacticianActivationResolver(_tableState, _evaluator).Urgency(shooter);
            Assert.That(urgency, Is.GreaterThan(0.05f),
                "a step-past screen must not zero the volley - the shot is priced from past it");
        }

        [Test]
        public void Urgency_BlockingTerrainAcrossTheLane_DoesNotPriceTheVolley()
        {
            // The #363 terrain half: a Blocking wall between the advanced firing position and the
            // enemy. Distance and standing room are both fine; sight alone kills the volley.
            DataBinding<UnitData> shooter = MakeUnitAt(_us, 5, Rifle(), i => new Position(24f + i * 1.1f, 20f));
            MakeUnitAt(_them, 3, Claws(), i => new Position(24f + i * 1.1f, 48f));
            _store.Create(new TerrainData(ETerrainType.Blocking, new RectangularZone(10f, 40f, 30f, 31f)));
            float urgency = new TacticianActivationResolver(_tableState, _evaluator).Urgency(shooter);
            Assert.That(urgency, Is.LessThan(0.01f),
                $"the wall blocks every sight line - the volley is phantom (urgency={urgency:F4})");
        }

        [Test]
        public async Task ActivationPick_WalledShooter_YieldsToItsLaneBlocker()
        {
            // The WarriorSistersMovedLaterally shape in miniature: a rear shooter walled by its own
            // melee unit, an enemy only the shooter could (phantom-)shoot. Pre-#389 the phantom
            // kill term made the REAR unit activate first; grounded, both urgencies are flat and
            // the #296 frontline bias sends the wall - the lane-blocker - first. Rear is listed
            // first so a flat-urgency first-option tie cannot pass this by accident.
            DataBinding<UnitData> rear = MakeUnitAt(_us, 5, Rifle(), i => new Position(24f + i * 1.1f, 20f));
            DataBinding<UnitData> wall = MakeUnitAt(_us, 10, Claws(),
                i => new Position(23.45f + (i % 2) * 1.1f, 22.5f + (i / 2) * 1.1f));
            MakeUnitAt(_them, 3, Claws(), i => new Position(24f + i * 1.1f, 48f));
            var resolver = new TacticianActivationResolver(_tableState, _evaluator);

            DataBinding<UnitData> chosen = await resolver.Resolve(new ChooseUnitToActivateRequest(_us,
                new List<SelectionRequest<UnitData>.ValidOption> { new(rear, "rear"), new(wall, "wall") },
                new List<SelectionRequest<UnitData>.InvalidOption>()));

            Assert.That(chosen, Is.EqualTo(wall),
                "the walled shooter's volley is phantom - its lane-blocker activates first");
        }

        // --- helpers (the TacticianLaneClearTests construction) -----------------------------------

        private static Weapon Rifle() => new Weapon("Rifle", rangeInches: 24f, attacks: 1, armorPenetration: 0);
        private static Weapon Claws() => new Weapon("Claws", rangeInches: 0f, attacks: 2, armorPenetration: 0);

        // A packed 2x5 friendly block starting 2.5" up the lane from the shooter row at z=20.
        private List<IModel> MakeWall(float xShift = 0f) =>
            MakeBlockers(Enumerable.Range(0, 10)
                .Select(i => (23.45f + (i % 2) * 1.1f + xShift, 22.5f + (i / 2) * 1.1f)).ToArray());

        private List<IModel> MakeBlockers(params (float x, float z)[] at) =>
            at.Select(p => (IModel)new ModelData(0.5f, new List<Weapon>(),
                new Position(p.x, p.z), _store)).ToList();

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
