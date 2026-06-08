using System.Diagnostics;

namespace FDG.Samples
{
    public class BasicConsoleLogger : ITextOutput
    {
        public void Log(string message, TextColor? color = null)
        {
            Debug.WriteLine(message);
        }
    }
}
