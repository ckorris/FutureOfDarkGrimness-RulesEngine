
using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.Rules.Tokens;

namespace FDG
{
    public interface IUnit : IPlayerOwnable
    {
        /// <summary>
        /// Stable per-unit identifier, used by the rule system to track cross-unit
        /// token ownership and to re-link saved state on load. Assigned at unit
        /// creation; survives JSON / network round-trips.
        /// </summary>
        public UnitID ID { get; }

        /// <summary>
        /// Per-unit token container holding rule-system state (cost gates, status
        /// conditions, stacking markers, target tags, per-activation effects).
        /// Tokens survive JSON / network round-trips; subscriptions to the
        /// container's events do not and must be re-attached after rehydration.
        /// </summary>
        public ITokenContainer Tokens { get; }

        public string Name { get; }

        public int Quality { get; }

        public int Defense { get; }

        /// <summary>
        /// How many wounds the unit had remaining when created.
        /// </summary>
        public float MaxWounds { get; }

        /// <summary>
        /// How many wounds remain before the unit is killed.
        /// </summary>
        public float RemainingWounds { get; }

        public List<IModel> Models { get; }

        /// <summary>
        /// Special-rule definitions attached to this unit under the #042 rule
        /// framework, each paired (via <see cref="ResolvedRule"/>) with the name
        /// it was requested under so alias display ("Healing Pods (Regeneration)")
        /// survives. The hook bus reads this to find a unit's passive hook entries
        /// and activated abilities at dispatch time.
        ///
        /// Not serialized: a unit's rules are resolved from army-list rule *names*
        /// against the host registry at load (see #042 arch notes), so the names —
        /// not the full definitions — are the persisted form.
        /// </summary>
        public IReadOnlyList<ResolvedRule> RuleDefinitions { get; }

        /// <summary>
        /// The model ID of a joined Hero (#006) fighting as part of this unit, or null for a plain unit.
        /// A joined hero carries its OWN special rules (relocated onto that model at merge) and does NOT
        /// inherit the host unit's rules, so per-model rule checks (#093) use this to distinguish a native
        /// model — which shares the unit's rules — from the joined hero.
        /// </summary>
        public ModelID? JoinedHeroModelId { get; }

        public bool GetMobility(out float moveShootDistanceInches, out float chargeDistanceInches);

        public event DataValueChangedHandler<float> OnWoundsDealt;
    }

    
    public static class IUnitExtensions
    {
        public static bool GetIsAlive(this IUnit unit)
        {
            return unit.RemainingWounds > 0;
        }

        public static bool GetIsDead(this IUnit unit)
        {
            return unit.RemainingWounds <= 0;
        }

        /// <summary>
        /// Whether the unit is at half strength or less, the threshold the morale rules use to
        /// decide Rout vs. Shaken (and the wound-driven morale trigger). The measure is
        /// shape-dependent, matching the GDF rulebook:
        /// <list type="bullet">
        /// <item>Multi-model units: by model count — half or more of the *starting* models are dead.
        /// Dead models are retained in <see cref="IUnit.Models"/>, so the starting count is the list
        /// size and the living count is the alive subset.</item>
        /// <item>Single-model units: by wounds — remaining wounds are at or below half of
        /// <see cref="IUnit.MaxWounds"/> (which is Tough-aware, set at unit creation).</item>
        /// </list>
        /// A plain wound-sum would misjudge multi-model units whose models have Tough &gt; 1, hence
        /// the branch on model count.
        /// </summary>
        public static bool GetIsAtHalfStrength(this IUnit unit)
        {
            if (unit.Models.Count == 1)
            {
                return unit.RemainingWounds * 2f <= unit.MaxWounds;
            }

            int startingModels = unit.Models.Count;
            int livingModels = unit.Models.Count(model => model.GetIsAlive());
            return livingModels * 2 <= startingModels;
        }

        /// <summary>
        /// Whether this unit is in play: alive, and neither held off the table nor tucked inside something.
        /// The single gate for activation, targeting, line of sight, and drawing.
        ///
        /// Off-table state is explicit, and each kind of it owns a token: a unit held for a later-round
        /// arrival carries <see cref="Rules.Foundation.TokenType.InReserve"/>, one riding a transport
        /// carries <c>EmbarkedIn</c>, and an Aircraft that flew past the edge carries
        /// <c>OffTableFromForcedMove</c>. Those checks come first and are authoritative.
        ///
        /// The origin check that follows is the last resort, for a unit that has simply never been placed
        /// (mid-deployment, before its models get positions). It used to be the ONLY check, which made
        /// "off-table" a property of a coordinate rather than of the unit - so anything that wrote a
        /// reserve model's position promoted it to deployed, and a held-back Ambush unit turned up
        /// activatable on round 1, drawn in the table's bottom-left corner. A model's base never fits
        /// wholly inside the table with its centre exactly at (0,0), so the coordinate is still a safe
        /// "never placed" marker - it is just no longer the thing that defines reserve.
        /// </summary>
        public static bool GetIsOnBattlefield(this IUnit unit)
        {
            if (Rules.Dispatch.ReserveRules.IsInReserve(unit)) return false;
            if (unit.Tokens.HasToken(Rules.Foundation.TokenType.EmbarkedIn)) return false;
            if (unit.Tokens.HasToken(Rules.Foundation.TokenType.OffTableFromForcedMove)) return false;

            foreach (IModel model in unit.Models)
            {
                if (!model.GetIsAlive()) continue;
                Position pos = model.Position;
                if (pos.x != 0f || pos.z != 0f) return true;
            }
            return false;
        }

        public static List<IWeapon> AllWeapons(this IUnit unit)
        {
            List<IWeapon> allWeapons = new List<IWeapon>();

            foreach (IModel model in unit.Models)
            {
                if (!model.GetIsAlive()) continue;
                allWeapons.AddRange(model.Weapons);
            }

            return allWeapons;
        }

        public static List<Weapon> AllWeapons(this IUnit unit, Func<Weapon, bool> predicate)
        {
            List<Weapon> allWeapons = new List<Weapon>();

            foreach (IModel model in unit.Models)
            {
                if (!model.GetIsAlive()) continue;
                allWeapons.AddRange(model.Weapons.Where(predicate));
            }

            return allWeapons;
        }

        // #197 Strafing: "this weapon may only be used in this way" - a strafe weapon is excluded from BOTH
        // attack pools. The melee exclusion is the one that bites: every corpus strafe weapon has range 0,
        // and IsMelee() is defined as "range 0", so a bomb rack would otherwise be swung in close combat.
        public static List<Weapon> GetMeleeWeapons(this IUnit unit)
        {
            return unit.AllWeapons(u => u.IsMelee() && !Rules.Dispatch.StrafingRules.IsStrafeOnly(u));
        }

        public static List<Weapon> GetRangedWeapons(this IUnit unit)
        {
            return unit.AllWeapons(u => u.IsRanged() && !Rules.Dispatch.StrafingRules.IsStrafeOnly(u));
        }
    }


}