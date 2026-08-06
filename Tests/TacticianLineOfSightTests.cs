using FDG.Ai.Tactician;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.StageResolution.Requests;
using FDG.Stages;
using FDG.Utilities;
using NUnit.Framework;

namespace FDG.Tests
{
    // #363 — the Tactician was line-of-sight-blind: EstimateShooting priced volleys by distance
    // alone, so a unit behind a Blocking wall was credited a phantom volley it could never take
    // (the engine's Shoot gate then found nothing fireable and the activation fizzled - the
    // BattleBrothers save: a 6" advance into the wall's shadow, no shot, while a real firing lane
    // sat 5" to the side). These tests pin the two mechanisms of the fix: (1) AttackContext's
    // SightBlocked flag silences LoS-bound weapons in the estimate (Indirect exempt, per weapon);
    // (2) MacroActionGenerator's EngageAtRange goals rotate around the target to a clear lane when
    // the straight-line band point is blocked. Plus the headline behavior: the planned move ends
    // where the unit can actually fire.
    //
    // The shared scene: 5 rifles (24") at (24,6), 5 enemy rifles at (24,30) - exactly max range -
    // with a small Blocking wall (x 22..26, z 17..18) cutting the straight lane. A clear lane
    // exists ~15 degrees to either side; the arc sample at band distance is within (clipped)
    // advance reach, and its whole final leg sees the enemy.
    [TestFixture]
    public class TacticianLineOfSightTests
    {
        private GameDataStore _store = null!;
        private TableState _tableState = null!;
        private RuleEvaluator _evaluator = null!;
        private PlayerID _us;
        private PlayerID _them;
        private List<string> _decisions = null!;

        private static readonly Position EnemyCenter = new Position(24f, 30f);

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _tableState = new TableState(_store);
            _evaluator = new RuleEvaluator(new ProbabilisticDiceRoller());
            _us = new PlayerID(Guid.NewGuid());
            _them = new PlayerID(Guid.NewGuid());
            _decisions = new List<string>();
        }

        private (DataBinding<UnitData> Us, DataBinding<UnitData> Them) MakeWalledShooterScene()
        {
            _store.Create(new TerrainData(ETerrainType.Blocking | ETerrainType.Impassible,
                new RectangularZone(22f, 26f, 17f, 18f)));
            var us = MakeUnitAt(_us, 5, Rifle(),
                i => new Position(22.9f + (i % 3) * 1.1f, 5.5f + (i / 3) * 1.1f));
            var them = MakeUnitAt(_them, 5, Rifle(),
                i => new Position(22.9f + (i % 3) * 1.1f, 29.5f + (i / 3) * 1.1f));
            return (us, them);
        }

        // --- Mechanism 1: the estimate itself. ---

        [Test]
        public void EstimateShooting_SightBlocked_SilencesLosBoundWeapons()
        {
            (DataBinding<UnitData> us, DataBinding<UnitData> them) = MakeWalledShooterScene();

            AttackEstimate clear = CombatMath.EstimateShooting(_evaluator, us, them,
                new AttackContext(20f));
            AttackEstimate blocked = CombatMath.EstimateShooting(_evaluator, us, them,
                new AttackContext(20f, SightBlocked: true));

            Assert.That(clear.ExpectedWounds, Is.GreaterThan(0f),
                "sanity: rifles in range with a clear line must expect wounds");
            Assert.That(blocked.ExpectedWounds, Is.EqualTo(0f),
                "a blocked sight line must silence every LoS-bound weapon - the phantom volley " +
                "through a wall is the #363 failure");
        }

        [Test]
        public void EstimateShooting_SightBlocked_IndirectStillFires()
        {
            (DataBinding<UnitData> us, DataBinding<UnitData> them) = MakeWalledShooterScene();
            AttachRule(us, "Indirect", CoreRuleCatalog.Indirect);

            AttackEstimate blocked = CombatMath.EstimateShooting(_evaluator, us, them,
                new AttackContext(20f, SightBlocked: true));

            Assert.That(blocked.ExpectedWounds, Is.GreaterThan(0f),
                "Indirect ignores line of sight - a blocked lane must not zero its estimate " +
                "(the #360 artillery family keeps its value)");
        }

        // --- Mechanism 2: the generator's firing positions. ---

