namespace FDG.Rules.Definitions;

/// <summary>
/// Which terrain movement effect a <see cref="Effect.CountAsInTerrain"/> bearer suffers on its move,
/// regardless of the table's actual terrain. Deliberately narrower than <c>ETerrainType</c>: only the
/// two effects the "counts as being in X Terrain" rule family (#153 spell corpus) confers — the
/// difficult-terrain move cap and the dangerous-terrain per-model wound roll.
/// </summary>
public enum ECountAsTerrain
{
    /// <summary>The move is capped at the difficult-terrain limit, as if crossing Difficult terrain.</summary>
    Difficult = 1,

    /// <summary>The moving unit takes the Dangerous terrain test after the move - a d6 per wound it would
    /// take to destroy it, a wound to the unit on each 1 (3.6, #420) - as if crossing Dangerous terrain.</summary>
    Dangerous = 2,
}
