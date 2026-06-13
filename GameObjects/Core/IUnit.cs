
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
        /// Whether any living model of this unit is on the table (a non-origin position). A unit kept in
        /// reserve (Ambush) has never been placed, so all its models sit at the default origin (0,0,0);
        /// such a unit is alive but not yet on the battlefield, and must be excluded from activation,
        /// targeting, etc. until it arrives. Mirrors the renderer/AI "(0,0,0) means unplaced" convention.
        /// </summary>
        public static bool GetIsOnBattlefield(this IUnit unit)
        {
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

        public static List<Weapon> GetMeleeWeapons(this IUnit unit)
        {
            return unit.AllWeapons(u => u.IsMelee());
        }

        public static List<Weapon> GetRangedWeapons(this IUnit unit)
        {
            return unit.AllWeapons(u => u.IsRanged());
        }
    }


}