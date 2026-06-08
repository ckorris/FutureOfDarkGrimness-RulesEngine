using System;
using System.Collections.Generic;

namespace FDG.Presentation.Beats
{
    /// <summary>
    /// Shots/blows that were saved (deflected off armor). The engine resolves saves per AP group,
    /// not per defending model, so this is unit-level: a count of saves plus the defender's model
    /// positions. The front-end shows that many deflection "pings" across the unit.
    /// </summary>
    [Serializable]
    public sealed class SaveBeat : PresentationBeat
    {
        public IReadOnlyList<Position> DefenderPositions { get; }
        public int SavedCount { get; }

        public SaveBeat(IReadOnlyList<Position> defenderPositions, int savedCount)
        {
            DefenderPositions = defenderPositions;
            SavedCount = savedCount;
        }

        public override TimeSpan NominalDuration => PresentationDurations.Saves;

        public override string? Text => null;
    }
}
