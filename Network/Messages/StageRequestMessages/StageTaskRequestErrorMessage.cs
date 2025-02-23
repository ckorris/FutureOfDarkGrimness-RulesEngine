using FDG.StageResolution;

namespace FDG.Network.Messages.StageRequestMessages
{
    public class StageTaskRequestErrorMessage
    {
        public PlayerID PlayerID;

        public TaskID TaskID;

        public string ErrorMessage;

        public StageTaskRequestErrorMessage(PlayerID playerID, TaskID taskID, string errorMessage)
        {
            PlayerID = playerID;
            TaskID = taskID;
            ErrorMessage = errorMessage;
        }
    }
}
