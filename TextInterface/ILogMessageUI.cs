
namespace FDG.TextInterface
{
    /// <summary>
    /// Interface intended for UI that displays log messages. To be implemented by the engine.
    /// </summary>
    public interface ILogMessageUI
    {
        void DisplayLogMessage(string message, TextColor color);
    }
}
