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

        void SendMessage(string message);

        bool TryLaunchGame(out string? failReason);

        
    }
}
