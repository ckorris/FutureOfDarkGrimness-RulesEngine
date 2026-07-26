using System;
using System.Collections.Generic;
using FDG.Data;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #282: PathTemplate captures the manual facing offset per waypoint at AddStep, so rotating AFTER
    // placing waypoints only shapes the next placement - it never re-orients the committed path. This
    // pins the capture, the undo sync, and the result derivation the GUI movement resolver rides.
    [TestFixture]
    public class PathTemplateFacingOffsetTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        [Test]
        public void AddStep_CapturesOffsetPerWaypoint_LateRotationDoesNotRotateEarlierWaypoints()
        {
            DataBinding<ModelData> model = MakeModel();
            DataBinding<UnitData> unit = MakeUnit(model);
            var pt = new PathTemplate(unit, 6f, 12f);
            IModel m = model.GetValue();

            pt.AddStep(m, new Position(0f, 3f));                            // placed unrotated, travel +Z
            pt.AddStep(m, new Position(3f, 3f), MathF.PI / 2f);             // placed after a +90 deg turn, travel +X

            List<ModelMoveEntry> results = pt.GetResultsAsList(EPathFacingDerivation.TravelDirection);
            ModelMoveEntry entry = results.Find(e => e.Model.GetValue() == m);

            Assert.That(entry.Facings, Is.Not.Null);
            Assert.That(entry.Facings![0].X, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(entry.Facings![0].Y, Is.EqualTo(1f).Within(1e-4f), "first waypoint keeps its placement-time facing");
            Assert.That(entry.Facings![1].X, Is.EqualTo(0f).Within(1e-4f), "travel +X rotated +90 deg -> +Z");
            Assert.That(entry.Facings![1].Y, Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void RemoveLastStep_DropsTheStoredOffsetToo()
        {
            DataBinding<ModelData> model = MakeModel();
            DataBinding<UnitData> unit = MakeUnit(model);
            var pt = new PathTemplate(unit, 6f, 12f);
            IModel m = model.GetValue();

            pt.AddStep(m, new Position(2f, 0f));
            pt.AddStep(m, new Position(2f, 2f), MathF.PI / 2f);
            pt.RemoveLastStep(m);
            pt.AddStep(m, new Position(2f, 2f));                            // re-placed WITHOUT the rotation

            IReadOnlyList<float> offsets = pt.GetModelFacingOffsets(m);
            Assert.That(offsets, Is.EqualTo(new[] { 0f, 0f }), "the undone waypoint's offset went with it");

            List<ModelMoveEntry> results = pt.GetResultsAsList(EPathFacingDerivation.TravelDirection);
            ModelMoveEntry entry = results.Find(e => e.Model.GetValue() == m);
            Assert.That(entry.Facings![1].X, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(entry.Facings![1].Y, Is.EqualTo(1f).Within(1e-4f), "travel +Z, no leftover rotation");
        }

        [Test]
        public void GetResultsAsList_RotateInPlace_KeepsBaseFacingRotatedByEachStoredOffset()
        {
            DataBinding<ModelData> model = MakeModel();
            DataBinding<UnitData> unit = MakeUnit(model);
            var pt = new PathTemplate(unit, 6f, 12f);
            IModel m = model.GetValue();
            m.SetFacing(new Float2(0f, 1f));

            // #283 (consolidation): travel goes +Z then +X, but the facing must ignore travel entirely -
            // it is the model's own facing rotated by the offset each step was committed with.
            pt.AddStep(m, new Position(0f, 1f));
            pt.AddStep(m, new Position(1f, 1f), MathF.PI / 2f);

            List<ModelMoveEntry> results = pt.GetResultsAsList(EPathFacingDerivation.RotateInPlace);
            ModelMoveEntry entry = results.Find(e => e.Model.GetValue() == m);

            Assert.That(entry.Facings, Is.Not.Null);
            Assert.That(entry.Facings![0].X, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(entry.Facings![0].Y, Is.EqualTo(1f).Within(1e-4f), "unrotated step keeps the model's facing");
            Assert.That(entry.Facings![1].X, Is.EqualTo(-1f).Within(1e-4f), "+Z facing rotated +90 deg -> -X, not the +X travel");
            Assert.That(entry.Facings![1].Y, Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void ClearModelSteps_And_ClearAllSteps_KeepOffsetsInSync()
        {
            DataBinding<ModelData> model = MakeModel();
            DataBinding<UnitData> unit = MakeUnit(model);
            var pt = new PathTemplate(unit, 6f, 12f);
            IModel m = model.GetValue();

            pt.AddStep(m, new Position(0f, 3f), 1f);
            pt.ClearModelSteps(m);
            Assert.That(pt.GetModelFacingOffsets(m), Is.Empty);

            pt.AddStep(m, new Position(0f, 3f), 1f);
            pt.ClearAllSteps();
            Assert.That(pt.GetModelFacingOffsets(m), Is.Empty);

            pt.AddStep(m, new Position(3f, 0f), MathF.PI);
            Assert.That(pt.GetModelFacingOffsets(m), Is.EqualTo(new[] { MathF.PI }));
        }

        private DataBinding<ModelData> MakeModel()
        {
            var model = new ModelData(0.75f, new List<Weapon>(), new Position(0, 0), _store);
            return _store.GetDataBinding<ModelData>(_store.Create(model));
        }

        private DataBinding<UnitData> MakeUnit(params DataBinding<ModelData>[] models)
        {
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4, modelBindings: new List<DataBinding<ModelData>>(models));
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
