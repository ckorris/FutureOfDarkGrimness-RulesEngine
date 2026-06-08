
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
}