
namespace FDG.SerializableVisuals
{
    public interface ITextureProvider
    {
        int PixelWidth { get; }

        int PixelHeight { get; }

        byte[] RawTextureData { get; }

        ETextureFormat Format { get; }
    }
}
