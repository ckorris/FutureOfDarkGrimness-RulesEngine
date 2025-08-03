using FDG.BuiltInAssets;
using Newtonsoft.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FDG.SerializableVisuals.Textures
{
    [Serializable]
    public class BuiltInPngTextureProvider : ITextureProvider
    {
        public readonly string ResourcePath;

        [JsonIgnore]
        public int PixelWidth
        { 
            get
            {
                LoadIfNeeded();
                return _image.Width;
            }
        }

        [JsonIgnore]
        public int PixelHeight
        {
            get
            {
                LoadIfNeeded();
                return _image.Height;
            }
        }

        [JsonIgnore]
        public byte[] RawTextureData
        {
            get
            {
                LoadIfNeeded();
                
                byte[] rawData = new byte[_image.Width * _image.Height * 4]; //4 one-byte channels.
                _image.CopyPixelDataTo(rawData);

                return rawData;
            }
        }

        [JsonIgnore]
        public ETextureFormat Format => ETextureFormat.RGBA;

        private bool _hasLoaded = false;

        private Image<Rgba32> _image;

        public BuiltInPngTextureProvider(string resourcePath)
        {
            ResourcePath = resourcePath; 
        }

        private void LoadIfNeeded()
        {
            if (_hasLoaded)
            {
                return;
            }

            byte[] textureData = BuiltInAssetHelper.GetEmbeddedResource(ResourcePath);

            using (MemoryStream stream = new MemoryStream(textureData))
            {
                _image = Image.Load<Rgba32>(stream);
            }

            _hasLoaded = true;
        }
    }
}
