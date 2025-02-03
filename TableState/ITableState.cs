

using FDG.Data;
using FutureOfDarkGrimness.TableState;

namespace FDG
{
    public interface ITableState
    {
        public IDataState<IPlayerInfo> Players { get; }

        public IDataState<IUnit> Units { get; }

        public IDataState<IModel> Models { get; }

        public IDataState<IArmy> Armies { get; }

        public IDataState<ITerrain> Terrain { get; }
    }

    public class TableState : ITableState
    {
        public IDataState<IPlayerInfo> Players { get; }

        public IDataState<IUnit> Units { get; }

        public IDataState<IModel> Models { get; }

        public IDataState<IArmy> Armies { get; }

        public IDataState<ITerrain> Terrain { get; }



        public TableState(IReadableGameDataStore gameDataStore)
        {
            Players = new DataState<IPlayerInfo, PlayerData>(gameDataStore);
            Units = new DataState<IUnit, UnitData>(gameDataStore);
            Models = new DataState<IModel, ModelData>(gameDataStore);
            Armies = new DataState<IArmy, ArmyData>(gameDataStore);
            Terrain = new DataState<ITerrain, Terrain>(gameDataStore);
        }
    }
}
