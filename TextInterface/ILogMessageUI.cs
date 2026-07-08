
namespace FDG.TextInterface
{
    /// <summary>
    /// Interface intended for UI that displays log messages. To be implemented by the engine.
    /// </summary>
    public interface ILogMessageUI
    {
        void DisplayLogMessage(string message, TextColor color);

        // Developer-facing detail line. Defaults to a normal log line for sinks that don't separate it
        // (e.g. the headless console); the GUI overrides it to route to a hidden-by-default Debug view.
        void DisplayDebugMessage(string message, TextColor color) => DisplayLogMessage(message, color);
    }
}
