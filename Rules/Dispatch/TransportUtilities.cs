using FDG.Rules.Foundation;
using FDG.Rules.Tokens;

namespace FDG.Rules.Dispatch;

/// <summary>
/// Pure-logic helpers for the Transport(X) core rule (#035). Transport is a first-class engine
/// subsystem under a thin <see cref="CoreRuleCatalog.Transport"/> marker; this is the foundation layer
/// the stage-level verbs (deploy-time loading, embark/disembark, mid-combat spillout) build on.
///
/// Two design commitments live here (decided with the user — see <c>WorkItems/035-transport.md</c>):
/// <list type="bullet">
///   <item><b>Occupancy is token-derived, not stored.</b> A transport keeps no occupant list; each
///   occupant carries a cross-unit <see cref="TokenType.EmbarkedIn"/> token whose <c>OwnerUnitID</c> is
///   the transport. A transport's load is a query over those tokens, and its capacity is the rule's
///   <c>Arg(0)</c>. So there is no bespoke state field on <c>UnitData</c>, and save/load is free.</item>
///   <item><b>Embarked units are off-table.</b> Occupant models stay at origin (off-battlefield), so the
///   inside↔outside targeting / activation / objective exclusions fall out of the existing
///   <see cref="IUnitExtensions.GetIsOnBattlefield"/> filter for free. Where a rule genuinely needs an
///   embarked unit's position (Caster proximity, #033), <see cref="GetEffectivePosition"/> resolves it
///   to the transport's — an opt-in seam, so generic reads are untouched.</item>
/// </list>
///
/// Methods take an explicit <c>allUnits</c> enumerable rather than an <c>ITableState</c> so the logic
/// is decoupled and unit-testable; the stage callers pass the live unit set.
///
/// NOTE (#035 slice A): bodies are intentionally unimplemented — this is the TDD scaffold that lets
/// <c>TransportUtilitiesTests</c> compile and run RED ahead of the implementation pass.
/// </summary>
public static class TransportUtilities
{
    /// <summary> Canonical name of the Transport core rule (its <see cref="CoreRuleCatalog.Transport"/> identity). </summary>
    public const string TransportRuleName = "Transport";

    /// <summary> A Hero is transportable only up to this Tough value. </summary>
    public const int HeroToughCap = 6;

    /// <summary> A non-Hero is transportable only up to this Tough value. </summary>
    public const int NonHeroToughCap = 3;

    /// <summary> Spaces a multi-wound (Tough ≥ 2) model occupies. </summary>
    public const int LargeModelSpaceCost = 3;

    /// <summary> Spaces a standard (Tough ≤ 1) model occupies. </summary>
    public const int StandardModelSpaceCost = 1;

    // --- Identity & capacity ---------------------------------------------------------------------

    /// <summary> True if <paramref name="unit"/> carries the Transport rule. </summary>
    public static bool IsTransport(IUnit unit) => throw new NotImplementedException();

    /// <summary>
    /// The transport's capacity in spaces — the Transport rule's <c>Arg(0)</c>. Returns 0 for a unit
    /// that is not a transport.
    /// </summary>
    public static int GetCapacity(IUnit transport) => throw new NotImplementedException();

    // --- Space cost ------------------------------------------------------------------------------

    /// <summary> Spaces a single model occupies: 1 if Tough ≤ 1, otherwise 3. </summary>
    public static int GetModelSpaceCost(IModel model) => throw new NotImplementedException();

    /// <summary> Total spaces a unit occupies — the sum over its living models' space costs. </summary>
    public static int GetUnitSpaceCost(IUnit unit) => throw new NotImplementedException();

    // --- Ride eligibility ------------------------------------------------------------------------

    /// <summary>
    /// Whether a single model is within the Tough cap to ride: Heroes up to <see cref="HeroToughCap"/>,
    /// non-Heroes up to <see cref="NonHeroToughCap"/>. Models above their cap can't be transported.
    /// </summary>
    public static bool CanModelRide(IModel model, bool isHero) => throw new NotImplementedException();

    /// <summary>
    /// Full eligibility check for loading <paramref name="candidate"/> into <paramref name="transport"/>:
    /// the transport is a transport, the candidate is friendly (same player), not already embarked, every
    /// model is within its ride cap, and the candidate's space cost fits the transport's remaining
    /// capacity (accounting for units already aboard). <paramref name="reason"/> describes the first
    /// failure when the result is false; empty on success.
    /// </summary>
    public static bool CanUnitEmbark(IUnit candidate, IUnit transport, IEnumerable<IUnit> allUnits, out string reason)
        => throw new NotImplementedException();

