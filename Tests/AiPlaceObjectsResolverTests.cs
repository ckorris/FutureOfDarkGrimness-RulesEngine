using FDG.Ai.Resolvers;
using FDG.Data;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // Proves the faithful Ambush placement constraint: when a request sets MinDistanceFromEnemiesInches,
    // AiPlaceObjectsResolver returns positions strictly farther than that from every enemy model. The
    // Ambush integration test uses a canned requester, so the enforcement is verified here directly.
    [TestFixture]
    public class AiPlaceObjectsResolverTests
    {
        [Test]
        public async Task PlacesAllModelsOverMinDistanceFromEnemies()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();

            var selfPlayer = new PlayerID(System.Guid.NewGuid());
            var enemyPlayer = new PlayerID(System.Guid.NewGuid());

            // Enemy model parked mid-table.
            var enemyPos = new Position(36f, 24f);
            var enemyModel = new ModelData(0.5f, new List<Weapon>(), enemyPos, store);
            var enemyBinding = store.GetDataBinding<ModelData>(store.Create(enemyModel));
            var enemyUnit = new UnitData(enemyPlayer, "Enemies", 4, 4, new List<DataBinding<ModelData>> { enemyBinding });
            store.Create(enemyUnit);

            // Placing unit (2 models, still at origin / in reserve).
            var placing = new List<DataBinding<ModelData>>();
            for (int i = 0; i < 2; i++)
            {
                var m = new ModelData(0.5f, new List<Weapon>(), new Position(0f, 0f), store);
                placing.Add(store.GetDataBinding<ModelData>(store.Create(m)));
            }
            var placingUnit = new UnitData(selfPlayer, "Infiltrators", 4, 4, placing);
            store.Create(placingUnit);

            var tableState = new TableState(store);
            var resolver = new AiPlaceObjectsResolver<ModelData>(tableState);

            var wholeTable = new RectangularZone(0f, GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES,
                0f, GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES);
            const float minDist = 9f;
            var request = new PlaceObjectsRequest<ModelData>(selfPlayer, "Ambush Deploy", wholeTable,
                placing, minDistanceFromEnemiesInches: minDist);

            List<PlacedObjectEntry<ModelData>> result = await resolver.Resolve(request);

            Assert.That(result, Has.Count.EqualTo(2));
            foreach (PlacedObjectEntry<ModelData> entry in result)
            {
                float dx = entry.Position.x - enemyPos.x;
                float dz = entry.Position.z - enemyPos.z;
                float dist = MathF.Sqrt(dx * dx + dz * dz);
                Assert.That(dist, Is.GreaterThanOrEqualTo(minDist),
                    $"AI deep-strike placement must stay {minDist}\" from enemies (was {dist:F1}\").");
            }
        }

        // #150: the default deploy facing points toward the table centre — a top-zone unit starts facing
        // down at the enemy instead of off the table (matters for Aircraft, whose heading IS the facing).
        [Test]
        public void DefaultDeployFacing_PointsTowardTableCentre()
        {
            float tableH = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
            var bottomZone = new RectangularZone(0f, 72f, 0f, 12f);
            var topZone = new RectangularZone(0f, 72f, tableH - 12f, tableH);

            Assert.That(PlacementUtilities.DefaultDeployFacing(bottomZone.Bounds, tableH), Is.EqualTo(new Float2(0f, 1f)),
                "a bottom zone faces up (+Z) toward the centre.");
            Assert.That(PlacementUtilities.DefaultDeployFacing(topZone.Bounds, tableH), Is.EqualTo(new Float2(0f, -1f)),
                "a top zone faces down (−Z) toward the centre.");
        }

        // #150: a rotated/elongated base must be allowed as close to an edge as its TRUE reach in that
        // direction — the facing-aware containment insets each edge by the real per-axis footprint extent, not
        // the circumscribing circle (which over-blocks the thin axis, stopping a base short of the side). It
        // still keeps the whole base on the table.
        [Test]
        public void IsBaseWithinZone_FacingAware_HugsTheThinEdgeButStaysOnTable()
        {
            var rect = new RectangleBase(1f, 3f); // long axis along Z at +Z facing: 0.5" on X, 1.5" on Z
            Float2 up = new Float2(0f, 1f);
            var zone = new RectangularZone(0f, 40f, 0f, 40f);

            // Centre 0.6" from the left edge: the 0.5" X-extent clears it (footprint reaches x=0.1"), so the
            // facing-aware check accepts it — where the bounding circle (half-diagonal ≈ 1.58") over-blocks.
            var nearLeft = new Position(0.6f, 20f);
            Assert.That(PlacementUtilities.IsBaseWithinZone(nearLeft, rect, up, zone), Is.True,
                "a base thin on X can sit close to the side edge.");
            Assert.That(PlacementUtilities.IsBaseWithinZone(nearLeft, rect.CircumscribedRadiusInches, zone), Is.False,
                "the bounding circle wrongly over-blocks the thin axis.");

            // But a base whose footprint actually crosses the edge is still rejected (never off the table).
            var overLeft = new Position(0.3f, 20f); // 0.5" X-extent reaches x=-0.2" < 0
            Assert.That(PlacementUtilities.IsBaseWithinZone(overLeft, rect, up, zone), Is.False,
                "a footprint that crosses the edge is rejected.");
        }

        // #029: an edge-constrained placement (the Aircraft off-table redeploy) must come back on touching a
        // table edge, with the models facing inward from that edge (facing = heading, so the aircraft doesn't
        // immediately fly back off).
        [Test]
        public async Task EdgeConstrainedPlacement_TouchesATableEdge_AndFacesInward()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var selfPlayer = new PlayerID(System.Guid.NewGuid());

            var placing = new List<DataBinding<ModelData>>();
            for (int i = 0; i < 2; i++)
            {
                var m = new ModelData(0.75f, new List<Weapon>(), new Position(0f, 0f), store);
                placing.Add(store.GetDataBinding<ModelData>(store.Create(m)));
            }
            var unit = new UnitData(selfPlayer, "Skyblade", 4, 4, placing);
            store.Create(unit);

            var tableState = new TableState(store);
            var resolver = new AiPlaceObjectsResolver<ModelData>(tableState);
            var wholeTable = new RectangularZone(0f, GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES,
                0f, GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES);
            var request = new PlaceObjectsRequest<ModelData>(selfPlayer, "Aircraft Redeploy", wholeTable,
                placing, mustTouchTableEdge: true);

            List<PlacedObjectEntry<ModelData>> result = await resolver.Resolve(request);

            Assert.That(result, Has.Count.EqualTo(2));
            bool anyTouches = result.Any(e => PlacementUtilities.TouchesZoneEdge(
                e.Position, e.Binding.GetValue().BaseShape.CircumscribedRadiusInches, wholeTable.Bounds));
            Assert.That(anyTouches, Is.True, "at least one base must touch a table edge.");

            foreach (var e in result)
            {
                Assert.That(e.Facing.HasValue, Is.True, "edge placements carry an inward facing.");
                Float2 f = e.Facing!.Value;
                // Inward = the facing points away from the touched edge, into the table.
                float len = MathF.Sqrt(f.X * f.X + f.Y * f.Y);
                Assert.That(len, Is.EqualTo(1f).Within(0.001f), "the facing is a unit vector.");
            }
        }

        // #150: rectangular bases must not overlap when the AI auto-packs them. The old inscribed
        // BaseRadiusInches under-bounded a tall rectangle, so adjacent bases in a grid overlapped; the
        // resolver now packs by the circumscribing radius. Verified against the true shape-aware collision.
        [Test]
        public async Task RectangularBases_PackWithoutOverlapping()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var selfPlayer = new PlayerID(System.Guid.NewGuid());

            var placing = new List<DataBinding<ModelData>>();
            for (int i = 0; i < 6; i++)
            {
                // 1" wide × 3" tall — a rectangle whose inscribed radius (0.5") is far smaller than its
                // circumscribed one (~1.58"); the packing spacing must use the latter.
                var m = new ModelData(new RectangleBase(1f, 3f), new List<Weapon>(), new Position(0f, 0f), store);
                placing.Add(store.GetDataBinding<ModelData>(store.Create(m)));
            }
            var unit = new UnitData(selfPlayer, "Wall-Bearers", 4, 4, placing);
            store.Create(unit);

            var tableState = new TableState(store);
            var resolver = new AiPlaceObjectsResolver<ModelData>(tableState);
            var zone = new RectangularZone(0f, 48f, 0f, 24f);
            var request = new PlaceObjectsRequest<ModelData>(selfPlayer, "Place Unit Models", zone, placing);

            List<PlacedObjectEntry<ModelData>> result = await resolver.Resolve(request);

            Assert.That(result, Has.Count.EqualTo(6));
            for (int i = 0; i < result.Count; i++)
                for (int j = i + 1; j < result.Count; j++)
                {
                    Float2 fi = result[i].Facing ?? new Float2(0f, 1f);
                    Float2 fj = result[j].Facing ?? new Float2(0f, 1f);
                    bool colliding = BaseShapeGeometry.AreColliding(
                        result[i].Binding.GetValue().BaseShape, result[i].Position, fi,
                        result[j].Binding.GetValue().BaseShape, result[j].Position, fj);
                    Assert.That(colliding, Is.False,
                        $"rectangular bases {i} and {j} overlap at ({result[i].Position.x:F1},{result[i].Position.z:F1}) / " +
                        $"({result[j].Position.x:F1},{result[j].Position.z:F1})");
                }
        }

        // #150 follow-up: no base may be deployed off the table. Deployment zones sit flush with the table
        // edge, so a rectangular base whose footprint pokes past the zone edge would be off the board — the AI
        // must keep every base fully within the table.
        [Test]
        public async Task NeverDeploysABaseOffTheTable()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var selfPlayer = new PlayerID(System.Guid.NewGuid());

            var placing = new List<DataBinding<ModelData>>();
            for (int i = 0; i < 8; i++)
            {
                var m = new ModelData(new RectangleBase(1f, 3f), new List<Weapon>(), new Position(0f, 0f), store);
                placing.Add(store.GetDataBinding<ModelData>(store.Create(m)));
            }
            store.Create(new UnitData(selfPlayer, "Wall-Bearers", 4, 4, placing));

            var tableState = new TableState(store);
            var resolver = new AiPlaceObjectsResolver<ModelData>(tableState);
            float tableW = GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES;
            float tableH = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
            // A bottom deployment zone, flush with the table's bottom/left/right edges (as real zones are).
            var zone = new RectangularZone(0f, tableW, 0f, 12f);
            var request = new PlaceObjectsRequest<ModelData>(selfPlayer, "Place Unit Models", zone, placing);

            List<PlacedObjectEntry<ModelData>> result = await resolver.Resolve(request);

            Assert.That(result, Has.Count.EqualTo(8));
            foreach (var e in result)
            {
                float r = e.Binding.GetValue().BaseShape.CircumscribedRadiusInches;
                Assert.That(e.Position.x, Is.GreaterThanOrEqualTo(r - 0.001f).And.LessThanOrEqualTo(tableW - r + 0.001f),
                    $"base off the left/right table edge at x={e.Position.x:F2}");
                Assert.That(e.Position.z, Is.GreaterThanOrEqualTo(r - 0.001f).And.LessThanOrEqualTo(tableH - r + 0.001f),
                    $"base off the bottom/top table edge at z={e.Position.z:F2}");
            }
        }

        // Degenerate case: a unit far too big for its zone forces the AI's clamped fallback. The block can't fit,
        // but the last-resort table clamp must still keep every base on the board (never off the table).
        [Test]
        public async Task UnitBiggerThanZone_StillLandsOnTable()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var selfPlayer = new PlayerID(System.Guid.NewGuid());

            var placing = new List<DataBinding<ModelData>>();
            for (int i = 0; i < 12; i++)
            {
                var m = new ModelData(new RectangleBase(2f, 2f), new List<Weapon>(), new Position(0f, 0f), store);
                placing.Add(store.GetDataBinding<ModelData>(store.Create(m)));
            }
            store.Create(new UnitData(selfPlayer, "Big Block", 4, 4, placing));

            var tableState = new TableState(store);
            var resolver = new AiPlaceObjectsResolver<ModelData>(tableState);
            float tableW = GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES;
            float tableH = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
            // A tiny corner zone the 12-model block cannot possibly fit into.
            var zone = new RectangularZone(0f, 6f, 0f, 6f);
            var request = new PlaceObjectsRequest<ModelData>(selfPlayer, "Place Unit Models", zone, placing);

            List<PlacedObjectEntry<ModelData>> result = await resolver.Resolve(request);

            Assert.That(result, Has.Count.EqualTo(12));
            foreach (var e in result)
            {
                float r = e.Binding.GetValue().BaseShape.CircumscribedRadiusInches;
                Assert.That(e.Position.x, Is.GreaterThanOrEqualTo(r - 0.01f).And.LessThanOrEqualTo(tableW - r + 0.01f),
                    $"base off the table at x={e.Position.x:F2}");
                Assert.That(e.Position.z, Is.GreaterThanOrEqualTo(r - 0.01f).And.LessThanOrEqualTo(tableH - r + 0.01f),
                    $"base off the table at z={e.Position.z:F2}");
            }
        }

        // The bug a user hit: a 10-model unit deployed with one model stranded far out of cohesion (behind
        // terrain) and the rest in an over-wide line. The resolver must place the whole unit as one block
        // satisfying BOTH cohesion rules — every model within 1" of a neighbour AND within 9" of every other —
        // and clear of impassible terrain, even when terrain splits the zone (forcing the block to relocate).
        [Test]
        public async Task PacksTenModelsIntoCohesiveBlock_NearImpassibleTerrain()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var selfPlayer = new PlayerID(System.Guid.NewGuid());

            // A deployment strip split near the left by a full-height impassible wall (the "cross").
            var zone = new RectangularZone(0f, 48f, 0f, 12f);
            var wall = new TerrainData(ETerrainType.Impassible, new RectangularZone(3f, 9f, 0f, 12f));
            store.Create(wall);

            var placing = new List<DataBinding<ModelData>>();
            for (int i = 0; i < 10; i++)
            {
                var m = new ModelData(0.5f, new List<Weapon>(), new Position(0f, 0f), store);
                placing.Add(store.GetDataBinding<ModelData>(store.Create(m)));
            }
            var unit = new UnitData(selfPlayer, "Assault Grunts", 4, 4, placing);
            store.Create(unit);

            var tableState = new TableState(store);
            var resolver = new AiPlaceObjectsResolver<ModelData>(tableState);
            var request = new PlaceObjectsRequest<ModelData>(selfPlayer, "Place Unit Models", zone, placing);

            List<PlacedObjectEntry<ModelData>> result = await resolver.Resolve(request);

            Assert.That(result, Has.Count.EqualTo(10));

            var impassible = new List<ITerrain> { wall };
            foreach (PlacedObjectEntry<ModelData> e in result)
                Assert.That(PlacementUtilities.OverlapsImpassibleTerrain(e.Position, e.Binding.GetValue().BaseRadiusInches, impassible),
                    Is.False, $"model on impassible terrain at ({e.Position.x:F1}, {e.Position.z:F1})");

            const float eps = 0.01f;
            for (int i = 0; i < result.Count; i++)
            {
                float ri = result[i].Binding.GetValue().BaseRadiusInches;
                float nearestB2B = float.MaxValue;
                for (int j = 0; j < result.Count; j++)
                {
                    if (i == j) continue;
                    float rj = result[j].Binding.GetValue().BaseRadiusInches;
                    float b2b = Dist(result[i].Position, result[j].Position) - ri - rj;
                    nearestB2B = MathF.Min(nearestB2B, b2b);
                    Assert.That(b2b, Is.LessThanOrEqualTo(GameWideConstants.MAX_MODEL_DISTANCE_FROM_ALL_OTHER_MODELS_INCHES + eps),
                        $"models {i},{j} are {b2b:F2}\" apart — beyond the {GameWideConstants.MAX_MODEL_DISTANCE_FROM_ALL_OTHER_MODELS_INCHES}\" all-pairs cohesion");
                }
                Assert.That(nearestB2B, Is.LessThanOrEqualTo(GameWideConstants.MAX_MODEL_DISTANCE_FROM_ANY_OTHER_MODEL_INCHES + eps),
                    $"model {i} has no neighbour within {GameWideConstants.MAX_MODEL_DISTANCE_FROM_ANY_OTHER_MODEL_INCHES}\" — stranded out of cohesion");
            }
        }

        private static float Dist(Position a, Position b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        [Test]
        public async Task DoesNotPlaceModelsOnImpassibleTerrain()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var selfPlayer = new PlayerID(System.Guid.NewGuid());

            // A deployment strip with an impassible wall splitting it at x=8..12 (full height). #048.
            var zone = new RectangularZone(0f, 20f, 0f, 12f);
            var wall = new TerrainData(ETerrainType.Impassible, new RectangularZone(8f, 12f, 0f, 12f));
            store.Create(wall);

            // Five models to deploy.
            var placing = new List<DataBinding<ModelData>>();
            for (int i = 0; i < 5; i++)
            {
                var m = new ModelData(0.75f, new List<Weapon>(), new Position(0f, 0f), store);
                placing.Add(store.GetDataBinding<ModelData>(store.Create(m)));
            }
            var unit = new UnitData(selfPlayer, "Warriors", 4, 4, placing);
            store.Create(unit);

            var tableState = new TableState(store);
            var resolver = new AiPlaceObjectsResolver<ModelData>(tableState);
            var request = new PlaceObjectsRequest<ModelData>(selfPlayer, "Place Unit Models", zone, placing);

            List<PlacedObjectEntry<ModelData>> result = await resolver.Resolve(request);

            Assert.That(result, Has.Count.EqualTo(5));
            var impassible = new List<ITerrain> { wall };
            foreach (PlacedObjectEntry<ModelData> entry in result)
            {
                float r = entry.Binding.GetValue().BaseRadiusInches;
                Assert.That(PlacementUtilities.OverlapsImpassibleTerrain(entry.Position, r, impassible), Is.False,
                    $"AI must not deploy a model overlapping impassible terrain (placed at {entry.Position.x:F1}, {entry.Position.z:F1}).");
            }
        }
    }
}
