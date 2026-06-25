namespace FDG.SaveLoad
{
    /// <summary> The base shapes an army-list entry can choose (#149). More may be added later. </summary>
    public enum EBaseShapeKind
    {
        Circle,
        Rectangle,
    }

    /// <summary>
    /// The per-unit base footprint as authored in a <c>.fdgarmy</c> file (#149). Stored as plain primitives
    /// (a string enum + dimensions in inches) rather than a polymorphic shape, so it needs no custom STJ
    /// converter and an old army file with no base simply keeps the defaults (a 28mm circle). Dimensions are
    /// in INCHES: a circle uses <see cref="DiameterInches"/>; a rectangle uses <see cref="WidthInches"/> ×
    /// <see cref="HeightInches"/>. <see cref="ToBaseShape"/> turns it into the engine <see cref="IBaseShape"/>.
    /// </summary>
    [Serializable]
    public class BaseFileEntry
    {
        public EBaseShapeKind Shape { get; set; } = EBaseShapeKind.Circle;

        public float DiameterInches { get; set; } = BaseShapeDefaults.CircleDiameterInches;

        public float WidthInches { get; set; } = BaseShapeDefaults.RectangleWidthInches;

        public float HeightInches { get; set; } = BaseShapeDefaults.RectangleHeightInches;

        public IBaseShape ToBaseShape() => Shape switch
        {
            EBaseShapeKind.Rectangle => new RectangleBase(WidthInches, HeightInches),
            _ => new CircleBase(DiameterInches / 2f),
        };
    }
}
