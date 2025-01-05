
namespace FDG
{

    public interface ITextOutput
    {
        public void Log(string message);
    }



    public class EmptyTextOutput : ITextOutput
    {
        public void Log(string message)
        {
            //Purposefully doing nothing.
        }
    }
}