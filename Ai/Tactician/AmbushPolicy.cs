using FDG.Data;

namespace FDG.Ai.Tactician
{
    /// <summary>
    /// Hold-or-deploy policy for Ambush-capable units (#191 A5-2, revised A5-8). Everything with
    /// Ambush holds, always (Chris): round 1 is positioning with the occasional pot shot, and
    /// Ambush places a unit exactly where it is needed with zero marching - free movement a Slow
    /// army especially cannot buy any other way. The old heuristic (hold only melee/short-range
    /// profiles, cap at half the army) left long-range Ambushers like RL's Forge Spider walking
    /// on at round 1 in every game. Arrival timing stays the engine default (the round-2+ YesNo
    /// arrival prompt defaults to deploy, so units arrive at the first opportunity); deferring
    /// arrival past that is a search-level judgment (Phase B).
    /// </summary>
    public static class AmbushPolicy
    {
        /// <summary>True: a unit that CAN start in Ambush always does.</summary>
        public static bool ShouldHold(ITableState tableState, PlayerID player, string unitName)
            => true;
    }
}
