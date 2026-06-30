using FDG.Rules.Foundation;

namespace FDG.Rules.Tokens;

/// <summary>
/// Display-ready, semantic view of one token — what the engine hands the GUI (#151). The engine decides
/// everything here (identity, name, hover text, valence, prominence); the app turns it into pixels
/// (curated palette, hash → color/shape, layout, count badge, tooltip). No Raylib, no RGB. Produced by
/// <c>TokenDisplay.Resolve</c>.
/// </summary>
/// <param name="DisplayId">Stable identity used to hash a color/shape (granted-rule name for grants, else the type id).</param>
/// <param name="Name">Short human label (e.g. "Shaken", "+1 to Hit").</param>
/// <param name="Description">Full hover line (e.g. "+1 to Hit rolls, this round.").</param>
/// <param name="ColorOverride">Authored color, or null to let the app derive one from <see cref="DisplayId"/>.</param>
/// <param name="ShapeOverride">Authored shape, or null to let the app derive one from <see cref="DisplayId"/>.</param>
/// <param name="IsModelScoped">Drawn over a model (true) vs under the unit name (false).</param>
/// <param name="OwnerUnitID">The unit that placed this token, for hover ("placed by …"); null if self-placed.</param>
public sealed record TokenDisplayInfo(
    string DisplayId,
    string Name,
    string Description,
    EValence Valence,
    ETokenProminence Prominence,
    int Count,
    bool IsModelScoped,
    string LifetimeText,
    ETokenColor? ColorOverride = null,
    ETokenShape? ShapeOverride = null,
    UnitID? OwnerUnitID = null);
