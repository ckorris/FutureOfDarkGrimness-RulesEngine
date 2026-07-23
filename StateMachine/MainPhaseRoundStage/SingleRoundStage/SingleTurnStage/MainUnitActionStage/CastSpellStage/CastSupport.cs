using System.Collections.Generic;
using FDG.Data;
using FDG.Players;
using FDG.Utilities;

namespace FDG.Stages
{
    /// <summary>
    /// #197 P23 — the scan the casting-support rules share: living, on-table, friendly units OTHER than the
    /// caster, each with its distance from the caster.
    ///
    /// <para>Both support rules in the corpus are worded the same way ("... from other friendly units within
    /// 12\" ..."), and both gate on it identically, so <c>SpellPurse</c> (Spell Accumulator) and
    /// <c>SpellRelay</c> (Spell Conduit) share the eligibility test rather than each re-deriving what
    /// "other friendly unit in range" means. Range itself is NOT applied here: each rule carries its own
    /// range on the capability answer, so the caller compares.</para>
    /// </summary>
    internal static class CastSupport
    {
        internal readonly record struct Neighbour(IUnit Unit, float DistanceInches);

        internal static IEnumerable<Neighbour> NeighboursOf(ITableState tableState, IUnit caster)
        {
            foreach (IUnit other in tableState.Units.Objects)
            {
                if (ReferenceEquals(other, caster) || other.ID.Equals(caster.ID)) continue;
                if (!other.GetIsAlive() || !other.GetIsOnBattlefield()) continue;
                if (!IsFriendly(tableState, caster.PlayerID, other.PlayerID)) continue;

                yield return new Neighbour(other, UnitCompareUtilities.MinDistanceBetweenUnits(
                    caster, other, out _, out _, includeVertical: true));
            }
        }

        // Team-aware friendliness, mirroring SpellTargeting and SpellValuation: teammates count as friendly;
        // with no registered team, only the player itself does.
        private static bool IsFriendly(ITableState tableState, PlayerID player, PlayerID other)
        {
            foreach (ITeam team in tableState.Teams.Objects)
            {
                if (team.IsPlayerOnTeam(player)) return team.IsPlayerOnTeam(other);
            }

            return player == other;
        }
    }
}
