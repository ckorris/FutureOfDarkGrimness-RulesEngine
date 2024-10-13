using System;

namespace FDG.Stages
{
    public interface IExitOnlyHandler<TStateContext>
    {
        public void Handle(TStateContext context, Action exitStage);
    }
}