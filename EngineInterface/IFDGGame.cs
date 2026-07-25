using FDG.Presentation;
using FDG.StageResolution;
using FDG.StageResolution.Previews;
using FDG.TextInterface;

namespace FDG.EngineInterface
{
    /// <summary>
    /// TODO: I put this in the EngineInterface folder, and it feels like more things could be there.
    /// </summary>
    public interface IFDGGame
    {
        ITableState TableState { get;}

        ILogMessageUI? LogMessageUI { get;}

        IPlayerMessageUI? PlayerMessageUI { get;}

        IStageResolverRegistry? StageResolverRegistry { get; }

        IPresentationSink? PresentationSink { get; }

        /// <summary>
        /// Publish side of live decision-preview sharing (#277): the front end pushes its active
        /// resolver's transient state (ghost models, planned paths) here so other players watch the
        /// decision take shape. Available from construction (property, not AssignInterfaces) - the
        /// bus exists at ctor time, and the GUI wires its publisher separately from the resolver
        /// registry. Publishing with no listeners (headless peers) is harmless.
        /// </summary>
        IPreviewChannel PreviewChannel { get; }

        /// <summary>
        /// Receive side of #277: latest preview state of every other (non-local) player, for the
        /// renderer to draw. Same lifecycle rationale as <see cref="PreviewChannel"/>.
        /// </summary>
        IPreviewFeed PreviewFeed { get; }

        void AssignInterfaces(ILogMessageUI? logMessageUI, IPlayerMessageUI? playerMessageUI,
            IStageResolverRegistry stageResolverRegistry,
            IPresentationSink? presentationSink,
            IOutstandingListDisplay? outstandingTaskDisplay);
    }
}
