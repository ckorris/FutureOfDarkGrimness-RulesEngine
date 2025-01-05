using FDG.Stages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FDG.StateMachine
{
    public interface IStateMachine
    {
        IReadOnlyList<IStage> ActiveStages { get; }
    }
}
