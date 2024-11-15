

using System.Runtime.CompilerServices;

namespace FDG.Stages
{

    public interface IUnitActionContext : IGameContextAccessor
    {

    }

    public class UnitActionContext : IUnitActionContext
    {
        public IGameContext GameContext { get; private set; }


        public UnitActionContext(IGameContext gameContext)
        {
            GameContext = gameContext;
        }
    }
}