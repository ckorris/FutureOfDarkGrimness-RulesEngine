
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

        /// <summary>
        /// The list this slot launches with, or null on a RESUME - where the armies are already in the
        /// loaded store and the slot's file is vestigial (see FDGServer's resume ctor). A fresh launch
        /// always has one: <see cref="GameBootstrap.CreateArmy"/> refuses a slot without it rather than
        /// inventing a placeholder, which is how a game used to start with an army nobody picked (#400).
        /// </summary>
        public readonly ArmyListFile? ArmyListFile;

        internal IPlayerController? Controller;

        internal DataBinding<PlayerSlotInfo> InfoBinding;

        public PlayerSlot(int slotID, int teamNumber, PlayerID playerID, ArmyListFile? armyListFile,
            IReadWriteableGameDataStore gameDataStore)
        {
            SlotID = slotID;
            TeamNumber = teamNumber;
            PlayerID = playerID;
            ArmyListFile = armyListFile;

            PlayerSlotInfo info = new PlayerSlotInfo(this);
            DataReference infoReference = gameDataStore.Create(info);
            InfoBinding = gameDataStore.GetDataBinding<PlayerSlotInfo>(infoReference);
        }

        public void AssignPlayerController(IPlayerController newController)
        {
            if(Controller != null)
            {
                throw new InvalidOperationException($"Tried to fill player slot {SlotID} with a player controller named " +
                    $"{newController.Name}, but that slot was already filled by one named {Controller.Name}.");
            }

            Controller = newController;

            InfoBinding.SetValue(new PlayerSlotInfo(this)); //Update the binding.
        }

        public void ClearPlayerController()
        {
            //Thinking we shouldn't throw an exception if it's not filled to make cleanup easier.
            Controller = null;

            InfoBinding.SetValue(new PlayerSlotInfo(this)); //Update the binding.
        }

    }
}
