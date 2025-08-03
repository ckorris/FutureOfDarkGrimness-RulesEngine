using System.Reflection;

namespace FDG.BuiltInAssets
{
    public static class BuiltInAssetHelper
    {
        public const string SILLYMANMODEL_PATH = "FutureOfDarkGrimness.BuiltInAssets.SillyManModel.obj";

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
