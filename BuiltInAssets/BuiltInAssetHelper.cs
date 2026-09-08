using System.Reflection;

namespace FDG.BuiltInAssets
{
    public static class BuiltInAssetHelper
    {
        public const string SILLYMANMODEL_PATH = "FDG.BuiltInAssets.SillyManModel.obj";

        public const string SILLYMANTEXTURE_PATH = "FDG.BuiltInAssets.SillyManTexture.png";

        /// <summary>
        /// #191 step 15b: the weights the Strategist's search scores leaves with. See the file's own
        /// "provenance" block for what trained it and what it measured.
        /// </summary>
        public const string STRATEGIST_LEAF_PATH = "FDG.BuiltInAssets.StrategistLeafV1.json";

        public static byte[] GetEmbeddedResource(string resourcePath)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();

            using (Stream stream = assembly.GetManifestResourceStream(resourcePath))
            {
                if (stream != null)
                {
                    byte[] buffer = new byte[stream.Length];
                    stream.Read(buffer, 0, buffer.Length);
                    return buffer;
                }
                else
                {
                    throw new InvalidOperationException($"Resource not found with path: {resourcePath}");
                }
            }
        }
    }
}
