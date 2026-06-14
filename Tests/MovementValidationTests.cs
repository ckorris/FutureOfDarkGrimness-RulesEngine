using FDG.Data;
using FDG.Stages;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    [TestFixture]
    public class MovementValidationTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
        }

        [Test]
        public void ValidatePaths_NoTerrain_DoesNotProduceTerrainError()
        {
            DataBinding<ModelData> model = MakeModel(new Position(0, 0));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(5, 0) });

            bool ok = MovementUtilities.ValidatePaths(
                new List<ModelMoveEntry> { move },
                maxDistanceInches: 12f,
                terrain: null,
                out List<ReasonForInvalidMove> errors);

            Assert.That(ok, Is.True);
            Assert.That(errors, Is.Empty);
        }

        [Test]
        public void ValidatePaths_PathCrossesImpassible_Rejected()
        {
            //Path goes straight through a 5"-wide impassable square at x=4..6, z=-2..2.
            DataBinding<ModelData> model = MakeModel(new Position(0, 0));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(10, 0) });

            List<ITerrain> terrain = new List<ITerrain>
            {
                new TerrainData(ETerrainType.Impassible, new RectangularZone(4, 6, -2, 2))
            };

            bool ok = MovementUtilities.ValidatePaths(
                new List<ModelMoveEntry> { move },
                maxDistanceInches: 12f,
                terrain: terrain,
                out List<ReasonForInvalidMove> errors);

            Assert.That(ok, Is.False);
            Assert.That(errors.Any(e => e.ErrorReasonType == EErrorReasonType.MovingThroughImpassibleTerrain), Is.True);
        }

        [Test]
        public void ValidatePaths_PathSkirtsImpassible_Accepted()
        {
            //Path goes around the impassable piece (above it on Z axis).
            DataBinding<ModelData> model = MakeModel(new Position(0, 5));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(10, 5) });

            List<ITerrain> terrain = new List<ITerrain>
            {
                new TerrainData(ETerrainType.Impassible, new RectangularZone(4, 6, -2, 2))
            };

            bool ok = MovementUtilities.ValidatePaths(
                new List<ModelMoveEntry> { move },
                maxDistanceInches: 12f,
                terrain: terrain,
                out List<ReasonForInvalidMove> errors);

            Assert.That(ok, Is.True);
            Assert.That(errors.Where(e => e.ErrorReasonType == EErrorReasonType.MovingThroughImpassibleTerrain),
                Is.Empty);
        }

        [Test]
        public void ValidatePaths_PathThroughCoverOrDifficult_NotRejected()
        {
            //Same crossing path, but terrain is Cover|Difficult — no Impassible flag.
            DataBinding<ModelData> model = MakeModel(new Position(0, 0));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(10, 0) });

            List<ITerrain> terrain = new List<ITerrain>
            {
                new TerrainData(ETerrainType.Cover | ETerrainType.Difficult, new RectangularZone(4, 6, -2, 2))
            };

            bool ok = MovementUtilities.ValidatePaths(
                new List<ModelMoveEntry> { move },
                maxDistanceInches: 12f,
                terrain: terrain,
                out List<ReasonForInvalidMove> errors);

            Assert.That(errors.Where(e => e.ErrorReasonType == EErrorReasonType.MovingThroughImpassibleTerrain),
                Is.Empty,
                "Cover/Difficult must not produce an impassable error.");
            //ok may or may not be true depending on other validators; we only assert the terrain check.
        }

        [Test]
        public void ValidatePaths_MultiSegmentPath_OneSegmentCrosses_Rejected()
        {
            //Three-step path: start→(0,5) is fine, (0,5)→(10,5) is fine (skirts above terrain),
            //(10,5)→(10,-5) is fine, (10,-5)→(0,-5) is fine, then (0,-5)→(5,5) crosses the rect.
            DataBinding<ModelData> model = MakeModel(new Position(0, 0));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position>
            {
                new Position(0, 5),
                new Position(10, 5),
                new Position(0, -5),
                new Position(5, 5),
            });

            List<ITerrain> terrain = new List<ITerrain>
            {
                new TerrainData(ETerrainType.Impassible, new RectangularZone(4, 6, -2, 2))
            };

            bool ok = MovementUtilities.ValidatePaths(
                new List<ModelMoveEntry> { move },
                maxDistanceInches: 100f, //high so distance doesn't dominate
                terrain: terrain,
                out List<ReasonForInvalidMove> errors);

            Assert.That(ok, Is.False);
            Assert.That(errors.Any(e => e.ErrorReasonType == EErrorReasonType.MovingThroughImpassibleTerrain),
                Is.True);
        }

        [Test]
        public void ValidatePaths_CircleImpassible_RejectsCrossingPath()
        {
            DataBinding<ModelData> model = MakeModel(new Position(0, 5));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(10, 5) });

            //Circle centered at (5,5) radius 2 — path passes through center.
            List<ITerrain> terrain = new List<ITerrain>
            {
                new TerrainData(ETerrainType.Impassible, new CircularZone(new Float2(5, 5), 2))
            };

            bool ok = MovementUtilities.ValidatePaths(
                new List<ModelMoveEntry> { move },
                maxDistanceInches: 12f,
                terrain: terrain,
                out List<ReasonForInvalidMove> errors);

            Assert.That(ok, Is.False);
            Assert.That(errors.Any(e => e.ErrorReasonType == EErrorReasonType.MovingThroughImpassibleTerrain),
                Is.True);
        }

        [Test]
        public void ValidatePaths_DifficultTerrain_WithinCap_Accepted()
        {
            // Move of exactly 6" through difficult terrain — should be fine.
            DataBinding<ModelData> model = MakeModel(new Position(0, 0));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(6, 0) });

            List<ITerrain> terrain = new List<ITerrain>
            {
                new TerrainData(ETerrainType.Difficult, new RectangularZone(2, 4, -2, 2))
            };

            bool ok = MovementUtilities.ValidatePaths(
                new List<ModelMoveEntry> { move },
                maxDistanceInches: 12f,
                terrain: terrain,
                out List<ReasonForInvalidMove> errors);

            Assert.That(errors.Any(e => e.ErrorReasonType == EErrorReasonType.ExceededDifficultTerrainMoveLimit),
                Is.False);
        }

        [Test]
        public void ValidatePaths_DifficultTerrain_ExceedsCap_Rejected()
        {
            // Move of 8" through difficult terrain — exceeds the 6" cap.
            DataBinding<ModelData> model = MakeModel(new Position(0, 0));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(8, 0) });

            List<ITerrain> terrain = new List<ITerrain>
            {
                new TerrainData(ETerrainType.Difficult, new RectangularZone(2, 4, -2, 2))
            };

            bool ok = MovementUtilities.ValidatePaths(
                new List<ModelMoveEntry> { move },
                maxDistanceInches: 12f,
                terrain: terrain,
                out List<ReasonForInvalidMove> errors);

            Assert.That(ok, Is.False);
            Assert.That(errors.Any(e => e.ErrorReasonType == EErrorReasonType.ExceededDifficultTerrainMoveLimit),
                Is.True);
        }

        [Test]
        public void ValidatePaths_DifficultTerrain_PathMisses_NotRejected()
        {
            // Path skirts above the difficult terrain — no cap applies.
            DataBinding<ModelData> model = MakeModel(new Position(0, 5));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(8, 5) });

            List<ITerrain> terrain = new List<ITerrain>
            {
                new TerrainData(ETerrainType.Difficult, new RectangularZone(2, 4, -2, 2))
            };

            bool ok = MovementUtilities.ValidatePaths(
                new List<ModelMoveEntry> { move },
                maxDistanceInches: 12f,
                terrain: terrain,
                out List<ReasonForInvalidMove> errors);

            Assert.That(errors.Any(e => e.ErrorReasonType == EErrorReasonType.ExceededDifficultTerrainMoveLimit),
                Is.False);
        }

        [Test]
        public void ValidatePaths_ImpassibleWithoutDifficult_NoDifficultError()
        {
            // Impassable-only terrain should not trigger the difficult cap, only the impassable block.
            DataBinding<ModelData> model = MakeModel(new Position(0, 0));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(8, 0) });

            List<ITerrain> terrain = new List<ITerrain>
            {
                new TerrainData(ETerrainType.Impassible, new RectangularZone(2, 4, -2, 2))
            };

            MovementUtilities.ValidatePaths(
                new List<ModelMoveEntry> { move },
                maxDistanceInches: 12f,
                terrain: terrain,
                out List<ReasonForInvalidMove> errors);

            Assert.That(errors.Any(e => e.ErrorReasonType == EErrorReasonType.ExceededDifficultTerrainMoveLimit),
                Is.False);
        }

        [Test]
        public void DoesPathCrossDangerousTerrain_PathCrosses_ReturnsTrue()
        {
            DataBinding<ModelData> model = MakeModel(new Position(0, 0));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(8, 0) });

            List<ITerrain> terrain = new List<ITerrain>
            {
                new TerrainData(ETerrainType.Dangerous, new RectangularZone(3, 5, -2, 2))
            };

            Assert.That(MovementUtilities.DoesPathCrossDangerousTerrain(move, terrain), Is.True);
        }

        [Test]
        public void DoesPathCrossDangerousTerrain_PathMisses_ReturnsFalse()
        {
            DataBinding<ModelData> model = MakeModel(new Position(0, 5));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(8, 5) });

            List<ITerrain> terrain = new List<ITerrain>
            {
                new TerrainData(ETerrainType.Dangerous, new RectangularZone(3, 5, -2, 2))
            };

            Assert.That(MovementUtilities.DoesPathCrossDangerousTerrain(move, terrain), Is.False);
        }

        [Test]
        public void DoesPathCrossDangerousTerrain_NonDangerousTerrain_ReturnsFalse()
        {
            // Difficult-only terrain — must not trigger dangerous check.
            DataBinding<ModelData> model = MakeModel(new Position(0, 0));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(8, 0) });

            List<ITerrain> terrain = new List<ITerrain>
            {
                new TerrainData(ETerrainType.Difficult, new RectangularZone(3, 5, -2, 2))
            };

            Assert.That(MovementUtilities.DoesPathCrossDangerousTerrain(move, terrain), Is.False);
        }

        [Test]
        public void DoesPathCrossDangerousTerrain_NoPositions_ReturnsFalse()
        {
            // Model that didn't move — positions list is empty.
            DataBinding<ModelData> model = MakeModel(new Position(0, 0));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position>());

            List<ITerrain> terrain = new List<ITerrain>
            {
                new TerrainData(ETerrainType.Dangerous, new RectangularZone(3, 5, -2, 2))
            };

            Assert.That(MovementUtilities.DoesPathCrossDangerousTerrain(move, terrain), Is.False);
        }

        [Test]
        public void ValidatePaths_CenterSkirtsButBaseOverlapsImpassible_Rejected()
        {
            //Center runs along z=2.5, just above the rect's top edge (z=2), so the zero-width
            //center line misses — but the 0.75" base overlaps. Must be rejected (#050).
            DataBinding<ModelData> model = MakeModel(new Position(0, 2.5f));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(10, 2.5f) });

            List<ITerrain> terrain = new List<ITerrain>
            {
                new TerrainData(ETerrainType.Impassible, new RectangularZone(4, 6, -2, 2))
            };

            bool ok = MovementUtilities.ValidatePaths(
                new List<ModelMoveEntry> { move },
                maxDistanceInches: 12f,
                terrain: terrain,
                out List<ReasonForInvalidMove> errors);

            Assert.That(ok, Is.False);
            Assert.That(errors.Any(e => e.ErrorReasonType == EErrorReasonType.MovingThroughImpassibleTerrain), Is.True);
        }

        [Test]
        public void ValidatePaths_BaseClearsImpassible_Accepted()
        {
            //Center along z=3 — 1.0" clearance above the rect's top edge (z=2), more than the
            //0.75" base radius. The base does not overlap, so the move stays legal.
            DataBinding<ModelData> model = MakeModel(new Position(0, 3));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(10, 3) });

            List<ITerrain> terrain = new List<ITerrain>
            {
                new TerrainData(ETerrainType.Impassible, new RectangularZone(4, 6, -2, 2))
            };

            bool ok = MovementUtilities.ValidatePaths(
                new List<ModelMoveEntry> { move },
                maxDistanceInches: 12f,
                terrain: terrain,
                out List<ReasonForInvalidMove> errors);

            Assert.That(ok, Is.True);
            Assert.That(errors.Where(e => e.ErrorReasonType == EErrorReasonType.MovingThroughImpassibleTerrain),
                Is.Empty);
        }

        [Test]
        public void ValidatePaths_CircleImpassible_BaseOverlapWithoutCenterCrossing_Rejected()
        {
            //Circle radius 2 centered at (5,5); center line at z=2.75 is 2.25" away — outside the
            //bare radius (2), but the 0.75" base brings effective reach to 2.75" so it touches.
            DataBinding<ModelData> model = MakeModel(new Position(0, 2.75f));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(10, 2.75f) });

            List<ITerrain> terrain = new List<ITerrain>
            {
                new TerrainData(ETerrainType.Impassible, new CircularZone(new Float2(5, 5), 2))
            };

            bool ok = MovementUtilities.ValidatePaths(
                new List<ModelMoveEntry> { move },
                maxDistanceInches: 12f,
                terrain: terrain,
                out List<ReasonForInvalidMove> errors);

            Assert.That(ok, Is.False);
            Assert.That(errors.Any(e => e.ErrorReasonType == EErrorReasonType.MovingThroughImpassibleTerrain), Is.True);
        }

        [Test]
        public void ValidatePaths_DifficultTerrain_BaseOverlapExceedsCap_Rejected()
        {
            //8" move whose center skirts above difficult terrain (z=2.5 vs top z=2) but whose base
            //overlaps it; total move exceeds the 6" cap, so it must now be rejected (#050).
            DataBinding<ModelData> model = MakeModel(new Position(0, 2.5f));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(8, 2.5f) });

            List<ITerrain> terrain = new List<ITerrain>
            {
                new TerrainData(ETerrainType.Difficult, new RectangularZone(2, 4, -2, 2))
            };

            bool ok = MovementUtilities.ValidatePaths(
                new List<ModelMoveEntry> { move },
                maxDistanceInches: 12f,
                terrain: terrain,
                out List<ReasonForInvalidMove> errors);

            Assert.That(ok, Is.False);
            Assert.That(errors.Any(e => e.ErrorReasonType == EErrorReasonType.ExceededDifficultTerrainMoveLimit), Is.True);
        }

        [Test]
        public void DoesPathCrossDangerousTerrain_CenterSkirtsButBaseOverlaps_ReturnsTrue()
        {
            //Center along z=2.5 misses the rect (top z=2); the 0.75" base overlaps it (#050).
            DataBinding<ModelData> model = MakeModel(new Position(0, 2.5f));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(8, 2.5f) });

            List<ITerrain> terrain = new List<ITerrain>
            {
                new TerrainData(ETerrainType.Dangerous, new RectangularZone(3, 5, -2, 2))
            };

            Assert.That(MovementUtilities.DoesPathCrossDangerousTerrain(move, terrain), Is.True);
        }

        [Test]
        public void ValidatePaths_DeadModel_DoesNotAnchorCohesion()
        {
            // A casualty's body must not count toward cohesion: a dead model 1.5" base-to-base from the
            // lone survivor used to fail the survivor for being "too far from the closest model" and crash
            // DefinePathStage. With the dead model ignored, the single living model has nothing to compare to.
            DataBinding<ModelData> living = MakeModel(new Position(0, 0));
            DataBinding<ModelData> dead = MakeModel(new Position(3, 0)); // center 3" → 1.5" base-to-base
            dead.GetValue().DealWounds(dead.GetValue().TotalWounds);

            bool ok = MovementUtilities.ValidatePaths(
                StayInPlace(living, dead),
                maxDistanceInches: 12f,
                terrain: null,
                out List<ReasonForInvalidMove> errors);

            Assert.That(errors.Any(e => e.ErrorReasonType == EErrorReasonType.TooFarFromAnyUnitModel), Is.False,
                "a dead model must not break the survivor's cohesion.");
            Assert.That(ok, Is.True);
        }

        [Test]
        public void ValidatePaths_TwoLivingModelsTooFarApart_RejectedForCohesion()
        {
            // Control for the dead-model case: two LIVING models 1.5" base-to-base apart still fail cohesion.
            DataBinding<ModelData> a = MakeModel(new Position(0, 0));
            DataBinding<ModelData> b = MakeModel(new Position(3, 0)); // 1.5" base-to-base

            bool ok = MovementUtilities.ValidatePaths(
                StayInPlace(a, b),
                maxDistanceInches: 12f,
                terrain: null,
                out List<ReasonForInvalidMove> errors);

            Assert.That(ok, Is.False);
            Assert.That(errors.Any(e => e.ErrorReasonType == EErrorReasonType.TooFarFromAnyUnitModel), Is.True,
                "two living models beyond 1\" must still break cohesion.");
        }

        [Test]
        public void ValidatePaths_LivingModelsJustWithinFloatSlack_Accepted()
        {
            // Base-to-base 1.0005" — over the 1" limit by less than the epsilon, so float rounding at the
            // exact boundary doesn't wrongly reject the move (0.75 + 0.75 radii + 1.0005 gap = 2.5005 centre).
            DataBinding<ModelData> a = MakeModel(new Position(0, 0));
            DataBinding<ModelData> b = MakeModel(new Position(2.5005f, 0));

            bool ok = MovementUtilities.ValidatePaths(
                StayInPlace(a, b),
                maxDistanceInches: 12f,
                terrain: null,
                out List<ReasonForInvalidMove> errors);

            Assert.That(errors.Any(e => e.ErrorReasonType == EErrorReasonType.TooFarFromAnyUnitModel), Is.False,
                "a hair over the limit is absorbed by the cohesion epsilon.");
            Assert.That(ok, Is.True);
        }

        [Test]
        public void ValidatePaths_LivingModelsClearlyBeyondSlack_Rejected()
        {
            // Base-to-base 1.01" — comfortably past the epsilon, so cohesion still fails.
            DataBinding<ModelData> a = MakeModel(new Position(0, 0));
            DataBinding<ModelData> b = MakeModel(new Position(2.51f, 0));

            bool ok = MovementUtilities.ValidatePaths(
                StayInPlace(a, b),
                maxDistanceInches: 12f,
                terrain: null,
                out List<ReasonForInvalidMove> errors);

            Assert.That(ok, Is.False);
            Assert.That(errors.Any(e => e.ErrorReasonType == EErrorReasonType.TooFarFromAnyUnitModel), Is.True);
        }

        // Move entries that leave every model at its current position — isolates the cohesion check.
        private static List<ModelMoveEntry> StayInPlace(params DataBinding<ModelData>[] models)
        {
            return models
                .Select(m => new ModelMoveEntry(m, new List<Position> { m.GetValue().PositionBinding.GetValue() }))
                .ToList();
        }

        private DataBinding<ModelData> MakeModel(Position initialPosition)
        {
            ModelData modelData = new ModelData(
                baseRadiusInches: 0.75f,
                weapons: new List<Weapon>(),
                initialPosition: initialPosition,
                gameDataStore: _store);
            DataReference reference = _store.Create(modelData);
            return _store.GetDataBinding<ModelData>(reference);
        }
    }
}
