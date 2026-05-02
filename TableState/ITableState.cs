

using FDG.Data;
using FDG.Players;
using FutureOfDarkGrimness.TableState;

namespace FDG
{
    public interface ITableState
    {
        public IDataState<IPlayerSlotInfo> Players { get; }

        public IDataState<ITeam> Teams { get; }

        public IDataState<IArmy> Armies { get; }

        public IDataState<IUnit> Units { get; }

        public IDataState<IModel> Models { get; }

        public IDataState<ITerrain> Terrain { get; }

    }

    public class TableState : ITableState
    {
        //public IDataState<IPlayerInfo> Players { get;
        public IDataState<IPlayerSlotInfo> Players { get; }
        
        public IDataState<ITeam> Teams { get; }
        
        public IDataState<IArmy> Armies { get; }

        public IDataState<IUnit> Units { get; }

        public IDataState<IModel> Models { get; }

        public IDataState<ITerrain> Terrain { get; }


        public TableState(IReadableGameDataStore gameDataStore)
        {
            //Players = new DataState<IPlayerInfo, PlayerData>(gameDataStore);
            Players = new DataState<IPlayerSlotInfo, PlayerSlotInfo>(gameDataStore);
            Units = new DataState<IUnit, UnitData>(gameDataStore);
            Models = new DataState<IModel, ModelData>(gameDataStore);
            Armies = new DataState<IArmy, ArmyData>(gameDataStore);
            Teams = new DataState<ITeam, TeamData>(gameDataStore);
            Terrain = new DataState<ITerrain, TerrainData>(gameDataStore);
        }
    }
}
