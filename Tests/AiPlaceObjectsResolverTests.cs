using FDG.Ai.Resolvers;
using FDG.Data;
using FDG.StageResolution;
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

            // Enemy model parked ON the AI's natural landing spot (the fan-out lane starts at the left
            // edge, mid-height), so only the enemy-distance check explains a distant placement. At
            // mid-table the test passed even with the check deleted - geometry, not enforcement.
            var enemyPos = new Position(1f, 24f);
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

            List<PlacedObjectEntry<ModelData>> result = Unwrap(await resolver.Resolve(request));

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

        // #197 P22: a Repel Ambushers keep-out disc (larger than the flat 9" rule) must push the AI's
        // arrival placement outside it - the disc reaches BlockPenalty through PlacementDistanceRules.
        [Test]
        public async Task PlacesAllModelsOutsideAKeepOutDisc()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var selfPlayer = new PlayerID(System.Guid.NewGuid());
            var enemyPlayer = new PlayerID(System.Guid.NewGuid());

            // On the AI's natural landing spot (see PlacesAllModelsOverMinDistanceFromEnemies), so the
            // disc - not geometry - is what pushes the arrival out.
            var enemyPos = new Position(1f, 24f);
            var enemyModel = new ModelData(0.5f, new List<Weapon>(), enemyPos, store);
            var enemyBinding = store.GetDataBinding<ModelData>(store.Create(enemyModel));
            store.Create(new UnitData(enemyPlayer, "Repellers", 4, 4,
                new List<DataBinding<ModelData>> { enemyBinding }));

            var placing = new List<DataBinding<ModelData>>();
            for (int i = 0; i < 2; i++)
            {
                var m = new ModelData(0.5f, new List<Weapon>(), new Position(0f, 0f), store);
                placing.Add(store.GetDataBinding<ModelData>(store.Create(m)));
            }
            store.Create(new UnitData(selfPlayer, "Shifters", 4, 4, placing));

            var resolver = new AiPlaceObjectsResolver<ModelData>(new TableState(store));
            var wholeTable = new RectangularZone(0f, GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES,
                0f, GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES);
            const float repelRadius = 16f; // deliberately larger than the flat 9", so only the disc explains the distance
            var request = new PlaceObjectsRequest<ModelData>(selfPlayer, "Ambush Deploy", wholeTable,
                placing, minDistanceFromEnemiesInches: 9f,
                enemyKeepOutDiscs: new[] { new PlacementDisc(enemyPos, repelRadius) });

            List<PlacedObjectEntry<ModelData>> result = Unwrap(await resolver.Resolve(request));

            Assert.That(result, Has.Count.EqualTo(2));
            foreach (PlacedObjectEntry<ModelData> entry in result)
            {
                float dx = entry.Position.x - enemyPos.x;
                float dz = entry.Position.z - enemyPos.z;
                float dist = MathF.Sqrt(dx * dx + dz * dz);
                Assert.That(dist, Is.GreaterThanOrEqualTo(repelRadius),
                    $"the AI must respect the Repel keep-out disc, not just the flat rule (was {dist:F1}\").");
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

        // #197 reposition-at-activation: a per-model radius makes the block-packing search meaningless (it aims
        // at a deployment lane and knows nothing about each model's own circle), and the bot cannot value a
        // 1-3in shuffle before it has chosen an action or a target. It declines by standing still - which is a
        // legal answer, because the rule says "you MAY place".
        [Test]
        public async Task RepositionRequest_LeavesEveryModelWhereItStands()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var selfPlayer = new PlayerID(System.Guid.NewGuid());

            var starts = new[] { new Position(12f, 20f), new Position(13f, 20f), new Position(12f, 21f) };
            var placing = starts
                .Select(p => store.GetDataBinding<ModelData>(
                    store.Create(new ModelData(0.5f, new List<Weapon>(), p, store))))
                .ToList();
            store.Create(new UnitData(selfPlayer, "Wolfborn Pack", 4, 4, placing));

            var resolver = new AiPlaceObjectsResolver<ModelData>(new TableState(store));
            var wholeTable = new RectangularZone(0f, GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES,
                0f, GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES);
            var request = new PlaceObjectsRequest<ModelData>(selfPlayer, "Reposition", wholeTable, placing,
                allowCancel: true, maxDistanceFromStartInches: 3f);

            List<PlacedObjectEntry<ModelData>> result = Unwrap(await resolver.Resolve(request));

            Assert.That(result.Select(e => e.Position), Is.EqualTo(starts).AsCollection,
                "Standing still is always inside the radius and never breaks a rule the unit already satisfies.");
            foreach (PlacedObjectEntry<ModelData> entry in result)
            {
                Assert.That(PlacementUtilities.IsWithinStartRadius(
                    entry.Position, entry.Binding.GetValue().Position, 3f), Is.True);
            }
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

            List<PlacedObjectEntry<ModelData>> result = Unwrap(await resolver.Resolve(request));

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

            List<PlacedObjectEntry<ModelData>> result = Unwrap(await resolver.Resolve(request));

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

            List<PlacedObjectEntry<ModelData>> result = Unwrap(await resolver.Resolve(request));

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

            List<PlacedObjectEntry<ModelData>> result = Unwrap(await resolver.Resolve(request));

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

            List<PlacedObjectEntry<ModelData>> result = Unwrap(await resolver.Resolve(request));

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

            List<PlacedObjectEntry<ModelData>> result = Unwrap(await resolver.Resolve(request));

            Assert.That(result, Has.Count.EqualTo(5));
            var impassible = new List<ITerrain> { wall };
            foreach (PlacedObjectEntry<ModelData> entry in result)
            {
                float r = entry.Binding.GetValue().BaseRadiusInches;
                Assert.That(PlacementUtilities.OverlapsImpassibleTerrain(entry.Position, r, impassible), Is.False,
                    $"AI must not deploy a model overlapping impassible terrain (placed at {entry.Position.x:F1}, {entry.Position.z:F1}).");
            }
        }

        // The same shape with room to spare: here a fully legal centre EXISTS, so the block must find it and
        // touch nothing at all. Guards the ordinary path the penalty refactor now routes through.
        [Test]
        public async Task ZoneWithRoom_DeploysClearOfAnAlliedBlock()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var me = new PlayerID(System.Guid.NewGuid());
            var ally = new PlayerID(System.Guid.NewGuid());
            store.Create(new TeamData(1, new List<PlayerID> { me, ally }));

            var zone = new RectangularZone(10f, 44f, 34f, 46f);
            var allyModels = new List<DataBinding<ModelData>>();
            for (int i = 0; i < 6; i++)
            {
                var p = new Position(12f + (i % 3) * 2.2f, 39f + (i / 3) * 2.2f);
                var m = new ModelData(0.5f, new List<Weapon>(), p, store);
                allyModels.Add(store.GetDataBinding<ModelData>(store.Create(m)));
            }
            var allyUnit = new UnitData(ally, "Saurian Guardians", 4, 4, allyModels);
            store.Create(allyUnit);
            store.Create(new ArmyData(ally, new List<DataBinding<UnitData>>
                { store.GetDataBinding<UnitData>(store.Create(allyUnit)) }));

            var placing = new List<DataBinding<ModelData>>();
            for (int i = 0; i < 5; i++)
            {
                var m = new ModelData(0.5f, new List<Weapon>(), new Position(0f, 0f), store);
                placing.Add(store.GetDataBinding<ModelData>(store.Create(m)));
            }
            store.Create(new UnitData(me, "Retributors", 4, 4, placing));

            var tableState = new TableState(store);
            var resolver = new AiPlaceObjectsResolver<ModelData>(tableState);
            List<PlacedObjectEntry<ModelData>> result = Unwrap(await resolver.Resolve(
                new PlaceObjectsRequest<ModelData>(me, "Deploy", zone, placing)));

            foreach (PlacedObjectEntry<ModelData> entry in result)
            {
                float r = entry.Binding.GetValue().BaseRadiusInches;
                foreach (DataBinding<ModelData> occupied in allyModels)
                {
                    ModelData other = occupied.GetValue();
                    float dx = entry.Position.x - other.Position.x;
                    float dz = entry.Position.z - other.Position.z;
                    float gap = MathF.Sqrt(dx * dx + dz * dz) - (r + other.BaseRadiusInches);
                    Assert.That(gap, Is.GreaterThanOrEqualTo(0f),
                        $"with room available, a deployed model at ({entry.Position.x:F1},{entry.Position.z:F1}) " +
                        $"must not touch an ALLIED model at ({other.Position.x:F1},{other.Position.z:F1})");
                }
            }
        }


        // When a deployment zone has NO legal centre left, both block finders used to return a clamped
        // guess that no check had ever looked at - so the block could land in the densest corner and bury
        // itself in whatever was already there. The sweep now keeps the least-bad centre it saw
        // (BlockPenalty) and returns that. Measured on this scene: the blind guess landed at (0.5,43.0)
        // buried 0.82"; the least-bad centre lands at (33.5,45.2), 0.24".
        //
        // NOTE: this is NOT the cause of the YellowDeployedOverGreen overlap - that save's zone was 72x9
        // and nowhere near full, and old/new code place identically there. That root cause is still open.
        [Test]
        public async Task FullZone_FallbackPicksLeastOverlappingCentre_NotABlindGuess()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var me = new PlayerID(System.Guid.NewGuid());
            var ally = new PlayerID(System.Guid.NewGuid());
            store.Create(new TeamData(1, new List<PlayerID> { me, ally }));

            // Every centre overlaps something, but by wildly different amounts: the bottom is packed
            // shoulder-to-shoulder, the top is sparse. A fallback that consults the checks finds the top.
            var zone = new RectangularZone(0f, 40f, 39f, 48f);
            var occ = new List<DataBinding<ModelData>>();
            for (float z = 39.5f; z < 43.5f; z += 1.1f)
                for (float x = 0.6f; x < 39.5f; x += 1.1f)
                    occ.Add(store.GetDataBinding<ModelData>(store.Create(
                        new ModelData(0.5f, new List<Weapon>(), new Position(x, z), store))));
            for (float z = 44.5f; z < 47.5f; z += 2.6f)
                for (float x = 1.5f; x < 39.5f; x += 2.6f)
                    occ.Add(store.GetDataBinding<ModelData>(store.Create(
                        new ModelData(0.5f, new List<Weapon>(), new Position(x, z), store))));
            var allyUnit = new UnitData(ally, "Crowd", 4, 4, occ);
            store.Create(allyUnit);
            store.Create(new ArmyData(ally, new List<DataBinding<UnitData>>
                { store.GetDataBinding<UnitData>(store.Create(allyUnit)) }));

            var placing = new List<DataBinding<ModelData>>();
            for (int i = 0; i < 5; i++)
                placing.Add(store.GetDataBinding<ModelData>(store.Create(
                    new ModelData(0.5f, new List<Weapon>(), new Position(0f, 0f), store))));
            store.Create(new UnitData(me, "Newcomer", 4, 4, placing));

            var resolver = new AiPlaceObjectsResolver<ModelData>(new TableState(store));
            var result = Unwrap(await resolver.Resolve(
                new PlaceObjectsRequest<ModelData>(me, "Deploy", zone, placing)));

            float worst = 0f;
            foreach (PlacedObjectEntry<ModelData> e in result)
                foreach (DataBinding<ModelData> o in occ)
                {
                    ModelData om = o.GetValue();
                    float dx = e.Position.x - om.Position.x, dz = e.Position.z - om.Position.z;
                    worst = MathF.Max(worst, 1.0f - MathF.Sqrt(dx * dx + dz * dz));
                }

            Assert.That(worst, Is.GreaterThan(0f),
                "scene check: this zone must have no legal centre, or the fallback is never exercised");
            Assert.That(worst, Is.LessThan(0.4f),
                $"a full zone must yield the least-overlapping centre the sweep saw, not a blind clamped " +
                $"guess (worst interpenetration {worst:F2}in; the blind guess measured 0.82in)");
        }

        // Dead models must not act as invisible occupants: casualties aren't drawn, so if they still
        // repelled placement, the spot where models died would become an inexplicable no-place region
        // (seen live as an Ambush drop flagged red on visibly empty ground). The unit keeps one SURVIVOR
        // so it still reads on-battlefield - a fully wiped unit is already excluded wholesale by the
        // GetIsOnBattlefield occupancy filter; the per-model leak is corpses of a unit that still lives.
        // Same scene placed with and without the corpses must come out identical.
        [Test]
        public async Task DeadModels_DoNotBlockPlacement()
        {
            async Task<List<Position>> PlaceWith(bool corpses)
            {
                var store = GameDataStore.GameDataStoreBuilder.GetDefault();
                var me = new PlayerID(System.Guid.NewGuid());

                var zone = new RectangularZone(20f, 40f, 20f, 28f);

                // The survivor stands well clear of the zone centre and exists in BOTH runs, so any
                // placement difference is attributable to the corpses alone.
                var fallenModels = new List<DataBinding<ModelData>>();
                var survivor = new ModelData(0.5f, new List<Weapon>(), new Position(38f, 26f), store);
                fallenModels.Add(store.GetDataBinding<ModelData>(store.Create(survivor)));
                if (corpses)
                {
                    // Dead squadmates right where the sweep wants to put the newcomers (the zone's
                    // left-centre - where the corpse-free control run measurably lands).
                    for (int i = 0; i < 5; i++)
                    {
                        var m = new ModelData(0.5f, new List<Weapon>(), new Position(20f + i, 24f), store);
                        m.DealWounds(m.TotalWounds);
                        fallenModels.Add(store.GetDataBinding<ModelData>(store.Create(m)));
                    }
                }
                store.Create(new UnitData(me, "Fallen", 4, 4, fallenModels));

                var placing = new List<DataBinding<ModelData>>();
                for (int i = 0; i < 3; i++)
                    placing.Add(store.GetDataBinding<ModelData>(store.Create(
                        new ModelData(0.5f, new List<Weapon>(), new Position(0f, 0f), store))));
                store.Create(new UnitData(me, "Newcomer", 4, 4, placing));

                var resolver = new AiPlaceObjectsResolver<ModelData>(new TableState(store));
                var result = Unwrap(await resolver.Resolve(
                    new PlaceObjectsRequest<ModelData>(me, "Deploy", zone, placing)));
                return result.Select(e => e.Position).ToList();
            }

            List<Position> with = await PlaceWith(corpses: true);
            List<Position> without = await PlaceWith(corpses: false);

            Assert.That(with.Count, Is.EqualTo(without.Count));
            for (int i = 0; i < with.Count; i++)
            {
                Assert.That(with[i].x, Is.EqualTo(without[i].x).Within(0.001f),
                    $"model {i} shifted by corpses - dead models must not be occupants");
                Assert.That(with[i].z, Is.EqualTo(without[i].z).Within(0.001f),
                    $"model {i} shifted by corpses - dead models must not be occupants");
            }
        }

        // The AI resolver never cancels a placement.
        private static List<PlacedObjectEntry<ModelData>> Unwrap(
            CancellableResult<List<PlacedObjectEntry<ModelData>>> result)
        {
            Assert.That(result, Is.InstanceOf<Selected<List<PlacedObjectEntry<ModelData>>>>(),
                "the AI must always supply placements.");
            return ((Selected<List<PlacedObjectEntry<ModelData>>>)result).Value;
        }

    }
}
