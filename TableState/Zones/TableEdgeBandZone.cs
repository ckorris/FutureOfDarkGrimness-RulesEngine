using Newtonsoft.Json;

namespace FDG
{
    /// <summary>
    /// #197 P17c — the placement region "fully within <see cref="BandWidthInches"/> of any table edge"
    /// (Reinforcement's arrival band). A bounded zone the placement resolvers can consume as-is:
    /// <see cref="Bounds"/> is the whole table and the true shape is a four-rectangle border frame,
    /// delegated to an internal <see cref="CompositeZone"/> so the path/entry geometry is the union
    /// logic that already exists. Like <see cref="CircularZone"/> placements, the band constrains the
    /// model's CENTRE to the true shape (the bounding-box inset only keeps the base on the table), so
    /// a base can straddle the band's inner boundary by up to its radius — the same approximation
    /// every non-rectangular placement zone accepts.
    /// </summary>
    public class TableEdgeBandZone : IBoundedZone
    {
        public float TableWidthInches { get; }
        public float TableHeightInches { get; }
        public float BandWidthInches { get; }

        [JsonIgnore] private readonly CompositeZone _frame;

        /// <summary>The four non-overlapping border rectangles, for renderers that draw by part.</summary>
        [JsonIgnore] public IReadOnlyList<IZone> Bands => _frame.Parts;

        [JsonConstructor]
        public TableEdgeBandZone(float tableWidthInches, float tableHeightInches, float bandWidthInches)
        {
            TableWidthInches = tableWidthInches;
            TableHeightInches = tableHeightInches;
            BandWidthInches = bandWidthInches;

            float w = tableWidthInches, h = tableHeightInches;
            float band = Math.Clamp(bandWidthInches, 0f, Math.Min(w, h) / 2f);
            // Non-overlapping: full-width top/bottom strips, side strips between them - a renderer
            // filling each part with alpha paints every point exactly once.
            _frame = new CompositeZone(new IZone[]
            {
                new RectangularZone(0f, w, 0f, band),           // bottom
                new RectangularZone(0f, w, h - band, h),        // top
                new RectangularZone(0f, band, band, h - band),  // left
                new RectangularZone(w - band, w, band, h - band), // right
            });
        }

        public ZoneBounds Bounds => new ZoneBounds(0f, TableWidthInches, 0f, TableHeightInches);

        public bool IsPointWithinZone(Float2 position) => _frame.IsPointWithinZone(position);

        public bool DoesPathIntersectZone(Float2 startPosition, Float2 endPosition)
            => _frame.DoesPathIntersectZone(startPosition, endPosition);

        public bool DoesPathIntersectZone(Float2 startPosition, Float2 endPosition, float inflationRadius)
            => _frame.DoesPathIntersectZone(startPosition, endPosition, inflationRadius);

        public Float2? GetFirstSegmentEntry(Float2 startPosition, Float2 endPosition)
            => _frame.GetFirstSegmentEntry(startPosition, endPosition);

        public Float2? GetLastSegmentExit(Float2 startPosition, Float2 endPosition)
            => _frame.GetLastSegmentExit(startPosition, endPosition);
    }
}
