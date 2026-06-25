using System;

namespace FDG
{
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
        /// <summary> Radius of the smallest circle (centred on the base) that fully contains the shape. </summary>
        float BoundingRadiusInches { get; }

        /// <summary>
        /// True if a point at offset (<paramref name="dxInches"/>, <paramref name="dzInches"/>) from the
        /// base centre — in the table's horizontal X/Z plane, inches — is on or inside the base.
        /// </summary>
        bool ContainsLocalPoint(float dxInches, float dzInches);
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

        public bool ContainsLocalPoint(float dxInches, float dzInches) =>
            dxInches * dxInches + dzInches * dzInches <= RadiusInches * RadiusInches;
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

        // Half-diagonal of the rectangle = radius of the circumscribing circle.
        public float BoundingRadiusInches =>
            0.5f * MathF.Sqrt(WidthInches * WidthInches + HeightInches * HeightInches);

        public bool ContainsLocalPoint(float dxInches, float dzInches) =>
            MathF.Abs(dxInches) <= WidthInches * 0.5f && MathF.Abs(dzInches) <= HeightInches * 0.5f;
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
