using System;

namespace FDG.Presentation.Beats
{
    /// <summary>
    /// A model took a wound but survived (it has more wounds left — e.g. Tough). The front-end gives
    /// it a brief hurt flinch, distinct from the death animation; the model stays on the table.
    /// </summary>
    [Serializable]
    public sealed class ModelWoundedBeat : PresentationBeat
    {
        public ModelID Model { get; }
        public Position Position { get; }

        /// <summary>#232 casualty cascade - same contract as <see cref="ModelDiedBeat.Overlap"/>:
        /// pace only the stagger, the flinch plays out concurrently with the next casualty.</summary>
        public bool Overlap { get; }

        public ModelWoundedBeat(ModelID model, Position position, bool overlap = false)
        {
            Model = model;
            Position = position;
            Overlap = overlap;
        }

        public override TimeSpan NominalDuration => PresentationDurations.ModelWounded;
        public override bool Held => Overlap;
        public override TimeSpan HoldLeadIn => PresentationDurations.CasualtyStagger;

        // The wound/hit logs already narrate the outcome.
        public override string? Text => null;
    }
}
