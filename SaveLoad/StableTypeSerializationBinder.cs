using Newtonsoft.Json.Serialization;

namespace FDG.SaveLoad
{
    /// <summary>
    /// A Newtonsoft <see cref="ISerializationBinder"/> that writes and reads the polymorphic <c>$type</c>
    /// tokens Newtonsoft emits under <c>TypeNameHandling.Auto</c> using <see cref="SaveTypeRegistry"/>'s
    /// stable string IDs instead of assembly-qualified type names (#070). Installed on the store's JSON
    /// settings (<see cref="GameDataStore.GetJsonSettings"/>), it makes renaming a persisted payload type — a
    /// base shape, a token payload, a token clear-trigger, or a terrain zone — safe for both saves and the
    /// full-state network sync (which share those settings).
    ///
    /// <para>Purely additive: a type the registry doesn't know falls straight through to Newtonsoft's
    /// <see cref="DefaultSerializationBinder"/>, i.e. exactly the pre-#070 FullName behavior. A registered
    /// type is written as <c>"$type": "&lt;id&gt;"</c> with no assembly token; on read, an assembly-less
    /// type name that matches a registry ID resolves through it, and everything else (legacy FullName
    /// <c>$type</c>s, framework types) goes to the default binder. Since the default binder always writes a
    /// non-null assembly name, an absent assembly is an unambiguous signal that the name is one of ours.</para>
    /// </summary>
    public sealed class StableTypeSerializationBinder : ISerializationBinder
    {
        private readonly DefaultSerializationBinder _fallback = new DefaultSerializationBinder();

        public void BindToName(Type serializedType, out string? assemblyName, out string? typeName)
        {
            if (SaveTypeRegistry.TryGetId(serializedType, out string id))
            {
                assemblyName = null;
                typeName = id;
                return;
            }

            _fallback.BindToName(serializedType, out assemblyName, out typeName);
        }

        public Type BindToType(string? assemblyName, string typeName)
        {
            if (assemblyName == null && SaveTypeRegistry.TryGetType(typeName, out Type type))
            {
                return type;
            }

            return _fallback.BindToType(assemblyName, typeName);
        }
    }
}
