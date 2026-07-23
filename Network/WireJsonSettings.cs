using FDG.Data;
using FDG.SaveLoad;
using Newtonsoft.Json;

namespace FDG.Network
{
    /// <summary>
    /// Builds the JSON settings for the untrusted network path (#186): the store's settings (same
    /// converters + <c>TypeNameHandling</c> the full-state sync depends on) with the permissive
    /// binder replaced by <see cref="AllowlistSerializationBinder"/>. Every wire deserialization —
    /// the message envelope (<c>MessageSerializer</c>), stage-request bodies (<c>StageResolverRegistry</c>),
    /// and reply bodies (<c>RequestMessageSender</c>) — must build its settings through here, never
    /// from <c>GetJsonSettings()</c> directly.
    ///
    /// <para>The store's own settings now also carry the allowlist binder (#265), so this factory
    /// exists mainly to isolate the wire's explicit <see cref="JsonSerializerSettings.MaxDepth"/> and
    /// to keep a clone the wire owns rather than sharing the store's mutable instance.</para>
    /// </summary>
    public static class WireJsonSettings
    {
        public static JsonSerializerSettings For(IReadableGameDataStore gameDataStore)
        {
            JsonSerializerSettings storeSettings = gameDataStore.GetJsonSettings();
            return new JsonSerializerSettings()
            {
                TypeNameHandling = storeSettings.TypeNameHandling,
                Converters = storeSettings.Converters,
                SerializationBinder = new AllowlistSerializationBinder(),
                // Explicit rather than inherited-by-default: an inbound frame must not drive the
                // deserializer past a sane nesting depth (Newtonsoft 13's default is 64; pinned here
                // so the wire path is unaffected by any future global default change).
                MaxDepth = 64,
            };
        }
    }
}
