

using FDG.Data;
using FDG.Players;

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

        public IDataState<IObjective> Objectives { get; }

        /// <summary>
        /// Aggregate read model of the game's progression -- round number, objective scores, the unit
        /// currently activating, etc. -- as live state read each frame. A single object so new
        /// progression facts can be added to <see cref="IGameProgress"/> without changing this surface.
        /// Read it instead of parsing round info out of the log or banner stream.
        /// </summary>
        public IGameProgress Progress { get; }

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

        public IDataState<IObjective> Objectives { get; }

        public IGameProgress Progress { get; }


        public TableState(IReadableGameDataStore gameDataStore)
        {
            //Players = new DataState<IPlayerInfo, PlayerData>(gameDataStore);
            Players = new DataState<IPlayerSlotInfo, PlayerSlotInfo>(gameDataStore);
            Units = new DataState<IUnit, UnitData>(gameDataStore);
            Models = new DataState<IModel, ModelData>(gameDataStore);
            Armies = new DataState<IArmy, ArmyData>(gameDataStore);
            Teams = new DataState<ITeam, TeamData>(gameDataStore);
            Terrain = new DataState<ITerrain, TerrainData>(gameDataStore);
            Objectives = new DataState<IObjective, ObjectiveData>(gameDataStore);
            Progress = new GameProgress(gameDataStore);
        }
    }
}
