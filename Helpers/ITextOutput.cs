
namespace FDG
{

    public interface ITextOutput
    {
        // color defaults to white when null, so existing callers (Log("…")) keep compiling.
        public void Log(string message, TextColor? color = null);
    }



    public class EmptyTextOutput : ITextOutput
    {
        public void Log(string message, TextColor? color = null)
        {
            //Purposefully doing nothing.
        }
    }


    /// <summary>
    /// Writes to the real console (unlike <see cref="System.Diagnostics.Debug"/>, this survives Release builds).
    /// Default sink for the network/bus layer, which sits below where a game-level <see cref="ITextOutput"/> is plumbed.
    /// </summary>
    public class ConsoleTextOutput : ITextOutput
    {
        public void Log(string message, TextColor? color = null)
        {
            Console.WriteLine(message);
        }
    }
}