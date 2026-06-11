using System;

namespace FDG
{
    /// <summary>
    /// Stable per-model identifier, opaque outside the engine. Wraps a Guid for
    /// type-safety so model identifiers can't be confused with <see cref="UnitID"/>,
    /// <see cref="PlayerID"/>, or other Guid-shaped values.
    ///
    /// Distinct from a model's <see cref="FDG.Data.DataReference"/> in
    /// <see cref="FDG.Data.GameDataStore"/> — the DataReference identifies a slot in
    /// the local store and is meaningful only to the engine, whereas ModelID is a
    /// stable identity that survives JSON / network round-trips. Mirrors
    /// <see cref="UnitID"/>; used to name a specific model across the wire (e.g. the
    /// presentation-beat stream referring to "this model moved" / "this model died").
    /// </summary>
    public readonly record struct ModelID(Guid ID);
}
