using System.Reflection;
using System.Runtime.CompilerServices;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch;

/// <summary>
/// Maps each <see cref="EHookID"/> to the context type fired at it, and reports the
/// capability interfaces (<see cref="ICapability"/>) that context provides. Built by
/// reflection — every concrete <see cref="IHookContext"/> in the engine assembly is
/// probed for its constant <c>Hook</c> — so there is no hand-maintained table to
/// drift out of sync as contexts are added.
///
/// Assumes a context's <c>Hook</c> getter is a constant expression that reads no
/// instance state (true for all current contexts), which lets us read it off an
/// uninitialized instance without invoking a constructor. If that ever stops
/// holding, replace the probe with an explicit dictionary.
/// </summary
public sealed class HookContextCatalog
{
    private readonly Dictionary<EHookID, Type> _contextTypeByHook;
    
    public HookContextCatalog() : this(typeof(IHookContext).Assembly) {}

    public HookContextCatalog(Assembly assembly)
    {
        var map = new Dictionary<EHookID, Type>();
        foreach (Type type in assembly.GetTypes())
        {
            if (type.IsAbstract || type.IsInterface || !typeof(IHookContext).IsAssignableFrom(type))
            {
                continue;
            }

            IHookContext probe = (IHookContext)RuntimeHelpers.GetUninitializedObject(type);
            map[probe.Hook] = type;
        }

        _contextTypeByHook = map;
    }

    public bool TryGetContextType(EHookID hook, out Type contextType)
    {
        return _contextTypeByHook.TryGetValue(hook, out contextType!);
    }

    public IReadOnlyCollection<Type> CapabilitiesOf(EHookID hook)
    {
        if (!_contextTypeByHook.TryGetValue(hook, out Type? type))
        {
            return Array.Empty<Type>();
        }

        return type.GetInterfaces()
            .Where(i => typeof(ICapability).IsAssignableFrom(i) && i != typeof(ICapability))
            .ToArray();
    }
}