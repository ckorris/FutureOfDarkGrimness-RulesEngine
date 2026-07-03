using System;

namespace FDG
{
    /// <summary>
    /// A base's footprint as a <b>rounded convex hull</b> in world space (#150): the convex-hull corner
    /// points plus a <see cref="Rounding"/> radius that inflates them (a Minkowski disc). This is the one
    /// uniform shape representation the pairwise collision routine (<see cref="BaseShapeGeometry"/>) consumes,
    /// so a new base shape only implements <see cref="IBaseShape.Footprint"/> — it never touches the collision
    /// code, and no shape-pair switch grows. A circle is a single point rounded by its radius; a rectangle is
    /// its four facing-oriented corners with zero rounding; a future hexagon is six corners; a capsule is two
    /// points with rounding.
    /// </summary>
    public readonly struct BaseFootprint
    {
        /// <summary> World-space convex-hull corners (at least one). A single point is a valid degenerate hull. </summary>
        public readonly Float2[] Corners;

        /// <summary> Minkowski inflation radius applied to the hull — a circle's radius; 0 for a straight-edged polygon. </summary>
        public readonly float Rounding;

        public BaseFootprint(Float2[] corners, float rounding)
        {
            Corners = corners;
            Rounding = rounding;
        }
    }

    /// <summary>
    /// A model's physical base footprint on the table (#149). Abstracts the base away from a single
    /// circular radius so non-circular bases (rectangles now; more shapes later) collide, measure, and
    /// render with their real footprint.
    ///
    /// Two seams every shape must provide:
    /// <list type="bullet">
    ///   <item><see cref="BoundingRadiusInches"/> — the circumscribing-circle radius. Geometry paths that
    ///   are NOT yet shape-aware (terrain swept-paths, pile-in, LoS blockers, objective seizure — see
    ///   <c>WorkItems/150</c>) treat any base as this circle, and <see cref="IModel.BaseRadiusInches"/>
    ///   returns it, so every existing radius-based call site keeps working and a bigger base of any shape
    ///   still means a bigger footprint.</item>
    ///   <item><see cref="ContainsLocalPoint"/> — point-in-base test in base-local inches (the base centre
    ///   is the origin, +X right, +Z "down"/forward), for exact hit-testing and rendering.</item>
    /// </list>
    /// Bases are axis-aligned (no facing/rotation yet — models have no facing in the engine). Exact
    /// shape-to-shape distance lives in <see cref="BaseShapeGeometry"/>.
    /// </summary>
    public interface IBaseShape
    {
        /// <summary>
        /// The single-radius approximation of this base used by collision paths that are not yet shape-aware
        /// (terrain swept-paths, LoS blockers, placement spacing — see <c>WorkItems/150</c>) and by
        /// render/geometry fallbacks. A circle reports its radius. A rectangle reports the INSCRIBED circle
        /// (half its lesser side) rather than the circumscribing one — the circumscribing (half-diagonal)
        /// radius over-blocked movement near terrain, so #149 (with the user) uses the lesser dimension,
        /// trading that for a slight under-block at the long ends until #150 brings true swept-shape geometry.
        /// </summary>
        float BoundingRadiusInches { get; }

        /// <summary>
        /// True if a point at offset (<paramref name="dxInches"/>, <paramref name="dzInches"/>) from the
        /// base centre — in the table's horizontal X/Z plane, inches — is on or inside the base.
        /// </summary>
        bool ContainsLocalPoint(float dxInches, float dzInches);

        /// <summary>
        /// The CIRCUMSCRIBING-circle radius — the base fits entirely inside this circle at any facing. Use it
        /// for rotation-safe conservative checks (placement spacing, staying on the table). Contrast with
        /// <see cref="BoundingRadiusInches"/>, which for a rectangle is the smaller inscribed circle (#149).
        /// </summary>
        float CircumscribedRadiusInches { get; }

        /// <summary>
        /// Distance (inches) from a point at base-local offset (<paramref name="dxInches"/>,
        /// <paramref name="dzInches"/>) to the nearest point on the base's surface; 0 on or inside. Callers
        /// rotate world offsets into the base-local frame first (see <see cref="BaseShapeGeometry"/>).
        /// </summary>
        float DistanceToLocalPoint(float dxInches, float dzInches);

        /// <summary>
        /// This base's world footprint as an <see cref="IZone"/>, centred at <paramref name="centre"/> and
        /// oriented by <paramref name="facing"/> (a yaw unit normal along local +Z; zero → forward). Lets
        /// zone-based systems (LoS blockers, swept move-through tests) treat a base like terrain (#150).
        /// </summary>
        IZone ToZone(Position centre, Float2 facing);

        /// <summary>
        /// This base's <see cref="BaseFootprint"/> — its rounded convex hull in world space, centred at
        /// <paramref name="centre"/> and oriented by <paramref name="facing"/> (yaw unit normal along local +Z;
        /// zero → forward). The single seam every shape provides so that <see cref="BaseShapeGeometry.SurfaceGap2D"/>
        /// can measure any two bases with no per-shape-pair code — implement this and the shape collides and
        /// measures against every existing shape for free.
        /// </summary>
        BaseFootprint Footprint(Position centre, Float2 facing);
    }

    /// <summary> A circular base of a given radius (the classic round wargaming base). </summary>
    public sealed class CircleBase : IBaseShape
    {
        public float RadiusInches { get; }

        public CircleBase(float radiusInches)
        {
            RadiusInches = radiusInches;
        }

