using System;
using FDG.StageResolution;

namespace FDG.Stages
{
    /// <summary>
    /// Thrown when the response to a <see cref="IStageTaskRequest"/> was invalid in terms
    /// of the game state, such as specifying a move for a unit that's out of cohesion.
    /// </summary>
    public class RequestResponseInvalidException : Exception
    {
        public RequestResponseInvalidException(string message)
            : base(message) { }
    }
}
