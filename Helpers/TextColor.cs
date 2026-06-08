namespace FDG
{
    /// <summary>
    /// A simple RGBA color for engine-emitted text — log lines and presentation banners. Kept as a
    /// plain 4-byte value (not a UI framework type) so it serializes cleanly over the bus and keeps
    /// the engine free of front-end dependencies; the front-end maps it to its own color type.
    /// </summary>
    public readonly record struct TextColor(byte R, byte G, byte B, byte A)
    {
        public static TextColor White => new(255, 255, 255, 255);

        public static TextColor FromRgb(byte r, byte g, byte b) => new(r, g, b, 255);
    }
}