        public float BoundingRadiusInches => RadiusInches;

        public float CircumscribedRadiusInches => RadiusInches;

        public bool ContainsLocalPoint(float dxInches, float dzInches) =>
            dxInches * dxInches + dzInches * dzInches <= RadiusInches * RadiusInches;

        public float DistanceToLocalPoint(float dxInches, float dzInches) =>
            MathF.Max(0f, MathF.Sqrt(dxInches * dxInches + dzInches * dzInches) - RadiusInches);

        // A circle is rotation-invariant, so facing is irrelevant.
        public IZone ToZone(Position centre, Float2 facing) => new CircularZone(centre.x, centre.z, RadiusInches);

        // A disc: a single hull point (its centre) rounded by the radius. Facing is irrelevant.
        public BaseFootprint Footprint(Position centre, Float2 facing) =>
            new BaseFootprint(new[] { new Float2(centre.x, centre.z) }, RadiusInches);
    }

    /// <summary>
    /// An axis-aligned rectangular base: <see cref="WidthInches"/> spans the table's X axis,
    /// <see cref="HeightInches"/> the Z axis. No facing yet (see <see cref="IBaseShape"/>).
    /// </summary>
    public sealed class RectangleBase : IBaseShape
    {
        public float WidthInches { get; }

        public float HeightInches { get; }

        public RectangleBase(float widthInches, float heightInches)
        {
            WidthInches = widthInches;
            HeightInches = heightInches;
        }

        // Inscribed circle: half the lesser side (#149, with the user). NOT the circumscribing half-diagonal
        // — that over-blocked movement near terrain. The exact rectangle footprint is used for model-to-model
        // measurement (BaseShapeGeometry); this radius is only the approximation for the #150 collision paths.
        public float BoundingRadiusInches => 0.5f * MathF.Min(WidthInches, HeightInches);

        public float CircumscribedRadiusInches =>
            0.5f * MathF.Sqrt(WidthInches * WidthInches + HeightInches * HeightInches);

        public bool ContainsLocalPoint(float dxInches, float dzInches) =>
            MathF.Abs(dxInches) <= WidthInches * 0.5f && MathF.Abs(dzInches) <= HeightInches * 0.5f;

        public float DistanceToLocalPoint(float dxInches, float dzInches)
        {
            float qx = MathF.Max(0f, MathF.Abs(dxInches) - WidthInches * 0.5f);
            float qz = MathF.Max(0f, MathF.Abs(dzInches) - HeightInches * 0.5f);
            return MathF.Sqrt(qx * qx + qz * qz);
        }

        // The oriented world footprint: an axis-aligned rect rotated so its local +Z (height) axis points along
        // the facing. Table X/Z space (no screen Y-flip), so the angle is atan2(−facing.X, facing.Y).
        public IZone ToZone(Position centre, Float2 facing)
        {
            var rect = new RectangularZone(
                centre.x - WidthInches * 0.5f, centre.x + WidthInches * 0.5f,
                centre.z - HeightInches * 0.5f, centre.z + HeightInches * 0.5f);
            Float2 f = facing.X == 0f && facing.Y == 0f ? new Float2(0f, 1f) : facing;
            float deg = MathF.Atan2(-f.X, f.Y) * (180f / MathF.PI);
            return MathF.Abs(deg) < 1e-4f
                ? rect
                : new RotatedZoneWrapper(rect, deg, new Float2(centre.x, centre.z));
        }

        // Four facing-oriented corners (local +Z = height runs along facing, +X = width perpendicular),
        // wound consistently so hull edges connect adjacent corners. Straight edges → zero rounding.
        public BaseFootprint Footprint(Position centre, Float2 facing)
        {
            Float2 f = facing.X == 0f && facing.Y == 0f ? new Float2(0f, 1f) : facing;
            Float2 hAxis = f;                             // local +Z (height)
            Float2 wAxis = new Float2(f.Y, -f.X);         // local +X (width), perpendicular
            float hw = WidthInches * 0.5f, hh = HeightInches * 0.5f;
            float cx = centre.x, cz = centre.z;

            Float2 Corner(float sx, float sz) => new Float2(
                cx + sx * hw * wAxis.X + sz * hh * hAxis.X,
                cz + sx * hw * wAxis.Y + sz * hh * hAxis.Y);

            // CCW-consistent winding: BL, BR, TR, TL.
            Float2[] corners =
            {
                Corner(-1f, -1f), Corner(1f, -1f), Corner(1f, 1f), Corner(-1f, 1f),
            };
            return new BaseFootprint(corners, 0f);
        }
    }

    /// <summary>
    /// Canonical default base dimensions (#149). The circle default reproduces the value the engine
    /// hardcoded before this feature (a 28mm round base), so existing armies are unchanged.
    /// </summary>
    public static class BaseShapeDefaults
    {
        /// <summary> 28mm round base, in inches — the pre-#149 hardcoded default diameter. </summary>
        public const float CircleDiameterInches = 1.1023622f;

        public const float CircleRadiusInches = CircleDiameterInches / 2f;

        /// <summary> 25mm × 50mm — a classic cavalry base, in inches. </summary>
        public const float RectangleWidthInches = 0.9842520f;   // 25mm
        public const float RectangleHeightInches = 1.9685040f;  // 50mm

        /// <summary> The default base every model gets when none is specified (old army files, legacy saves). </summary>
        public static IBaseShape Default() => new CircleBase(CircleRadiusInches);
    }
}
