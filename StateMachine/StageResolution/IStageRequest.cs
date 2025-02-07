using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FDG.StateMachine.StageResolution
{
    public interface IStageRequest<TReply>
    {
        Task<TReply> Resolve(TReply resolution);
    }
}
