namespace FDG.Rules.Definitions
{
    /// <summary>
    /// How much terrain a movement-terrain-ignore rule waives (#102 Strider / #029 Flying). Carried by
    /// <see cref="Effect.IgnoreTerrainEffects"/> / <see cref="RuleOperation.IgnoreTerrainEffects"/> so the same
    /// op can express both rules: Strider waives only the difficult-terrain move cap, while Flying additionally
    /// waives Dangerous-terrain wound rolls and Impassible-terrain blocking (and moves through units via the
    /// separate <see cref="RuleOperation.IgnoreEnemyMovementBlock"/>).
    /// </summary>
    public enum ETerrainIgnoreScope
    {
        /// <summary>Strider — waive only the difficult-terrain movement cap.</summary>
        DifficultOnly,

        /// <summary>Flying — waive ALL terrain movement effects (difficult cap, Dangerous wounds, Impassible block).</summary>
        AllTerrain,
    }
}
