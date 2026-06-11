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

        public ModelWoundedBeat(ModelID model, Position position)
        {
            Model = model;
            Position = position;
        }

        public override TimeSpan NominalDuration => PresentationDurations.ModelWounded;

        // The wound/hit logs already narrate the outcome.
        public override string? Text => null;
    }
}
