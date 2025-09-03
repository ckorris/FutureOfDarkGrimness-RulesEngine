using FDG.BuiltInAssets;
using Newtonsoft.Json;
/*
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
*/

//TODO: I had to comment out this on a flight because I couldn't download the proper NuGet package.

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
                return 1;
                LoadIfNeeded();
                //return _image.Width;
            }
        }

        [JsonIgnore]
        public int PixelHeight
        {
            get
            {
                return 1;
                LoadIfNeeded();
                //return _image.Height;
            }
        }

        [JsonIgnore]
        public byte[] RawTextureData
        {
            get
            {
                return new byte[4] { 1,1,1,1};
                LoadIfNeeded();
                
                //byte[] rawData = new byte[_image.Width * _image.Height * 4]; //4 one-byte channels.
                //_image.CopyPixelDataTo(rawData);

                //return rawData;
            }
        }

        [JsonIgnore]
        public ETextureFormat Format => ETextureFormat.RGBA;

        private bool _hasLoaded = false;

        //private Image<Rgba32> _image;

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

            /*
            using (MemoryStream stream = new MemoryStream(textureData))
            {
                _image = Image.Load<Rgba32>(stream);

                //ImageSharp assumes Y coordinates are top-down, but most graphics APIs do the opposite. So flip the data.
                _image.Mutate(x => x.Flip(FlipMode.Vertical));
            }
            */
            _hasLoaded = true;
        }
    }
}
