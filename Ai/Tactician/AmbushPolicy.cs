using FDG.Data;
using FDG.Rules.Dispatch;

namespace FDG.Ai.Tactician
{
    /// <summary>
    /// Hold-or-deploy policy for Ambush-capable units (#191 A5-2, plan: "simple round/threat
    /// heuristics instead of always deploy normally"). The solo bot never holds, so Ambush was
    /// dead weight in every list carrying it. Heuristic: a unit gains from teleporting when its
    /// weapons only work up close - melee and short-range profiles skip their weakest phase (the
    /// approach march under fire); long-range units deploy normally, because holding them trades
    /// away a full round of shooting. Arrival timing stays the engine default (first opportunity,
    /// round 2) - deferring arrival is a search-level judgment (Phase B).
    /// </summary>
    public static class AmbushPolicy
    {
        /// <summary>A unit whose best weapon reaches this far deploys normally and shoots instead.</summary>
        public const float ShortRangeThresholdInches = 18f;

        /// <summary>
        /// True when the named unit should be held in Ambush. At most half the army's living units
        /// are ever held, so the table is not conceded in round 1. The unit arrives by NAME (the
        /// hold prompt carries nothing else); same-named units share a loadout in practice, so the
        /// first live match decides for all of them.
        /// </summary>
        public static bool ShouldHold(ITableState tableState, PlayerID player, string unitName)
        {
            ArmyData? army = SpellValuation.ArmyOf(tableState, player);
            if (army == null) return false;

            DataBinding<UnitData>? unit = null;
            int living = 0, held = 0;
            foreach (DataBinding<UnitData> binding in army.UnitBindings)
            {
                UnitData candidate = binding.GetValue();
                if (!candidate.GetIsAlive()) continue;
                living++;
                if (ReserveRules.IsInReserve(candidate)) held++;
                if (unit == null && candidate.Name == unitName) unit = binding;
            }
            if (unit == null) return false;           // cannot identify it: deploy normally (solo)
            if ((held + 1) * 2 > living) return false; // never hold more than half the army

            float maxRange = 0f;
            foreach (IModel model in unit.GetValue().Models)
            {
                if (!model.GetIsAlive()) continue;
                foreach (Weapon weapon in model.Weapons)
                    if (weapon.RangeInches > maxRange) maxRange = weapon.RangeInches;
            }
            return maxRange < ShortRangeThresholdInches;
        }
    }
}
