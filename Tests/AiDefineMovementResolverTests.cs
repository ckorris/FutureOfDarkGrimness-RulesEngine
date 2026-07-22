using FDG.Ai.Resolvers;
using FDG.Data;
using FDG.StageResolution;
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

            List<ModelMoveEntry> result = Unwrap(await resolver.Resolve(request));

            bool valid = MovementUtilities.ValidatePaths(result, request.MaxRushDistance,
                request.MaxDistanceInches, new List<EnemyModelFootprint> { new EnemyModelFootprint(enemyPos, 0.75f, 0) },
                new List<ITerrain> { wall }, out var errors);

            Assert.That(valid, Is.True,
                "AI must never emit a move the engine rejects: " + string.Join(", ", errors.Select(e => e.ToString())));
        }

        [Test]
        public async Task Resolve_AllEnemiesDead_StandStillNextToFriendly_ResultIsEngineValid()
        {
            // #256 bench fault (seed 1051): with every enemy dead (objectives keep the game going), the
            // resolver's early-out answered with an UNVALIDATED StayInPlace reform whose re-pack slot
            // landed on an adjacent friendly - DefinePathStage rejects end-on-friendly (#205) and faults.
            // Geometry copied from the fault: 3 movers whose centroid re-pack row overlaps the teammate.
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var selfPlayer = new PlayerID(System.Guid.NewGuid());
            const float r = 0.5511811f; // 28mm base

            var moverModels = new[]
                { new Position(18.474f, 31.937f), new Position(19.319f, 32.646f), new Position(20.521f, 32.646f) }
                .Select(p => store.GetDataBinding<ModelData>(store.Create(
                    new ModelData(r, new List<Weapon>(), p, store)))).ToList();
            var moverUnitBinding = store.GetDataBinding<UnitData>(store.Create(
                new UnitData(selfPlayer, "Gunners", 4, 4, moverModels)));

            // The adjacent friendly unit the reform's leftmost slot would land on.
            var friendlyPositions = new[]
            {
                new Position(15.485f, 31.610f), new Position(16.687f, 31.610f), new Position(17.288f, 32.812f),
                new Position(14.883f, 32.812f), new Position(16.086f, 32.812f),
            };
            var friendlyModels = friendlyPositions
                .Select(p => store.GetDataBinding<ModelData>(store.Create(
                    new ModelData(r, new List<Weapon>(), p, store)))).ToList();
            store.Create(new UnitData(selfPlayer, "Warriors", 4, 4, friendlyModels));

            // No enemy units at all - the early-out path.
            var tableState = new TableState(store);
            var resolver = new AiDefineMovementResolver(tableState, selfPlayer);
            var request = new DefineMovementPathRequest(selfPlayer, "Move", moverUnitBinding,
                maxAdvanceDistance: 6f, maxRushDistance: 12f, maxDistanceInches: 12f);

            List<ModelMoveEntry> result = Unwrap(await resolver.Resolve(request));

            var friendlyFootprints = friendlyPositions
                .Select(p => new EnemyModelFootprint(p, r, 0)).ToList();
            bool valid = MovementUtilities.ValidatePaths(result,
                _ => new ModelMoveBudget(12f, 12f), new List<EnemyModelFootprint>(),
                canMoveThroughEnemies: false, ignoresDifficultTerrain: false, ignoresImpassibleTerrain: false,
                new List<ITerrain>(), out var errors, friendlyFootprints, lenientCoherency: true);
            Assert.That(valid, Is.True,
                "a stand-still with no enemies must not end re-packed onto a friendly: "
                + string.Join(", ", errors.Select(e => e.ToString())));
        }

        [Test]
        public async Task Resolve_StraightPathBlockedByImpassible_SkirtsInsteadOfFreezing()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var selfPlayer = new PlayerID(System.Guid.NewGuid());
            var enemyPlayer = new PlayerID(System.Guid.NewGuid());

            // Mover at (0,24); enemy straight east at (40,24).
            Position start = new Position(0f, 24f);
            var mover = new ModelData(0.75f, new List<Weapon>(), start, store);
            var moverBinding = store.GetDataBinding<ModelData>(store.Create(mover));
            var moverUnit = new UnitData(selfPlayer, "Movers", 4, 4,
                new List<DataBinding<ModelData>> { moverBinding });
            var moverUnitBinding = store.GetDataBinding<UnitData>(store.Create(moverUnit));

            var enemyPos = new Position(40f, 24f);
            var enemy = new ModelData(0.75f, new List<Weapon>(), enemyPos, store);
            store.Create(new UnitData(enemyPlayer, "Enemies", 4, 4,
                new List<DataBinding<ModelData>> { store.GetDataBinding<ModelData>(store.Create(enemy)) }));

            // Impassible wall blocking the straight lane (x=8..10, z=20..28), clear above/below to skirt.
            var wall = new TerrainData(ETerrainType.Impassible, new RectangularZone(8f, 10f, 20f, 28f));
            store.Create(wall);

            var tableState = new TableState(store);
            var resolver = new AiDefineMovementResolver(tableState, selfPlayer);
            var request = new DefineMovementPathRequest(selfPlayer, "Move", moverUnitBinding,
                maxAdvanceDistance: 12f, maxRushDistance: 12f, maxDistanceInches: 12f);

            List<ModelMoveEntry> result = Unwrap(await resolver.Resolve(request));

            Position finalPos = result[0].Positions[result[0].Positions.Count - 1];
            Assert.That(Position.GetDistance2D(finalPos, start), Is.GreaterThan(2f),
                "a unit blocked straight ahead must skirt the terrain and advance, not freeze in place.");

            bool valid = MovementUtilities.ValidatePaths(result, request.MaxRushDistance,
                request.MaxDistanceInches, new List<EnemyModelFootprint> { new EnemyModelFootprint(enemyPos, 0.75f, 0) },
                new List<ITerrain> { wall }, out var errors);
            Assert.That(valid, Is.True,
                "the skirting move must still be engine-valid: " + string.Join(", ", errors.Select(e => e.ToString())));
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

            List<ModelMoveEntry> result = Unwrap(await resolver.Resolve(request));

            bool valid = MovementUtilities.ValidatePaths(result, request.MaxRushDistance,
                request.MaxDistanceInches, new List<EnemyModelFootprint> { new EnemyModelFootprint(enemyPos, 0.75f, 0) },
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

            List<ModelMoveEntry> result = Unwrap(await resolver.Resolve(request));

            bool valid = MovementUtilities.ValidatePaths(result, request.MaxRushDistance,
                request.MaxDistanceInches, new List<EnemyModelFootprint>(), new List<ITerrain>(), out var errors);
            Assert.That(valid, Is.True,
                "Staying put with casualty survivors must still be an engine-valid (cohesive) move: "
                + string.Join(", ", errors.Select(e => e.ToString())));
            Assert.That(errors.Any(e => e.ErrorReasonType == EErrorReasonType.TooFarFromAnyUnitModel), Is.False);
        }

        [Test]
        public async Task Resolve_MixedBaseCasualtyUnit_AdvancingTowardEnemy_ProducesValidMove()
        {
            // #159: a unit mixing one large base (r=1.5", e.g. a monster/joined hero) with small troopers
            // (r=0.5") that has taken a casualty (leaving the survivors out of cohesion) advances toward an
            // enemy. The re-pack fallbacks (PackGrid) can't reach cohesion for mixed bases, so the resolver
            // used to hold the survivors in their out-of-cohesion positions -> a move DefinePathStage rejects
            // (TooFarFromAnyUnitModel) and crashes the game. The resolver must never emit such a move.
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var selfPlayer = new PlayerID(System.Guid.NewGuid());
            var enemyPlayer = new PlayerID(System.Guid.NewGuid());

            // A tight, in-cohesion mixed-base row: big model then four small ones (0.1" base-to-base gaps).
            var radii = new[] { 1.5f, 0.5f, 0.5f, 0.5f, 0.5f };
            var bindings = new List<DataBinding<ModelData>>();
            float x = 0f;
            float prevR = 0f;
            for (int i = 0; i < radii.Length; i++)
            {
                if (i > 0) x += prevR + 0.1f + radii[i];
                var m = new ModelData(radii[i], new List<Weapon>(), new Position(x, 20f), store);
                bindings.Add(store.GetDataBinding<ModelData>(store.Create(m)));
                prevR = radii[i];
            }
            // Kill the two small models next to the big one, leaving the big model >1" from every survivor.
            foreach (int dead in new[] { 1, 2 })
                bindings[dead].GetValue().DealWounds(bindings[dead].GetValue().TotalWounds);

            var moverUnit = new UnitData(selfPlayer, "MixedSurvivors", 4, 4, bindings);
            var moverUnitBinding = store.GetDataBinding<UnitData>(store.Create(moverUnit));
            store.Create(new ArmyData(selfPlayer, new List<DataBinding<UnitData>> { moverUnitBinding }));

            // Enemy far to the east so the AI advances toward it.
            var enemyPos = new Position(40f, 20f);
            var enemy = new ModelData(0.75f, new List<Weapon>(), enemyPos, store);
            var enemyBinding = store.GetDataBinding<ModelData>(store.Create(enemy));
            var enemyUnit = new UnitData(enemyPlayer, "Enemies", 4, 4,
                new List<DataBinding<ModelData>> { enemyBinding });
            var enemyUnitBinding = store.GetDataBinding<UnitData>(store.Create(enemyUnit));
            store.Create(new ArmyData(enemyPlayer, new List<DataBinding<UnitData>> { enemyUnitBinding }));

            var tableState = new TableState(store);
            var resolver = new AiDefineMovementResolver(tableState, selfPlayer);
            var request = new DefineMovementPathRequest(selfPlayer, "Move", moverUnitBinding,
                maxAdvanceDistance: 6f, maxRushDistance: 12f, maxDistanceInches: 12f);

            List<ModelMoveEntry> result = Unwrap(await resolver.Resolve(request));

            var footprints = new List<EnemyModelFootprint> { new EnemyModelFootprint(enemyPos, 0.75f, 0) };
            bool valid = MovementUtilities.ValidatePaths(result, request.MaxRushDistance,
                request.MaxDistanceInches, footprints, new List<ITerrain>(), out var errors);
            Assert.That(valid, Is.True,
                "AI must never emit a move the engine rejects (mixed-base casualty unit): "
                + string.Join(", ", errors.Select(e => e.ToString())));
        }

        [Test]
        public async Task Resolve_MeleeUnitInReach_ChargesIntoBaseContact()
        {
            // #089: a melee unit that can reach should close to base contact, not stall ~1" out.
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var selfPlayer = new PlayerID(System.Guid.NewGuid());
            var enemyPlayer = new PlayerID(System.Guid.NewGuid());

            // Weaponless mover → classified Melee.
            var mover = new ModelData(0.75f, new List<Weapon>(), new Position(0f, 0f), store);
            var moverBinding = store.GetDataBinding<ModelData>(store.Create(mover));
            var moverUnit = new UnitData(selfPlayer, "Melee", 4, 4,
                new List<DataBinding<ModelData>> { moverBinding });
            var moverUnitBinding = store.GetDataBinding<UnitData>(store.Create(moverUnit));

            // Enemy 8" away — comfortably inside the 12" charge.
            var enemyPos = new Position(8f, 0f);
            var enemy = new ModelData(0.75f, new List<Weapon>(), enemyPos, store);
            var enemyBinding = store.GetDataBinding<ModelData>(store.Create(enemy));
            store.Create(new UnitData(enemyPlayer, "Enemies", 4, 4,
                new List<DataBinding<ModelData>> { enemyBinding }));

            var tableState = new TableState(store);
            var resolver = new AiDefineMovementResolver(tableState, selfPlayer);
            var request = new DefineMovementPathRequest(selfPlayer, "Move", moverUnitBinding,
                maxAdvanceDistance: 6f, maxRushDistance: 12f, maxDistanceInches: 12f);

            List<ModelMoveEntry> result = Unwrap(await resolver.Resolve(request));

            var footprints = new List<EnemyModelFootprint> { new EnemyModelFootprint(enemyPos, 0.75f, 0) };
            bool valid = MovementUtilities.ValidatePaths(result, request.MaxRushDistance,
                request.MaxDistanceInches, footprints, new List<ITerrain>(), out var errors);
            Assert.That(valid, Is.True, "Charge must be engine-valid: " + string.Join(", ", errors.Select(e => e.ToString())));

            float gap = MinGap(result, enemyPos);
            Assert.That(gap, Is.LessThanOrEqualTo(0.25f), "melee unit should end in base contact, not stall short");
            Assert.That(gap, Is.GreaterThanOrEqualTo(-0.11f), "but must not overlap the enemy base");
        }

        [Test]
        public async Task Resolve_ShootingUnitInReach_StopsAtStandoffWithoutOverlapping()
        {
            // #089: a shooting unit advances toward the enemy but holds at the 1" standoff, not on top of it.
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var selfPlayer = new PlayerID(System.Guid.NewGuid());
            var enemyPlayer = new PlayerID(System.Guid.NewGuid());

            // A ranged-only weapon → classified Shooting.
            var rifle = new Weapon("Rifle", rangeInches: 24f, attacks: 1, armorPenetration: 0);
            var mover = new ModelData(0.75f, new List<Weapon> { rifle }, new Position(0f, 0f), store);
            var moverBinding = store.GetDataBinding<ModelData>(store.Create(mover));
            var moverUnit = new UnitData(selfPlayer, "Shooters", 4, 4,
                new List<DataBinding<ModelData>> { moverBinding });
            var moverUnitBinding = store.GetDataBinding<UnitData>(store.Create(moverUnit));

            var enemyPos = new Position(8f, 0f);
            var enemy = new ModelData(0.75f, new List<Weapon>(), enemyPos, store);
            var enemyBinding = store.GetDataBinding<ModelData>(store.Create(enemy));
            store.Create(new UnitData(enemyPlayer, "Enemies", 4, 4,
                new List<DataBinding<ModelData>> { enemyBinding }));

            var tableState = new TableState(store);
            var resolver = new AiDefineMovementResolver(tableState, selfPlayer);
            var request = new DefineMovementPathRequest(selfPlayer, "Move", moverUnitBinding,
                maxAdvanceDistance: 6f, maxRushDistance: 12f, maxDistanceInches: 12f);

            List<ModelMoveEntry> result = Unwrap(await resolver.Resolve(request));

            var footprints = new List<EnemyModelFootprint> { new EnemyModelFootprint(enemyPos, 0.75f, 0) };
            bool valid = MovementUtilities.ValidatePaths(result, request.MaxRushDistance,
                request.MaxDistanceInches, footprints, new List<ITerrain>(), out var errors);
            Assert.That(valid, Is.True, "Advance must be engine-valid: " + string.Join(", ", errors.Select(e => e.ToString())));

            float gap = MinGap(result, enemyPos);
            Assert.That(gap, Is.GreaterThanOrEqualTo(GameWideConstants.ENEMY_STANDOFF_DISTANCE_INCHES - 0.01f),
                "shooter must hold at the standoff line, not overlap");
            bool advanced = result.Any(e => e.Positions.Count > 0 && e.Positions[e.Positions.Count - 1].x > 0.5f);
            Assert.That(advanced, Is.True, "shooter should still close toward the enemy");
        }

        [Test]
        public async Task Resolve_MeleeUnitAlreadyInBaseContact_ProducesValidMove()
        {
            // A unit touching an enemy must not deadlock the resolver into emitting a move the (throwing)
            // DefinePathStage rejects — it should hold / reform validly.
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var selfPlayer = new PlayerID(System.Guid.NewGuid());
            var enemyPlayer = new PlayerID(System.Guid.NewGuid());

            var enemyPos = new Position(5f, 0f);
            // Mover sits in base contact (1.5" centre-to-centre) with the enemy.
            var mover = new ModelData(0.75f, new List<Weapon>(), new Position(3.5f, 0f), store);
            var moverBinding = store.GetDataBinding<ModelData>(store.Create(mover));
            var moverUnit = new UnitData(selfPlayer, "Melee", 4, 4,
                new List<DataBinding<ModelData>> { moverBinding });
            var moverUnitBinding = store.GetDataBinding<UnitData>(store.Create(moverUnit));

            var enemy = new ModelData(0.75f, new List<Weapon>(), enemyPos, store);
            var enemyBinding = store.GetDataBinding<ModelData>(store.Create(enemy));
            store.Create(new UnitData(enemyPlayer, "Enemies", 4, 4,
                new List<DataBinding<ModelData>> { enemyBinding }));

            var tableState = new TableState(store);
            var resolver = new AiDefineMovementResolver(tableState, selfPlayer);
            var request = new DefineMovementPathRequest(selfPlayer, "Move", moverUnitBinding,
                maxAdvanceDistance: 6f, maxRushDistance: 12f, maxDistanceInches: 12f);

            List<ModelMoveEntry> result = Unwrap(await resolver.Resolve(request));

            var footprints = new List<EnemyModelFootprint> { new EnemyModelFootprint(enemyPos, 0.75f, 0) };
            bool valid = MovementUtilities.ValidatePaths(result, request.MaxRushDistance,
                request.MaxDistanceInches, footprints, new List<ITerrain>(), out var errors);
            Assert.That(valid, Is.True,
                "A unit already in contact must still get a valid move: " + string.Join(", ", errors.Select(e => e.ToString())));
        }

        // #208: when NO legal path exists - here two models start 6" apart (a unit spread by post-melee
        // intermingling) and the move budget is too small to either advance meaningfully or re-pack them
        // into cohesion - the AI must NOT submit the cohesion-breaking hold that faults the executor. On a
        // CANCELLABLE request (an optional post-combat "may move") it declines with Cancelled instead.
        [Test]
        public async Task Resolve_NoLegalPath_CancellableRequest_DeclinesWithCancelled()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var selfPlayer = new PlayerID(System.Guid.NewGuid());
            var enemyPlayer = new PlayerID(System.Guid.NewGuid());

            // Two mover models 6" apart - already breaking cohesion, as a post-melee spread does.
            var a = store.GetDataBinding<ModelData>(store.Create(new ModelData(0.5f, new List<Weapon>(), new Position(0f, 0f), store)));
            var b = store.GetDataBinding<ModelData>(store.Create(new ModelData(0.5f, new List<Weapon>(), new Position(0f, 6f), store)));
            var moverUnit = new UnitData(selfPlayer, "Spread", 4, 4, new List<DataBinding<ModelData>> { a, b });
            var moverUnitBinding = store.GetDataBinding<UnitData>(store.Create(moverUnit));

            // An enemy exists (so the resolver takes the advance path, not the no-enemy early-out) but is
            // far away and irrelevant to reachability.
            var enemy = store.GetDataBinding<ModelData>(store.Create(new ModelData(0.5f, new List<Weapon>(), new Position(0f, 100f), store)));
            store.Create(new UnitData(enemyPlayer, "Enemies", 4, 4, new List<DataBinding<ModelData>> { enemy }));

            var tableState = new TableState(store);
            var resolver = new AiDefineMovementResolver(tableState, selfPlayer);

            // A tiny budget: the models cannot advance to a valid spot NOR re-pack the 6" apart into
            // cohesion within 0.3", so the ladder bottoms out at the cohesion-breaking hold.
            var request = new DefineMovementPathRequest(selfPlayer, "Triggered Move", moverUnitBinding,
                maxAdvanceDistance: 0.3f, maxRushDistance: 0.3f, maxDistanceInches: 0.3f, allowCancel: true);

            CancellableResult<List<ModelMoveEntry>> result = await resolver.Resolve(request);

            Assert.That(result, Is.InstanceOf<Cancelled<List<ModelMoveEntry>>>(),
                "with no legal path and a cancellable request the AI declines rather than submitting an invalid move");
        }

        // The forced-move twin of the above: an identical stuck unit on a NON-cancellable request has no
        // decline channel, so the AI still returns its least-bad path (the pre-#208 behavior). It stays a
        // path, not a Cancelled - MoveUnit is then responsible for faulting a truly-forced invalid move.
        [Test]
        public async Task Resolve_NoLegalPath_NonCancellableRequest_StillReturnsAPath()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var selfPlayer = new PlayerID(System.Guid.NewGuid());
            var enemyPlayer = new PlayerID(System.Guid.NewGuid());

            var a = store.GetDataBinding<ModelData>(store.Create(new ModelData(0.5f, new List<Weapon>(), new Position(0f, 0f), store)));
            var b = store.GetDataBinding<ModelData>(store.Create(new ModelData(0.5f, new List<Weapon>(), new Position(0f, 6f), store)));
            var moverUnit = new UnitData(selfPlayer, "Spread", 4, 4, new List<DataBinding<ModelData>> { a, b });
            var moverUnitBinding = store.GetDataBinding<UnitData>(store.Create(moverUnit));

            var enemy = store.GetDataBinding<ModelData>(store.Create(new ModelData(0.5f, new List<Weapon>(), new Position(0f, 100f), store)));
            store.Create(new UnitData(enemyPlayer, "Enemies", 4, 4, new List<DataBinding<ModelData>> { enemy }));

            var tableState = new TableState(store);
            var resolver = new AiDefineMovementResolver(tableState, selfPlayer);

            var request = new DefineMovementPathRequest(selfPlayer, "Triggered Move", moverUnitBinding,
                maxAdvanceDistance: 0.3f, maxRushDistance: 0.3f, maxDistanceInches: 0.3f, allowCancel: false);

            CancellableResult<List<ModelMoveEntry>> result = await resolver.Resolve(request);

            Assert.That(result, Is.InstanceOf<Selected<List<ModelMoveEntry>>>(),
                "a non-cancellable request has no decline channel, so the AI still supplies a path");
        }

        // Smallest base-to-base gap between any moved model's end position and the enemy (both radius 0.75").
        private static float MinGap(List<ModelMoveEntry> moves, Position enemy)
        {
            float min = float.PositiveInfinity;
            foreach (var m in moves)
                if (m.Positions.Count > 0)
                {
                    float g = Position.GetDistance2D(m.Positions[m.Positions.Count - 1], enemy) - 0.75f - 0.75f;
                    if (g < min) min = g;
                }
            return min;
        }

        // The AI answers a stand-still as a zero-length path, not a Cancelled - EXCEPT when the request is
        // cancellable (an optional triggered move) AND no legal path exists, where it declines (#208). None
        // of the requests these tests build set AllowCancel, so here the AI must always supply a path.
        private static List<ModelMoveEntry> Unwrap(CancellableResult<List<ModelMoveEntry>> result)
        {
            Assert.That(result, Is.InstanceOf<Selected<List<ModelMoveEntry>>>(),
                "the AI must always supply a path.");
            return ((Selected<List<ModelMoveEntry>>)result).Value;
        }

    }
}
