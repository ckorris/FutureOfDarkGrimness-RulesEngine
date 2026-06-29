using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// Fires at <see cref="EHookID.Movement_OnMoveThroughTerrain"/>: a capability read for whether the
    /// moving unit ignores terrain movement effects (Strider waives the difficult-terrain move cap; a
    /// future Flying rule ignores all terrain). Mirrors <see cref="MoveThroughEnemyContext"/> — it carries
    /// only the mover, not a specific terrain piece, because <see cref="MovementRuleQueries.IgnoresDifficultTerrain"/>
    /// asks "does this unit ignore difficult terrain at all", evaluated once per move rather than per piece.
    /// </summary>
    public sealed record MoveThroughTerrainContext(IUnit Unit) : IHookContext, IHasActingUnit
    {
        public EHookID Hook => EHookID.Movement_OnMoveThroughTerrain;

        public IUnit ActingUnit => Unit;
    }
}
