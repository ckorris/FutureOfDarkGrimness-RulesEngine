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

        IObservable<string> ServerName { get; }

        IObservable<LobbyChatMessage> ChatMessages { get; }

        IObservable<IReadOnlyList<LobbyPlayerInfo>> PlayerInfos { get; }

        IObservable<int> Settings_ArmyPoints { get; }
        IObservable<int> Settings_TerrainPieceCount { get; }
        IObservable<ERandomnessType> Settings_RandomnessType { get; }
        IObservable<ETurnStyle> Settings_TurnStyle { get; }

        void SendMessage(string message);

        void SetArmyPoints(int armyPoints);

        void SetTerrainCount(int terrainCount);

        void SetRandomnessType(ERandomnessType randomnessType);

        void SetTurnStyle(ETurnStyle turnStyle);

        bool TryLaunchGame(out string? failReason);
    }
}
