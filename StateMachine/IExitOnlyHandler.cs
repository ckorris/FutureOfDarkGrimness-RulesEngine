using System;

namespace FDG.StateMachine
{
    public interface IExitOnlyHandler<TStateContext>
    {
        public void Handle(TStateContext context, Action exitStage);
    }
}