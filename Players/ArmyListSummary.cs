
namespace FDG.Players
{
    /// <summary>Lobby-visible digest of a slot's army. <paramref name="GameSystem"/> is the army's OPR
    /// game-system slug (#378) - null means Grimdark Future (pre-#378 files and summaries), letting the
    /// lobby warn when two players bring armies from different systems.</summary>
    public record ArmyListSummary(bool IsAssigned, string ArmyName, string FactionName, int PointCost,
        string? GameSystem = null);
}
