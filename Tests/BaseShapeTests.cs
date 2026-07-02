using System.Collections.Generic;
using FDG;
using FDG.Data;
using NUnit.Framework;

namespace FDG.Tests
{
    // Foundation unit tests for the configurable base-shape system (#149 slice A): the IBaseShape
    // implementations (bounding radius + point containment) and the exact shape-to-shape base-to-base
    // distance (BaseShapeGeometry / DistanceUtilities shape-aware overloads).
    [TestFixture]
    public class BaseShapeTests
    {
        private const float Tol = 0.0001f;

        // --- CircleBase ------------------------------------------------------------------------------

        [Test]
        public void CircleBase_BoundingRadius_IsTheRadius()
        {
            Assert.That(new CircleBase(0.75f).BoundingRadiusInches, Is.EqualTo(0.75f).Within(Tol));
        }

        [Test]
        public void CircleBase_ContainsLocalPoint_InsideAndOutside()
        {
            CircleBase c = new CircleBase(1f);
            Assert.That(c.ContainsLocalPoint(0.5f, 0.5f), Is.True, "inside the circle");
            Assert.That(c.ContainsLocalPoint(0.9f, 0.9f), Is.False, "outside (corner past radius 1)");
        }

        // --- RectangleBase ---------------------------------------------------------------------------

        [Test]
        public void RectangleBase_BoundingRadius_IsHalfTheLesserSide()
        {
            // #149: the collision radius is the inscribed circle (half the lesser side), not the
            // circumscribing half-diagonal. 3 wide × 4 tall → lesser side 3 → radius 1.5.
            Assert.That(new RectangleBase(3f, 4f).BoundingRadiusInches, Is.EqualTo(1.5f).Within(Tol));
        }

        [Test]
        public void RectangleBase_ContainsLocalPoint_RespectsHalfExtents()
        {
            RectangleBase r = new RectangleBase(2f, 4f); // half-extents 1 (X) and 2 (Z)
            Assert.That(r.ContainsLocalPoint(0.9f, 1.9f), Is.True, "inside both half-extents");
            Assert.That(r.ContainsLocalPoint(1.1f, 0f), Is.False, "outside the X half-extent");
            Assert.That(r.ContainsLocalPoint(0f, 2.1f), Is.False, "outside the Z half-extent");
        }

        // --- Circle ↔ circle gap ---------------------------------------------------------------------

        [Test]
        public void Gap_CircleCircle_SubtractsBothRadii()
        {
            float gap = BaseShapeGeometry.SurfaceGap2D(
                new CircleBase(1f), new Position(0f, 0f),
                new CircleBase(1f), new Position(3f, 0f));
            Assert.That(gap, Is.EqualTo(1f).Within(Tol)); // 3 − 1 − 1
        }

        [Test]
        public void Gap_CircleCircle_MatchesLegacyRadiusOverload()
        {
            Position a = new Position(1f, 2f), b = new Position(5f, 5f);
            float shapeAware = DistanceUtilities.GetBaseToBaseDistanceInches_2D(a, b, new CircleBase(0.5f), new CircleBase(0.75f));
            float legacy = DistanceUtilities.GetBaseToBaseDistanceInches_2D(a, b, 0.5f, 0.75f);
            Assert.That(shapeAware, Is.EqualTo(legacy).Within(Tol), "circles must measure identically to the radius path.");
        }

        // --- Rect ↔ rect gap -------------------------------------------------------------------------

        [Test]
        public void Gap_RectRect_AlongOneAxis()
        {
            // Two 2×2 squares (half-extent 1), centres 3 apart on X → edge gap 1.
            float gap = BaseShapeGeometry.SurfaceGap2D(
                new RectangleBase(2f, 2f), new Position(0f, 0f),
                new RectangleBase(2f, 2f), new Position(3f, 0f));
            Assert.That(gap, Is.EqualTo(1f).Within(Tol));
        }

