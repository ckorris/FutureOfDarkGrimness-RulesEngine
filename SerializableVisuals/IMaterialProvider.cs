using System.Numerics;

namespace FDG.SerializableVisuals
{
    public interface IMaterialProvider
    {
        Vector4 BaseColor { get; }

        Vector4 EmissiveColor { get; }

        ITextureProvider? BaseColorTexture { get; }

        ITextureProvider? NormalMapTexture { get; }

        ITextureProvider? RoughnessMapTexture { get; }

        ITextureProvider? MetallicMapTexture { get; }

        ITextureProvider? SpecularMapTexture { get; }

        ITextureProvider? EmissionMapTexture { get; }
    }
}
