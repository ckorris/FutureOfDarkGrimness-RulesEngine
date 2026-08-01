using FDG.Rules.Foundation;

namespace FDG.Rules.Tokens;

/// <summary>
/// The single definition of one engine-known token TYPE (#151, Option B): both its type-intrinsic
/// gameplay defaults (a fixed clear trigger for fixed-lifetime types) and its display metadata (name,
/// valence, prominence, optional color/shape overrides), in one place — so a token can't have its
/// behavior described in one spot and its look in another and silently drift apart. Held by
/// <see cref="TokenDefinitionCatalog"/> and used both to construct tokens (<c>Create</c>) and to resolve
/// them for display (<c>TokenDisplay</c>).
///
/// Instance-contextual fields — count, payload, owner, and the carriers' clear triggers — are
/// deliberately NOT here: by nature they vary between two tokens of the same type, so they're supplied at
/// the single creation/derivation site. See <see cref="TokenDefinitionCatalog"/>.
/// </summary>
/// <param name="DefaultClearTrigger">
/// The lifecycle for fixed-lifetime types (Shaken → ManualOnly, Fatigued → RoundEnd). Null for the
/// generic carriers, whose trigger is contributed by the granting effect — calling <c>Create</c> for such
/// a type without an explicit override throws.
/// </param>
/// <param name="ValenceSource">How <see cref="Valence"/> is determined for this type — see <see cref="EValenceSource"/>.</param>
/// <param name="VisibleOnlyWhenRead">
/// This type is BOOKKEEPING for whichever rules happen to read it, not a status the player is meant to
/// track in general — so it drops to <see cref="ETokenProminence.Invisible"/> unless its bearer actually
/// carries a rule whose condition tests it (<c>TokenReadership.IsReadByAnyRule</c>). "Moved" is the case
/// this exists for: every unit that moves gets stamped, but only a Mobile Artillery cares, and a chip on
/// every unit on the table is noise that means nothing to the other 99% of them.
/// <para>Resolved per BEARER, so it needs the unit passed to <c>TokenDisplay.Resolve</c>; with no bearer
/// in hand the token stays at its declared prominence (the caller can't prove it's unread).</para>
/// </param>
public sealed record TokenDefinition(
    string Id,
    string Name,
    EValence Valence,
    ETokenProminence Prominence,
    EValenceSource ValenceSource = EValenceSource.Fixed,
    TokenClearTrigger? DefaultClearTrigger = null,
    string Description = "",
    ETokenColor? ColorOverride = null,
    ETokenShape? ShapeOverride = null,
    bool VisibleOnlyWhenRead = false);
