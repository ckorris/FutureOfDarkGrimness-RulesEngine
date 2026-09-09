using FDG.Rules.Foundation;

namespace FDG.Rules.Definitions;

/// <summary>
/// Top-level definition of one rule. The unit of authoring — typically one
/// file (and eventually one JSON record) per rule.
///
/// Bundles:
/// <list type="bullet">
///   <item><see cref="Name"/> — the canonical identifier rules reference each
///         other by (e.g. "Regeneration", "Furious"). Stable; used as the
///         lookup key in the rule registry.</item>
///   <item><see cref="Passive"/> — all the hook attachments that fire
///         automatically when their conditions match. May be empty (e.g. a
///         pure spell rule has no passive entries).</item>
///   <item><see cref="Activated"/> — all player-triggered abilities the rule
///         provides. May be empty (e.g. Furious is purely passive).</item>
///   <item><see cref="Scope"/> — whether the rule attaches to a unit or to an
///         individual weapon (#027). Defaults to <see cref="ERuleScope.Unit"/>;
///         army-load refuses to attach a rule at the wrong scope.</item>
///   <item><see cref="Valence"/> — whether this rule reads as good/bad/neutral for the unit that
///         has it (#151). Read when a token GRANTS this rule, to color the token by the granted
///         rule's effect on its bearer. Defaults to <see cref="EValence.Neutral"/>; authored per rule.</item>
///   <item><see cref="Description"/> — a concise, player-facing summary of what the rule does
///         (#151), surfaced in granted-rule token hovers and rule tooltips. Defaults to empty.</item>
///   <item><see cref="EngineArgumentCount"/> — how many arguments the rule takes
///         that the engine reads <em>directly</em> (not through an effect's
///         <see cref="ValueSource.Arg"/>). Almost always 0: arg-driven rules like
///         Tough(3) reference their value from an effect, so
///         <see cref="Dispatch.RuleArgumentArity"/> infers their arity. The
///         exception is a "marker-with-arg" core rule whose value is consumed by
///         engine code rather than an effect — Transport(X), whose capacity
///         <c>TransportUtilities.GetCapacity</c> reads — which would otherwise look
///         like a 0-arg rule (non-numeric in the picker, no value field). Counted
///         toward arity/numeric detection alongside effect-referenced args.</item>
/// </list>
///
/// A rule may have only Passive entries (Furious, Stealth), only Activated
/// entries (most spells), or both (e.g. Battleborn's "passive recovery roll
/// at round start" combined with an activated piece).
/// </summary>
public record SpecialRuleDefinition(string Name, IReadOnlyList<HookEntry> Passive,
    IReadOnlyList<ActivatedAbility> Activated, ERuleScope Scope = ERuleScope.Unit,
    int EngineArgumentCount = 0, EValence Valence = EValence.Neutral, string Description = "")
{
    // #258: identity IS the canonical name - the registry allows one definition per name per
    // game, and every engine-side "has this rule?" check compares definitions with ==. The
    // default record equality compared the IReadOnlyList<> members by reference, so the
    // per-attachment instances that save/load rehydration rebuilds (#095, no resolver on the
    // resume path) were never equal to the catalog's - silently breaking every Definition ==
    // site on resumed games and crashing BuildWeaponOptions when WeaponComparer stopped
    // grouping same-named weapons (the WayTooManyInBack Sniper Team fault).
    public virtual bool Equals(SpecialRuleDefinition? other) => other is not null && Name == other.Name;

    public override int GetHashCode() => Name.GetHashCode();

    // #191 search perf pass 4/10: which (hook, seat) pairs any passive entry listens on, and which
    // hooks an activated ability triggers on. Definitions are immutable and never copied with `with`,
    // so these are pure functions of the constructor arguments.
    //
    // Built in the constructor, NOT memoized lazily. A definition instance is process-wide shared -
    // the core catalog's singletons, plus army-embedded definitions which since perf pass 7 come from
    // one content-keyed cache - and StoreClone shares ResolvedRule by reference, so every clone in
    // every search worker points at the same definition. A lazy `??=` here is therefore written by all
    // 4 search workers (and by every parallel lab game) on first use. That cannot corrupt memory - the
    // array is fully built before the reference is published, and a reference store is atomic - but on
    // a weak memory model (arm64, i.e. the shipped osx-arm64 build) another thread can observe the
    // reference before the element writes, read a stale zero bit, and skip a rule that should have
    // fired. That would break the "same seed, same tree" guarantee on Apple Silicon only. Building
    // eagerly costs one small array per definition per process and removes the hazard entirely.
    private readonly ulong[] _listeners = HookListeners.Build(Passive);
    private readonly ulong[] _activators = HookListeners.BuildActivated(Activated);

    /// <summary>True when some passive entry of this rule fires at this hook from this seat.</summary>
    public bool ListensAt(EHookID hook, ERuleSeat seat) => HookListeners.Test(_listeners, hook, seat);

    /// <summary>True when some activated ability of this rule triggers at this hook.</summary>
    public bool ActivatesAt(EHookID hook) => HookListeners.Test(_activators, hook, ERuleSeat.Actor);
}

/// <summary>
/// A bitset over (hook, seat) pairs: bit index = hook value x 2 + seat. The dispatch fast path asks
/// "does any rule on this participant listen here?" before it allocates anything (#191 pass 4).
/// </summary>
public static class HookListeners
{
    private const int SeatCount = 2;
    private static readonly int Words =
        ((Enum.GetValues<EHookID>().Select(h => (int)h).Max() + 1) * SeatCount + 63) / 64;

    public static ulong[] Build(IReadOnlyList<HookEntry> passive)
    {
        var mask = new ulong[Words];
        foreach (HookEntry entry in passive)
        {
            int bit = (int)entry.HookID * SeatCount + (int)entry.Seat;
            mask[bit >> 6] |= 1UL << (bit & 63);
        }
        return mask;
    }

    /// <summary>Activated abilities have no seat; they are recorded on the Actor bit of their trigger hook.</summary>
    public static ulong[] BuildActivated(IReadOnlyList<ActivatedAbility> activated)
    {
        var mask = new ulong[Words];
        foreach (ActivatedAbility ability in activated)
        {
            int bit = (int)ability.TriggerHook * SeatCount + (int)ERuleSeat.Actor;
            mask[bit >> 6] |= 1UL << (bit & 63);
        }
        return mask;
    }

    public static bool Test(ulong[] mask, EHookID hook, ERuleSeat seat)
    {
        int bit = (int)hook * SeatCount + (int)seat;
        int word = bit >> 6;
        return word < mask.Length && (mask[word] & (1UL << (bit & 63))) != 0;
    }
}
