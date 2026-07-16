using FDG.Players;

namespace FDG.Network.Connection.Lobby
{
    /// <param name="ColorIndex">The player's explicit lobby colour pick (#221) as an index into the
    /// front end's palette, or -1 for no pick (the front end assigns a default). The engine treats it
    /// as an opaque int - the palette itself is app-side.</param>
    public record LobbyPlayerInfoSummary(string PlayerName, ArmyListSummary ArmyListSummary, ETeamOption TeamNumber,
        EPlayerType PlayerType, ConnectionID connectionID, PlayerID PlayerID, int ColorIndex = -1);
}
