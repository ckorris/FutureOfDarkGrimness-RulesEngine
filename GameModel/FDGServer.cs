using FDG.Data;
using FDG.Network.Connection;
using FDG.Network.Synchronization;

namespace FDG.GameModel
{
    public class FDGServer
    {
        private IReadWriteableGameDataStore _gameDataStore;
        private FDGHost _host;
        private GameDataUpdateSender _synchronizer;

        public FDGServer(IReadWriteableGameDataStore gameDataStore, FDGHost fdgHost)
        {
            _gameDataStore = gameDataStore;
            _host = fdgHost;
            _synchronizer = new GameDataUpdateSender(gameDataStore, fdgHost);

            //For players/player slots, work backwards from here to create what you need to send updates to players,
            //then what you need to make that thing, then what you need for that, etc. until you lead back to these args.

            //TODO: Not implementing the state machine just yet.


            //For test, make a thing. 
            LoadTestData();
        }

        private void LoadTestData()
        {
            float baseRadiusInches = 0.75f;
            List<Weapon> weapons = new List<Weapon>() { new Weapon("Weapon 1", 6, 2, 1, new HashSet<ISpecialRule_Weapon>()) };
            List<SpecialRule> specialRules = new List<SpecialRule>() { new Rending() };

            int perTeamModelCount = 5;
            float spacing = 2.5f;

            float startX = GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES / 2f - (perTeamModelCount / 2f * spacing);
            float team1StartY = GameWideConstants.DEPLOYMENT_DISTANCE_INCHES;
            float team2StartY = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES - GameWideConstants.DEPLOYMENT_DISTANCE_INCHES;

            //Team 1.
            for (int i = 0; i < perTeamModelCount; i++)
            {
                Position position = new Position(startX + i * spacing, team1StartY);

                ModelData modelData = new ModelData(baseRadiusInches, weapons, specialRules, position, _gameDataStore);
                _gameDataStore.Create(modelData);
            }

            //Team 2.
            for (int i = 0; i < perTeamModelCount; i++)
            {
                Position position = new Position(startX + i * spacing, team2StartY);

                ModelData modelData = new ModelData(baseRadiusInches, weapons, specialRules, position, _gameDataStore);
                _gameDataStore.Create(modelData);
            }
        }
    }
}