    // --- Occupancy (token-derived) ---------------------------------------------------------------

    /// <summary> True if <paramref name="unit"/> is currently embarked (carries an <see cref="TokenType.EmbarkedIn"/> token). </summary>
    public static bool IsEmbarked(IUnit unit) => throw new NotImplementedException();

    /// <summary> The id of the transport <paramref name="unit"/> is embarked in, or null if it is not embarked. </summary>
    public static UnitID? GetTransportId(IUnit unit) => throw new NotImplementedException();

    /// <summary> The units currently embarked in <paramref name="transport"/> (those whose <see cref="TokenType.EmbarkedIn"/> token is owned by it). </summary>
    public static IEnumerable<IUnit> GetOccupants(IUnit transport, IEnumerable<IUnit> allUnits)
        => throw new NotImplementedException();

    /// <summary> Total spaces consumed by everything currently aboard <paramref name="transport"/>. </summary>
    public static int GetOccupiedSpaces(IUnit transport, IEnumerable<IUnit> allUnits)
        => throw new NotImplementedException();

    /// <summary> Spaces still free on <paramref name="transport"/> = capacity − occupied. </summary>
    public static int GetRemainingCapacity(IUnit transport, IEnumerable<IUnit> allUnits)
        => throw new NotImplementedException();

    // --- State transitions (token writes) --------------------------------------------------------

    /// <summary>
    /// Loads <paramref name="unit"/> into <paramref name="transport"/> by stamping a cross-unit
    /// <see cref="TokenType.EmbarkedIn"/> token (owner = the transport). Does not move models — embarked
    /// units stay off-table; the caller sets aside / leaves them at origin.
    /// </summary>
    public static void Embark(IUnit unit, IUnit transport) => throw new NotImplementedException();

    /// <summary> Removes <paramref name="unit"/>'s <see cref="TokenType.EmbarkedIn"/> token (disembark / spillout). </summary>
    public static void Disembark(IUnit unit) => throw new NotImplementedException();

    // --- Destruction spillout & placement range --------------------------------------------------

    /// <summary>
    /// Models placed by a disembark exit or by destruction spillout must stay fully within this range of
    /// the transport ("must stay fully within 6\" of it when exiting" / "placed fully within 6\"").
    /// </summary>
    public const float MaxTransportRangeInches = 6f;

    /// <summary>
    /// Whether a proposed model position is within <see cref="MaxTransportRangeInches"/> of the transport —
    /// the "fully within 6\"" constraint shared by disembark exits and destruction-spillout placement. Pure
    /// geometry; the interactive placement request itself is stage-side.
    /// </summary>
    public static bool IsWithinTransportRange(Position modelPosition, Position transportPosition)
        => throw new NotImplementedException();

    /// <summary>
    /// The deterministic consequences a transport's destruction inflicts on one occupant once it has been
    /// placed: it is no longer embarked (the <see cref="TokenType.EmbarkedIn"/> link is cut), it becomes
    /// Shaken, and every living model takes a dangerous-terrain test (a decisive d6 via
    /// <paramref name="diceRoller"/>; a roll of 1 deals one wound). The placement itself — the owner
    /// choosing where, within 6" of the wreck — is the stage's interactive part and is NOT done here; this
    /// is the slice of spillout that is deterministic given the dice, so it can be unit-tested ahead of the
    /// mid-combat orchestration.
    /// </summary>
    public static void ApplySpilloutEffects(IUnit occupant, IDiceRoller diceRoller)
        => throw new NotImplementedException();

    // --- Effective position (opt-in seam; implementation deferred to first consumer) --------------

    /// <summary>
    /// The position a rule that cares about an embarked unit's location should use: the transport's
    /// position when <paramref name="unit"/> is embarked, otherwise the unit's own. Null when it can't be
    /// resolved (no living model, or an embarked unit whose transport isn't in <paramref name="allUnits"/>).
    /// The default position reads (targeting, LoS, melee, objectives) do NOT use this — they read raw
    /// positions and see origin for embarked units, which is the intended exclusion. Build is deferred
    /// until the first consumer (Caster, #033); the token already stores the link.
    /// </summary>
    public static Position? GetEffectivePosition(IUnit unit, IEnumerable<IUnit> allUnits)
        => throw new NotImplementedException();
}