        [Test]
        public void EngageAtRange_BlockedStraightLane_FindsClearLaneEndpoint()
        {
            (DataBinding<UnitData> us, DataBinding<UnitData> them) = MakeWalledShooterScene();
            List<ITerrain> terrain = _tableState.Terrain.Objects.ToList();

            List<MacroAction> candidates = MacroActionGenerator.Enumerate(_evaluator, _tableState, us);

            List<MacroAction> engage = candidates
                .Where(c => c.Intent == EMacroIntent.EngageAtRange
                    && ReferenceEquals(c.TargetEnemy, them.GetValue()))
                .ToList();
            Assert.That(engage, Is.Not.Empty, "a ranged unit must generate EngageAtRange candidates");
            Assert.That(engage.Any(c =>
                    LineOfSightUtilities.HasLineOfSight(c.ProjectedCentroid, EnemyCenter, terrain)),
                Is.True,
                "with the straight band point in the wall's shadow, some EngageAtRange endpoint " +
                "must side-step to a lane that actually sees the target; got only: "
                + string.Join("; ", engage.Select(c =>
                    $"({c.ProjectedCentroid.x:F1},{c.ProjectedCentroid.z:F1})")));
        }

        // --- Headline: the planned activation ends able to fire. ---

        [Test]
        public void WalledShooter_PlansMoveToAClearFiringLane()
        {
            (DataBinding<UnitData> us, DataBinding<UnitData> them) = MakeWalledShooterScene();
            List<ITerrain> terrain = _tableState.Terrain.Objects.ToList();
            var planner = new TacticianPlanner(_tableState, _evaluator, _decisions.Add);
            planner.BeginActivation(us);

            string? action = planner.ChooseAction(new[]
            {
                ChooseActionStage.MOVEMENT_CHOICE_NAME, ChooseActionStage.PASS_CHOICE_NAME,
                // no Shoot offered: the engine gates it off (nothing fireable from the shadow),
                // exactly the BattleBrothers situation.
            });

            Assert.That(action, Is.EqualTo(ChooseActionStage.MOVEMENT_CHOICE_NAME),
                "the only value on the table is a side-step to a real firing lane - the unit must " +
                "move, not idle in the wall's shadow\n" + DecisionTable());

            List<ModelMoveEntry>? move = planner.TakePlannedMove(us);
            Assert.That(move, Is.Not.Null, "the movement action must carry a planned move\n" + DecisionTable());

            Position end = EndCentroid(move!, us);
            Assert.That(LineOfSightUtilities.HasLineOfSight(end, EnemyCenter, terrain), Is.True,
                $"the move must end on a clear firing lane, not in the wall's shadow; ended at " +
                $"({end.x:F1},{end.z:F1})\n" + DecisionTable());
        }

        [Test]
        public void WalledShooter_ClearLaneEngage_OutscoresHoldInShadow()
        {
            (DataBinding<UnitData> us, DataBinding<UnitData> them) = MakeWalledShooterScene();
            List<ITerrain> terrain = _tableState.Terrain.Objects.ToList();
            var planner = new TacticianPlanner(_tableState, _evaluator);
            planner.BeginActivation(us);
            List<MacroAction> candidates = MacroActionGenerator.Enumerate(_evaluator, _tableState, us);

            MacroAction hold = candidates.First(c => c.Intent == EMacroIntent.Hold);
            MacroAction clearEngage = candidates.First(c => c.Intent == EMacroIntent.EngageAtRange
                && ReferenceEquals(c.TargetEnemy, them.GetValue())
                && LineOfSightUtilities.HasLineOfSight(c.ProjectedCentroid, EnemyCenter, terrain));

            Assert.That(planner.Score(clearEngage), Is.GreaterThan(planner.Score(hold)),
                "a real volley from the clear lane must outscore standing in the shadow with a " +
                "phantom one - phantom offense propping up bad candidates is the #363 failure");
        }

        // --- Helpers (mirroring TacticianWalledUnitTests). ---

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

        private static void AttachRule(DataBinding<UnitData> unit, string name, SpecialRuleDefinition def) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule(name, def));

        private string DecisionTable() =>
            _decisions.Count == 0 ? "(no decision log)" : string.Join("\n", _decisions);

        private static Position EndCentroid(List<ModelMoveEntry> move, DataBinding<UnitData> unit)
        {
            var ends = move.Where(e => e.Positions.Count > 0).Select(e => e.Positions[^1]).ToList();
            if (ends.Count == 0)
            {
                var alive = unit.GetValue().Models.Where(m => m.GetIsAlive()).ToList();
                return new Position(alive.Average(m => m.Position.x), alive.Average(m => m.Position.z));
            }
            return new Position(ends.Average(p => p.x), ends.Average(p => p.z));
        }
    }
}