        [Test]
        public void Gap_RectRect_DiagonalCornerToCorner()
        {
            // Centres offset (3,3); each axis edge gap is 1 → corner distance sqrt(2).
            float gap = BaseShapeGeometry.SurfaceGap2D(
                new RectangleBase(2f, 2f), new Position(0f, 0f),
                new RectangleBase(2f, 2f), new Position(3f, 3f));
            Assert.That(gap, Is.EqualTo(1.41421f).Within(Tol));
        }

        [Test]
        public void Gap_RectRect_OverlapIsNegative()
        {
            float gap = BaseShapeGeometry.SurfaceGap2D(
                new RectangleBase(2f, 2f), new Position(0f, 0f),
                new RectangleBase(2f, 2f), new Position(1f, 0f));
            Assert.That(gap, Is.LessThan(0f), "overlapping bases report a negative (penetration) gap.");
        }

        // --- Circle ↔ rect gap (both argument orders) ------------------------------------------------

        [Test]
        public void Gap_CircleRect_BothOrdersAgree()
        {
            CircleBase circle = new CircleBase(1f);
            RectangleBase rect = new RectangleBase(2f, 2f); // half-extent 1
            Position pc = new Position(0f, 0f), pr = new Position(4f, 0f);

            float circleFirst = BaseShapeGeometry.SurfaceGap2D(circle, pc, rect, pr);
            float rectFirst = BaseShapeGeometry.SurfaceGap2D(rect, pr, circle, pc);

            // centre-to-rect = 4 − 1 = 3; minus circle radius 1 → 2.
            Assert.That(circleFirst, Is.EqualTo(2f).Within(Tol));
            Assert.That(rectFirst, Is.EqualTo(2f).Within(Tol), "gap is symmetric regardless of argument order.");
        }

        // --- Facing-aware gap (#150: the rounded-convex-hull mechanism) ------------------------------

        [Test]
        public void Gap_Rectangles_FacingChangesTheFootprint()
        {
            var tall = new RectangleBase(1f, 6f); // 1" wide × 6" tall
            Position pa = new Position(0f, 0f), pb = new Position(0f, 7f);
            Float2 up = new Float2(0f, 1f), side = new Float2(1f, 0f);

            // Both upright: the 6" lengths face each other → gap 7 − 3 − 3 = 1.
            Assert.That(BaseShapeGeometry.SurfaceGap2D(tall, pa, up, tall, pb, up),
                Is.EqualTo(1f).Within(Tol));

            // B turned sideways: only its 1" width spans Z → gap 7 − 3 − 0.5 = 3.5.
            Assert.That(BaseShapeGeometry.SurfaceGap2D(tall, pa, up, tall, pb, side),
                Is.EqualTo(3.5f).Within(Tol));
        }

        [Test]
        public void AreColliding_RotatedRect_FlipsResult()
        {
            var tall = new RectangleBase(1f, 6f);
            Position pa = new Position(0f, 0f), pb = new Position(0f, 5f);
            Float2 up = new Float2(0f, 1f), side = new Float2(1f, 0f);

            Assert.That(BaseShapeGeometry.AreColliding(tall, pa, up, tall, pb, up), Is.True,
                "both upright, their 6\" lengths overlap.");
            Assert.That(BaseShapeGeometry.AreColliding(tall, pa, up, tall, pb, side), Is.False,
                "turning one sideways pulls its footprint clear.");
        }

        [Test]
        public void Gap_CircleCircle_FacingIrrelevantAndByteIdentical()
        {
            var ca = new CircleBase(0.6f);
            var cb = new CircleBase(0.4f);
            Position a = new Position(1f, 2f), b = new Position(4f, 6f); // centre distance 5
            const float expected = 5f - 0.6f - 0.4f;

            float facingAware = BaseShapeGeometry.SurfaceGap2D(ca, a, new Float2(1f, 0f), cb, b, new Float2(0f, -1f));
            float facingLess = BaseShapeGeometry.SurfaceGap2D(ca, a, cb, b);
            float radiusPath = DistanceUtilities.GetBaseToBaseDistanceInches_2D(a, b, 0.6f, 0.4f);

            Assert.That(facingAware, Is.EqualTo(expected).Within(Tol), "circles ignore facing.");
            Assert.That(facingLess, Is.EqualTo(expected).Within(Tol));
            Assert.That(facingAware, Is.EqualTo(radiusPath).Within(Tol), "still byte-identical to the radius path.");
        }

