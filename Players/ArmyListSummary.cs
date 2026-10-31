
namespace FDG.Players
{
    /// <summary>Lobby-visible digest of a slot's army. <paramref name="GameSystem"/> is the army's OPR
    /// game-system slug (#378) - null means Grimdark Future (pre-#378 files and summaries), letting the
    /// lobby warn when two players bring armies from different systems. <paramref name="RulesVersion"/> (#440)
    /// is the ruleset stamp of the book a Forge-built army embeds ("OPR v3.5.3"), null for a hand-authored list;
    /// it rides the roster so every machine can show the legacy-army notice (LegacyArmyNotice).</summary>
    public record ArmyListSummary(bool IsAssigned, string ArmyName, string FactionName, int PointCost,
        string? GameSystem = null, string? RulesVersion = null);
}
