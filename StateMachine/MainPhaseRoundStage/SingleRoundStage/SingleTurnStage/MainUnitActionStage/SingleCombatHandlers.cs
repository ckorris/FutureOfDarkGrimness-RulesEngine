using FDG.Stages;

namespace FDG
{
    public class SingleCombatHandlers
    {
        public readonly IAssignWoundsHandler AssignWoundsHandler;

        public SingleCombatHandlers(IAssignWoundsHandler assignWoundsHandler)
        {
            AssignWoundsHandler = assignWoundsHandler;
        }
    }
}
