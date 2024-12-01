using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FDG.Stages
{
    public interface IStateMachineLayer<TContext>
    {
        public void ProcessEvent(string nextStageName, TContext context);
    }
}
