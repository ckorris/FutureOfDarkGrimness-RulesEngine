using FDG.Ai.Resolvers;
using FDG.Data;
using FDG.StageResolution.Requests;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // Guards the AI movement resolver against #050's base-radius gap: DefinePathStage throws (no retry)
    // on an invalid move, so the AI must never emit a path the same MovementUtilities.ValidatePaths the
    // stage uses would reject — including the case where a zero-width centroid line clears terrain that
    // the model's base actually clips.
    [TestFixture]
    public class AiDefineMovementResolverTests
    {
        [Test]
        public async Task Resolve_StraightLineWouldClipImpassible_ResultIsEngineValid()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var selfPlayer = new PlayerID(System.Guid.NewGuid());
            var enemyPlayer = new PlayerID(System.Guid.NewGuid());

            // Mover: a single model at (0,5).
            var mover = new ModelData(0.75f, new List<Weapon>(), new Position(0f, 5f), store);
            var moverBinding = store.GetDataBinding<ModelData>(store.Create(mover));
            var moverUnit = new UnitData(selfPlayer, "Movers", 4, 4, 
                new List<DataBinding<ModelData>> { moverBinding });
            var moverUnitBinding = store.GetDataBinding<UnitData>(store.Create(moverUnit));

            // Enemy straight ahead at (20,5) — the AI advances east along z=5.
            var enemyPos = new Position(20f, 5f);
            var enemy = new ModelData(0.75f, new List<Weapon>(), enemyPos, store);
            var enemyBinding = store.GetDataBinding<ModelData>(store.Create(enemy));
            var enemyUnit = new UnitData(enemyPlayer, "Enemies", 4, 4, 
                new List<DataBinding<ModelData>> { enemyBinding });
            store.Create(enemyUnit);

            // Impassible wall across x=8..10 with its top edge at z=4.2: the zero-width centroid line at
            // z=5 clears it, but the 0.75" base reaches down to z=4.25 and clips it — the #050 gap. The
            // old centroid-only, zero-width pre-check would have advanced straight through into an invalid move.
            var wall = new TerrainData(ETerrainType.Impassible, new RectangularZone(8f, 10f, -2f, 4.2f));
            store.Create(wall);

            var tableState = new TableState(store);
            var resolver = new AiDefineMovementResolver(tableState, selfPlayer);

            var request = new DefineMovementPathRequest(selfPlayer, "Move", moverUnitBinding,
                maxAdvanceDistance: 12f, maxRushDistance: 12f, maxDistanceInches: 12f);

            List<ModelMoveEntry> result = await resolver.Resolve(request);

            bool valid = MovementUtilities.ValidatePaths(result, request.MaxRushDistance,
                request.MaxDistanceInches, new List<Position> { enemyPos },
                new List<ITerrain> { wall }, out var errors);

            Assert.That(valid, Is.True,
                "AI must never emit a move the engine rejects: " + string.Join(", ", errors.Select(e => e.ToString())));
        }

        [Test]
        public async Task Resolve_ClearLane_AdvancesTowardEnemy()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var selfPlayer = new PlayerID(System.Guid.NewGuid());
            var enemyPlayer = new PlayerID(System.Guid.NewGuid());

            // Mover: three cohesive models around (0,24).
            var moverBindings = new List<DataBinding<ModelData>>();
            foreach (var z in new[] { 23.5f, 24f, 24.5f })
            {
                var m = new ModelData(0.75f, new List<Weapon>(), new Position(0f, z), store);
                moverBindings.Add(store.GetDataBinding<ModelData>(store.Create(m)));
            }
            var moverUnit = new UnitData(selfPlayer, "Movers", 4, 4, moverBindings);
            var moverUnitBinding = store.GetDataBinding<UnitData>(store.Create(moverUnit));

            // Enemy far to the east, clear lane (no terrain).
            var enemyPos = new Position(40f, 24f);
            var enemy = new ModelData(0.75f, new List<Weapon>(), enemyPos, store);
            var enemyBinding = store.GetDataBinding<ModelData>(store.Create(enemy));
            var enemyUnit = new UnitData(enemyPlayer, "Enemies", 4, 4, 
                new List<DataBinding<ModelData>> { enemyBinding });
            store.Create(enemyUnit);

            var tableState = new TableState(store);
            var resolver = new AiDefineMovementResolver(tableState, selfPlayer);

            var request = new DefineMovementPathRequest(selfPlayer, "Move", moverUnitBinding,
                maxAdvanceDistance: 6f, maxRushDistance: 12f, maxDistanceInches: 12f);

            List<ModelMoveEntry> result = await resolver.Resolve(request);

            bool valid = MovementUtilities.ValidatePaths(result, request.MaxRushDistance,
                request.MaxDistanceInches, new List<Position> { enemyPos },
                new List<ITerrain>(), out var errors);
            Assert.That(valid, Is.True,
                "Clear-lane advance must be valid: " + string.Join(", ", errors.Select(e => e.ToString())));

            // The back-off must not neuter normal movement: some model ends east of its start.
            bool advanced = result.Any(e => e.Positions.Count > 0 && e.Positions[e.Positions.Count - 1].x > 0.5f);
            Assert.That(advanced, Is.True, "AI should advance toward the enemy when the lane is clear.");
        }

        [Test]
        public async Task Resolve_CasualtyThinnedUnit_NoEnemies_ReformsToCohesionInsteadOfCrashing()
        {
            // A 5-model unit deployed in a row loses its middle 3 to casualties, leaving two survivors far
            // apart. With no enemies the AI "stays put" — but a literal stay submits those two survivors
            // >1" apart, which DefinePathStage rejects (and crashes). Staying must reform them to cohesion.
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var selfPlayer = new PlayerID(System.Guid.NewGuid());

            const float r = 0.551f;
            const float spacing = 2f * r + 0.1f; // matches CohesiveFormation grid spacing
            var bindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < 5; i++)
            {
                var m = new ModelData(r, new List<Weapon>(), new Position(i * spacing, 20f), store);
                bindings.Add(store.GetDataBinding<ModelData>(store.Create(m)));
            }
            // Kill the middle three, leaving survivors at the two ends (~4.4" apart, far out of cohesion).
            foreach (int dead in new[] { 1, 2, 3 })
                bindings[dead].GetValue().DealWounds(bindings[dead].GetValue().TotalWounds);

            var unit = new UnitData(selfPlayer, "Survivors", 4, 4, bindings);
            var unitBinding = store.GetDataBinding<UnitData>(store.Create(unit));
            store.Create(new ArmyData(selfPlayer, new List<DataBinding<UnitData>> { unitBinding }));

            // No enemy units on the table → the AI takes the "stay in place" path.
            var tableState = new TableState(store);
            var resolver = new AiDefineMovementResolver(tableState, selfPlayer);

            var request = new DefineMovementPathRequest(selfPlayer, "Move", unitBinding,
                maxAdvanceDistance: 6f, maxRushDistance: 12f, maxDistanceInches: 12f);

            List<ModelMoveEntry> result = await resolver.Resolve(request);

            bool valid = MovementUtilities.ValidatePaths(result, request.MaxRushDistance,
                request.MaxDistanceInches, new List<Position>(), new List<ITerrain>(), out var errors);
            Assert.That(valid, Is.True,
                "Staying put with casualty survivors must still be an engine-valid (cohesive) move: "
                + string.Join(", ", errors.Select(e => e.ToString())));
            Assert.That(errors.Any(e => e.ErrorReasonType == EErrorReasonType.TooFarFromAnyUnitModel), Is.False);
        }
    }
}
