using Newtonsoft.Json;

namespace FDG.SaveLoad
{
    /// <summary>
    /// JSON load/save for <see cref="TerrainLayoutFile"/>. Engine-side variant —
    /// the application layer (<c>FdgRaylib.Cli.TerrainLoader</c>) carries the same
    /// settings; both must stay in sync.
    /// </summary>
    public static class TerrainLayoutLoader
    {
        private static readonly JsonSerializerSettings _settings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.Auto,
            // Terrain layout files are untrusted input (may be hand-authored or shared), and the
            // IZone $type is polymorphic - gate it through the allowlist (#265) so a crafted file
            // can't resolve a Newtonsoft gadget. Legit zones are engine types and still load; the
            // binder also makes the format rename-safe (registered zones now write stable IDs).
            SerializationBinder = new AllowlistSerializationBinder(),
        };

        public static TerrainLayoutFile? TryLoadFromFile(string path, out string? error)
        {
            error = null;
            if (!File.Exists(path))
            {
                error = $"File not found: {path}";
                return null;
            }

            try
            {
                string json = File.ReadAllText(path);
                var layout = JsonConvert.DeserializeObject<TerrainLayoutFile>(json, _settings);
                if (layout == null)
                {
                    error = "Deserialized terrain layout was null.";
                    return null;
                }
                return layout;
            }
            catch (Exception ex)
            {
                error = $"Failed to load terrain layout: {ex.Message}";
                return null;
            }
        }
    }
}
