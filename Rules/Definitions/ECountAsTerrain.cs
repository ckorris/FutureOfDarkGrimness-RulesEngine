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

    /// <summary>Each moving model rolls a d6 after the move, taking a wound on a 1, as if crossing
    /// Dangerous terrain.</summary>
    Dangerous = 2,
}
