using System;

namespace FDG.Samples
{
    public class BasicConsoleLogger : ITextOutput
    {
        public void Log(string message)
        {
            Console.WriteLine(message);
        }
    }
}
