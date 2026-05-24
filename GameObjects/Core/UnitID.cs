using System;

namespace FDG
{
    /// <summary>
    /// Stable per-unit identifier, opaque outside the engine. Wraps a Guid for
    /// type-safety so unit identifiers can't be confused with <see cref="PlayerID"/>
    /// or other Guid-shaped values.
    ///
    /// Distinct from a unit's <see cref="FDG.Data.DataReference"/> in
    /// <see cref="FDG.Data.GameDataStore"/> — the DataReference identifies a slot in
    /// the local store and is meaningful only to the engine, whereas UnitID is a
    /// stable identity the rule system uses to track ownership of cross-unit tokens,
    /// re-link saved tokens on load, etc.
    /// </summary>
    public readonly record struct UnitID(Guid ID);
}
