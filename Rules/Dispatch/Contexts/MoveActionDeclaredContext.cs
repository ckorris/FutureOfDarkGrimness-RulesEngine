using System;
using System.Collections.Generic;
using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// Fires at <see cref="EHookID.Movement_OnMoveActionDeclared"/>: the player has
    /// chosen Advance/Rush/Charge and the distance budget is being computed. Carries
    /// the declared <see cref="ActionType"/> and the unmodified base distance so
    /// movement-bonus rules (Fast, Slow, Rapid Rush) can adjust it.
    /// </summary>
    /// <param name="TerrainPieces">Table terrain for terrain-proximity conditions (#376 Grounded
    /// Speed). Null/empty on paths that cannot supply it; see <see cref="IHasTerrain"/>.</param>
    public sealed record MoveActionDeclaredContext(
        IUnit Unit, EActionType ActionType, float BaseDistanceInches,
        IReadOnlyList<ITerrain>? TerrainPieces = null) : IHookContext, IHasActionType, IHasTerrain
    {
        public EHookID Hook => EHookID.Movement_OnMoveActionDeclared;

        public IReadOnlyList<ITerrain> Terrain => TerrainPieces ?? Array.Empty<ITerrain>();
    }
}
