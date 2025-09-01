
using FDG.Data;

namespace FDG.Stages
{
    public interface ISingleTurnContext : IGameContextAccessor
    {
        public PlayerID ActivatedPlayer { get; }

        public IReadOnlyList<DataBinding<UnitData>> PlayerUnactivatedUnits { get; } 

        public DataBinding<UnitData>? ActivatedUnit { get; }

        public void ChooseUnitToActivate(DataBinding<UnitData> unitToActivate);
    }

    public class SingleTurnContext : ISingleTurnContext
    {
        public IGameContext GameContext { get; }

        public PlayerID ActivatedPlayer { get; }

        public DataBinding<UnitData>? ActivatedUnit { get; private set; }

        public IReadOnlyList<DataBinding<UnitData>> PlayerUnactivatedUnits { get; }

        public SingleTurnContext(IGameContext gameContext, PlayerID activatedPlayer, List<DataBinding<UnitData>> playerUnactivatedUnits)
        {
            foreach(DataBinding<UnitData> unit in playerUnactivatedUnits)
            {
                if(unit.GetValue().PlayerID != activatedPlayer)
                {
                    throw new ArgumentException($"Passed in a unit for a player that didn't belong to that player. Unit: {unit.GetValue().Name} " +
                        $"Owning player: {unit.GetValue().PlayerID} Activated player: {activatedPlayer}.");
                }
            }

            GameContext = gameContext;
            ActivatedPlayer = activatedPlayer;
            PlayerUnactivatedUnits = playerUnactivatedUnits;
        }

        public void ChooseUnitToActivate(DataBinding<UnitData> unitToActivate)
        {
            if(PlayerUnactivatedUnits.Contains(unitToActivate) == false)
            {
                throw new ArgumentOutOfRangeException("Tried to activate unit that wasn't in the list of available units: " +
                    $"{unitToActivate.GetValue().Name}. Remaining units: {PlayerUnactivatedUnits.Count}");
            }

            ActivatedUnit = unitToActivate;
        }
    }
}
