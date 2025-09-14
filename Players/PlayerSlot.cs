
using FDG.Data;
using FDG.SaveLoad;

namespace FDG.Players
{
    public class PlayerSlot// : IPlayerSlotInfo
    {
        public int SlotID { get; }

        public int TeamNumber { get; } //TODO: Kinda wanna use TeamID.

        private const string EMPTY_PLAYER_NAME = "[Empty]";

        public string Name => Controller != null ? Controller.Name : EMPTY_PLAYER_NAME;

        public bool IsFilled => Controller != null;

        public PlayerID PlayerID;

        public readonly ArmyListFile ArmyListFile;

        internal IPlayerController? Controller;

        internal DataBinding<PlayerSlotInfo> _infoBinding;

        public PlayerSlot(int slotID, int teamNumber, PlayerID playerID, ArmyListFile armyListFile, 
            IReadWriteableGameDataStore gameDataStore)
        {
            SlotID = slotID;
            TeamNumber = teamNumber;
            PlayerID = playerID;
            ArmyListFile = armyListFile;

            PlayerSlotInfo info = new PlayerSlotInfo(this);
            DataReference infoReference = gameDataStore.Create(info);
            _infoBinding = gameDataStore.GetDataBinding<PlayerSlotInfo>(infoReference);
        }

        internal void AssignPlayerController(IPlayerController newController)
        {
            if(Controller != null)
            {
                throw new InvalidOperationException($"Tried to fill player slot {SlotID} with a player controller named " +
                    $"{newController.Name}, but that slot was already filled by one named {Controller.Name}.");
            }

            Controller = newController;

            _infoBinding.SetValue(new PlayerSlotInfo(this)); //Update the binding.
        }

        public void ClearPlayerController()
        {
            //Thinking we shouldn't throw an exception if it's not filled to make cleanup easier.
            Controller = null;

            _infoBinding.SetValue(new PlayerSlotInfo(this)); //Update the binding.
        }

    }
}
