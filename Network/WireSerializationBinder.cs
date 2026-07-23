using FDG.SaveLoad;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace FDG.Network
{
    /// <summary>
    /// The <b>wire-path</b> serialization binder (#186): resolves polymorphic <c>$type</c> tokens in
    /// network messages against an allowlist instead of letting Newtonsoft's
    /// <see cref="DefaultSerializationBinder"/> resolve arbitrary assembly-qualified names. An
    /// attacker who can reach the TCP port controls every byte of a message, including its
    /// <c>$type</c> strings; with an open binder those can name framework "gadget" types whose
    /// deserialization has side effects — the classic Newtonsoft remote-code-execution vector. The
    /// save/load path deliberately keeps the permissive fallback (see #186's scope; files are
    /// tracked separately as #265).
    ///
    /// <para>What resolves, in order:</para>
    /// <list type="number">
    ///   <item>Assembly-less names that are <see cref="SaveTypeRegistry"/> IDs (what our own
    ///   serializer emits for registered types). Any other assembly-less name is refused — the
    ///   stable binder only ever writes assembly-less names for registered types, so an unknown
    ///   one is forged.</item>
    ///   <item>Types that resolve into the engine assembly itself. Wire compatibility is already
    ///   gated by the #075 protocol-version + type-map handshake, so both ends run the same build
    ///   and every legitimate transient wire payload (requests, results, presentation beats) is an
    ///   engine type. Engine types are plain data holders; the dangerous deserialization gadgets
    ///   all live in framework/library assemblies, which this rule excludes wholesale.</item>
    ///   <item>Benign framework shapes composed of allowed parts: primitives, and
    ///   arrays / <see cref="List{T}"/> / <see cref="Dictionary{TKey,TValue}"/> / <see cref="HashSet{T}"/>
    ///   whose element types recursively pass (Newtonsoft records concrete collection types under
    ///   <c>TypeNameHandling.Auto</c> — see the <c>List&lt;IZone&gt;</c> registry note).</item>
    /// </list>
    /// Everything else throws <see cref="JsonSerializationException"/>; the read loop's existing
    /// catch turns that into a disconnect of the offending connection.
    /// </summary>
    public sealed class WireSerializationBinder : ISerializationBinder
    {
        private static readonly DefaultSerializationBinder Fallback = new DefaultSerializationBinder();

        // The engine assembly — the home of every legitimate polymorphic wire payload.
        private static readonly System.Reflection.Assembly EngineAssembly = typeof(WireSerializationBinder).Assembly;

        // Benign leaf types that may appear as generic arguments of allowed collections.
        private static readonly HashSet<Type> AllowedFrameworkLeafTypes = new HashSet<Type>
        {
            typeof(bool), typeof(byte), typeof(sbyte), typeof(short), typeof(ushort),
            typeof(int), typeof(uint), typeof(long), typeof(ulong),
            typeof(float), typeof(double), typeof(decimal),
            typeof(char), typeof(string), typeof(Guid), typeof(DateTime), typeof(TimeSpan),
            typeof(object), // List<object> of allowed items: each element's own $type is still checked.
        };

        private static readonly HashSet<Type> AllowedGenericCollectionDefinitions = new HashSet<Type>
        {
            typeof(List<>), typeof(Dictionary<,>), typeof(HashSet<>),
            typeof(IReadOnlyList<>), typeof(IReadOnlyCollection<>), typeof(IReadOnlyDictionary<,>),
        };

        // Writing is identical to StableTypeSerializationBinder: stable ID when registered,
        // FullName + assembly otherwise. The wire format is unchanged by #186 — only what a
        // RECEIVED name may resolve to is restricted — so no protocol version bump.
        public void BindToName(Type serializedType, out string? assemblyName, out string? typeName)
        {
            if (SaveTypeRegistry.TryGetId(serializedType, out string id))
            {
                assemblyName = null;
                typeName = id;
                return;
            }

            Fallback.BindToName(serializedType, out assemblyName, out typeName);
        }

        public Type BindToType(string? assemblyName, string typeName)
        {
            if (assemblyName == null)
            {
                if (SaveTypeRegistry.TryGetType(typeName, out Type registered))
                {
                    return registered;
                }

                throw new JsonSerializationException(
                    $"Refusing wire $type '{typeName}': assembly-less names must be registered stable IDs (#186).");
            }

            Type resolved = Fallback.BindToType(assemblyName, typeName);

            if (!IsAllowed(resolved))
            {
                throw new JsonSerializationException(
                    $"Refusing wire $type '{typeName}, {assemblyName}': type is outside the engine's wire allowlist (#186).");
            }

            return resolved;
        }

        /// <summary>Public so tests (and any future wire surface) can query the rule directly.</summary>
        public static bool IsAllowed(Type type)
        {
            if (type.Assembly == EngineAssembly)
            {
                return true;
            }

            if (AllowedFrameworkLeafTypes.Contains(type))
            {
                return true;
            }

            if (type.IsArray)
            {
                Type? element = type.GetElementType();
                return element != null && IsAllowed(element);
            }

            if (type.IsConstructedGenericType &&
                AllowedGenericCollectionDefinitions.Contains(type.GetGenericTypeDefinition()))
            {
                foreach (Type argument in type.GetGenericArguments())
                {
                    if (!IsAllowed(argument))
                    {
                        return false;
                    }
                }
                return true;
            }

            return false;
        }
    }
}
