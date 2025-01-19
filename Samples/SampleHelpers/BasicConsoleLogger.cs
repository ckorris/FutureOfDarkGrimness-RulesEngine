using System.Diagnostics;

namespace FDG.Samples
{
    public class BasicConsoleLogger : ITextOutput
    {
        public void Log(string message)
        {
            Debug.WriteLine(message);
        }
    }
}
