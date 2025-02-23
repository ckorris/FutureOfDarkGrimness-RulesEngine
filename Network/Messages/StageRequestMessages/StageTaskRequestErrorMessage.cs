using FDG.StageResolution;
using FDG;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FutureOfDarkGrimness.Network.Messages.StageRequestMessages
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
