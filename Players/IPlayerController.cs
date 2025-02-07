using FDG.StateMachine.StageResolution;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FDG.Players
{
    internal interface IPlayerController : IPlayerInfo
    {
        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageRequest<TReply>;
    }
}
