namespace FDG
{
    /// <summary>
    /// Axis-aligned bounding box of an <see cref="IBoundedZone"/>, in table inches (X horizontal, Z the
    /// table's depth axis — matching <see cref="Position"/> / <see cref="Float2"/> XZ). The placement
    /// resolvers iterate this box to generate candidate positions and reject any that fall outside the
    /// zone's true shape via <see cref="IZone.IsPointWithinZone"/> — so a circular zone places into a real
    /// circle, not its bounding square.
    /// </summary>
    public readonly struct ZoneBounds
    {
        public readonly float Left;
        public readonly float Right;
        public readonly float Bottom;
        public readonly float Top;

        public ZoneBounds(float left, float right, float bottom, float top)
        {
            Left = left;
            Right = right;
            Bottom = bottom;
            Top = top;
        }

        public float CenterX => (Left + Right) * 0.5f;
        public float CenterZ => (Bottom + Top) * 0.5f;
    }
}
