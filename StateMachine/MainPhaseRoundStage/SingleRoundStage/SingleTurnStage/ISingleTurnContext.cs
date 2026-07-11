
using FDG.Data;

namespace FDG.Stages
{
    public interface ISingleTurnContext : IGameContextAccessor
    {
        public PlayerID ActivatedPlayer { get; }

        public IReadOnlyList<DataBinding<UnitData>> PlayerUnactivatedUnits { get; }

        public DataBinding<UnitData>? ActivatedUnit { get; }

        // #197 Delayed Action: the acting player held a unit back (passed the turn) instead of activating.
        // No unit activated this turn - SingleTurnStage skips MarkUnitAsActivated so the held-back unit
        // stays in the pool, and the cursor advances to the opponent.
        public bool WasDelayed { get; }

        // #197: true when an opposing team has strictly more units left to activate than the acting player's
        // team, the gate Delayed Action rides on. Snapshotted at turn start (pools don't change until a unit
        // is marked activated at turn end), so ChooseUnitToActivateStage can read it while deciding.
        public bool OpponentHasMoreUnitsToActivate { get; }

        public void ChooseUnitToActivate(DataBinding<UnitData> unitToActivate);

        public void MarkTurnDelayed();
    }

    public class SingleTurnContext : ISingleTurnContext
    {
        public IGameContext GameContext { get; }

        public PlayerID ActivatedPlayer { get; }

        public DataBinding<UnitData>? ActivatedUnit { get; private set; }

        public IReadOnlyList<DataBinding<UnitData>> PlayerUnactivatedUnits { get; }

        public bool WasDelayed { get; private set; }

        public bool OpponentHasMoreUnitsToActivate { get; }

        public SingleTurnContext(IGameContext gameContext, PlayerID activatedPlayer,
            List<DataBinding<UnitData>> playerUnactivatedUnits, bool opponentHasMoreUnitsToActivate = false)
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
            OpponentHasMoreUnitsToActivate = opponentHasMoreUnitsToActivate;
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

        public void MarkTurnDelayed()
        {
            WasDelayed = true;
        }
    }
}
