using System;
using System.Numerics;

namespace FDG.SerializableVisuals.Materials
{
    [Serializable]
    public class BasicMaterial : IMaterialProvider
    {
        public Vector4? BaseColor { get; set; }

        public Vector4? EmissiveColor { get; set; }

        public ITextureProvider? BaseColorTexture { get; set; }

        public ITextureProvider? NormalMapTexture { get; set; }

        public ITextureProvider? RoughnessMapTexture { get; set; }

        public ITextureProvider? MetallicMapTexture { get; set; }

        public ITextureProvider? SpecularMapTexture { get; set; }

        public ITextureProvider? EmissionMapTexture { get; set; }
    }
}
