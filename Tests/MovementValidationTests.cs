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
                maxChargeDistance: 12f,
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
                maxChargeDistance: 12f,
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
                maxChargeDistance: 12f,
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
                maxChargeDistance: 12f,
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
                maxChargeDistance: 100f, //high so distance doesn't dominate
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
                maxChargeDistance: 12f,
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
                maxChargeDistance: 12f,
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
                maxChargeDistance: 12f,
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
                maxChargeDistance: 12f,
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
                maxChargeDistance: 12f,
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

        private DataBinding<ModelData> MakeModel(Position initialPosition)
        {
            ModelData modelData = new ModelData(
                baseRadiusInches: 0.75f,
                weapons: new List<Weapon>(),
                specialRules: new List<SpecialRule>(),
                initialPosition: initialPosition,
                gameDataStore: _store);
            DataReference reference = _store.Create(modelData);
            return _store.GetDataBinding<ModelData>(reference);
        }
    }
}