        // --- 3D (vertical) ---------------------------------------------------------------------------

        [Test]
        public void Gap3D_CombinesHorizontalGapWithVertical()
        {
            // Horizontal X/Z gap 1 (3 apart, radii 1+1), vertical 4 → hypot(1,4) = sqrt(17).
            float gap = DistanceUtilities.GetBaseToBaseDistanceInches_3D(
                new Position(0f, 0f, 0f), new Position(3f, 4f, 0f),
                new CircleBase(1f), new CircleBase(1f));
            Assert.That(gap, Is.EqualTo(4.12310f).Within(Tol));
        }

        // --- Shape-to-point distance (#150: objective seizure etc.) ----------------------------------

        [Test]
        public void SurfaceDistanceToPoint2D_Circle_SubtractsRadius()
        {
            float d = BaseShapeGeometry.SurfaceDistanceToPoint2D(
                new CircleBase(1f), new Position(0f, 0f), new Float2(0f, 1f), new Position(4f, 0f));
            Assert.That(d, Is.EqualTo(3f).Within(Tol)); // 4" centre distance − 1" radius
        }

        [Test]
        public void SurfaceDistanceToPoint2D_Rectangle_OrientationChangesDistance()
        {
            // 1" wide × 6" tall base at the origin; query point 4" away along +Z.
            var rect = new RectangleBase(1f, 6f);
            var point = new Position(0f, 4f);

            // Facing +Z → the 6" (height) axis points at the query: nearest edge 3" out, so a 1" gap.
            float lengthwise = BaseShapeGeometry.SurfaceDistanceToPoint2D(rect, new Position(0f, 0f), new Float2(0f, 1f), point);
            Assert.That(lengthwise, Is.EqualTo(1f).Within(Tol));

            // Facing +X → only the 1" (width) axis points at the query: nearest edge 0.5" out, so a 3.5" gap.
            float crosswise = BaseShapeGeometry.SurfaceDistanceToPoint2D(rect, new Position(0f, 0f), new Float2(1f, 0f), point);
            Assert.That(crosswise, Is.EqualTo(3.5f).Within(Tol));
        }

        [Test]
        public void SurfaceDistanceToPoint2D_PointInsideBase_IsZero()
        {
            float d = BaseShapeGeometry.SurfaceDistanceToPoint2D(
                new RectangleBase(2f, 2f), new Position(0f, 0f), new Float2(0f, 1f), new Position(0.5f, 0.5f));
            Assert.That(d, Is.EqualTo(0f).Within(Tol));
        }

        // --- Defaults --------------------------------------------------------------------------------

        [Test]
        public void Default_Is28mmCircle()
        {
            IBaseShape def = BaseShapeDefaults.Default();
            Assert.That(def, Is.TypeOf<CircleBase>());
            Assert.That(def.BoundingRadiusInches, Is.EqualTo(BaseShapeDefaults.CircleRadiusInches).Within(Tol));
            Assert.That(((CircleBase)def).RadiusInches, Is.EqualTo(1.1023622f / 2f).Within(Tol));
        }

        // --- Serialization round-trip (live state: save/load + network) ------------------------------

