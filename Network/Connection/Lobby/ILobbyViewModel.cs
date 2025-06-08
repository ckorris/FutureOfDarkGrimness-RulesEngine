using FDG.EngineInterface;
using FDG.Network.Messages;

namespace FDG.Network.Connection.Lobby
{
    public interface ILobbyViewModel : IDisposable
    {
        bool HasHostPrivileges { get; }

        /// <summary>
        /// The first parameter is the instance that you can use to bind your entire game view to.
        /// The second is a request to provide a reference to a <see cref="StageResolution.StageResolverRegistry"/>, 
        /// and you must fulfill this action before you can play the game.
        /// </summary>
        event Action<IFDGGame>? OnLaunched; //Need arguments?

        IObservable<string> ServerNameObservable { get; }

        IObservable<LobbyChatMessage> ChatMessagesObservable { get; }

        IObservable<IReadOnlyList<LobbyPlayerInfo>> PlayerInfosObservable { get; }

        IObservable<int> ArmyPointsObservable { get; }
        IObservable<int> TerrainPieceCountObservable { get; }
        IObservable<ERandomnessType> RandomnessTypeObservable { get; }
        IObservable<ETurnStyle> TurnStyleObservable { get; }

        string ServerName { get; }

        IReadOnlyList<LobbyChatMessage> ChatMessages { get; }

        IReadOnlyList<LobbyPlayerInfo> PlayerInfos { get; }

        int ArmyPoints { get; }

        int TerrainCount { get; }

        ERandomnessType RandomnessType { get; }

        ETurnStyle TurnStyle { get; }


        void SendMessage(string message);

        void SetArmyPoints(int armyPoints);

        void SetTerrainCount(int terrainCount);

        void SetRandomnessType(ERandomnessType randomnessType);

        void SetTurnStyle(ETurnStyle turnStyle);

        bool TryLaunchGame(out string? failReason);
    }
}
