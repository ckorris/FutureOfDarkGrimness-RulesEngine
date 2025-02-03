using FDG.Network.Messages;

namespace FDG.Network.Connection.Lobby
{
    public interface ILobbyViewModel : IDisposable
    {
        bool HasHostPrivileges { get; }

        event Action? OnLaunched; //Need arguments?

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