        [Test]
        public void RectangleBase_SurvivesModelDataRoundTrip()
        {
            // The base is polymorphic on a live ModelData; TypeNameHandling.Auto must record the concrete
            // shape via $type so a rectangular base survives save/load + the wire (mirrors ModelIDTests).
            GameDataStore fromStore = NewStore();
            ModelData model = new ModelData(new RectangleBase(0.9842520f, 1.9685040f),
                new List<Weapon>(), new Position(), fromStore);
            DataReference modelRef = fromStore.Create(model);

            string woundsJson   = fromStore.GetValueAsJson<float>(model.RemainingWoundsBinding.Reference);
            string positionJson = fromStore.GetValueAsJson<Position>(model.PositionBinding.Reference);
            string positionJsonFacing = fromStore.GetValueAsJson<Float2>(model.FacingBinding.Reference);
            string modelJson    = fromStore.GetValueAsJson<ModelData>(modelRef);

            GameDataStore toStore = NewStore();
            toStore.CreateFromReferenceAndJson(model.RemainingWoundsBinding.Reference, woundsJson);
            toStore.CreateFromReferenceAndJson(model.PositionBinding.Reference, positionJson);
            toStore.CreateFromReferenceAndJson(model.FacingBinding.Reference, positionJsonFacing);
            toStore.CreateFromReferenceAndJson(modelRef, modelJson);

            ModelData restored = toStore.GetValue<ModelData>(modelRef);

            Assert.That(restored.BaseShape, Is.TypeOf<RectangleBase>(), "the concrete rectangle shape must survive.");
            RectangleBase rect = (RectangleBase)restored.BaseShape;
            Assert.That(rect.WidthInches, Is.EqualTo(0.9842520f).Within(Tol));
            Assert.That(rect.HeightInches, Is.EqualTo(1.9685040f).Within(Tol));
        }

        [Test]
        public void Facing_DefaultsToForward_UpdatesAndSurvivesRoundTrip()
        {
            // #150: every model carries a yaw facing (a unit normal). Default is +Z (0,1) — the pre-facing
            // axis-aligned convention — and like Position it is store-backed, so it must round-trip.
            GameDataStore fromStore = NewStore();
            ModelData model = new ModelData(new CircleBase(0.5f), new List<Weapon>(), new Position(), fromStore);

            Assert.That(model.Facing.X, Is.EqualTo(0f).Within(Tol), "default facing is +Z.");
            Assert.That(model.Facing.Y, Is.EqualTo(1f).Within(Tol), "default facing is +Z.");

            // SetFacing updates the value and raises the change event.
            bool fired = false;
            Float2 observed = default;
            ((IModel)model).OnFacingChanged += (oldValue, newValue) => { fired = true; observed = newValue; };
            model.SetFacing(new Float2(1f, 0f));
            Assert.That(fired, Is.True, "SetFacing raises OnFacingChanged.");
            Assert.That(observed.X, Is.EqualTo(1f).Within(Tol));
            Assert.That(model.Facing.X, Is.EqualTo(1f).Within(Tol));
            Assert.That(model.Facing.Y, Is.EqualTo(0f).Within(Tol));

            // The (non-default) facing must survive a serialization round-trip.
            DataReference modelRef = fromStore.Create(model);
            string woundsJson   = fromStore.GetValueAsJson<float>(model.RemainingWoundsBinding.Reference);
            string positionJson = fromStore.GetValueAsJson<Position>(model.PositionBinding.Reference);
            string facingJson   = fromStore.GetValueAsJson<Float2>(model.FacingBinding.Reference);
            string modelJson    = fromStore.GetValueAsJson<ModelData>(modelRef);

            GameDataStore toStore = NewStore();
            toStore.CreateFromReferenceAndJson(model.RemainingWoundsBinding.Reference, woundsJson);
            toStore.CreateFromReferenceAndJson(model.PositionBinding.Reference, positionJson);
            toStore.CreateFromReferenceAndJson(model.FacingBinding.Reference, facingJson);
            toStore.CreateFromReferenceAndJson(modelRef, modelJson);

            ModelData restored = toStore.GetValue<ModelData>(modelRef);
            Assert.That(restored.Facing.X, Is.EqualTo(1f).Within(Tol), "facing X must survive round-trip.");
            Assert.That(restored.Facing.Y, Is.EqualTo(0f).Within(Tol), "facing Y must survive round-trip.");
        }

        private static GameDataStore NewStore() =>
            new GameDataStore.GameDataStoreBuilder()
                .RegisterType<float>(8)
                .RegisterType<Position>(8)
                .RegisterType<ModelData>(8)
                .RegisterType<Float2>(8)
                .Build();
    }
}
