using FDG.Data;
using FDG.Rules.Foundation;
using FDG.Utilities;
using System.Collections.Generic;

namespace FDG
{
    public interface ICombatMetadata
    {
        public IGameContext GameContext { get; }

        public IWeapon WeaponType { get; }

        public int WeaponCount { get; }

        public DataBinding<UnitData> AttackingUnit { get; }

        public DataBinding<UnitData> DefendingUnit { get; }

        /// <summary>True if the attacking unit moved earlier this activation (drives Indirect's -1 to hit).</summary>
        public bool AttackerMoved { get; }

        /// <summary>True when this is a melee attack (vs. shooting); the hit-roll stages are shared.</summary>
        public bool IsMelee { get; }

        /// <summary>True when the attacker is charging (the charger's swing, not a strike-back); drives Thrust.</summary>
        public bool IsCharging { get; }

        /// <summary>
        /// #197: how far the defender was when the charging unit's activation began — the distance this
        /// charge was declared from. 0 when shooting, and 0 for a melee swing that isn't a charge. Fills
        /// <c>IHasAttackOriginDistance</c> on the hit/save contexts so the "shoots or charges enemies over
        /// 9\" away" rules can gate their melee arm, which a live-distance check never can (melee resolves
        /// in base contact).
        /// </summary>
        public float ChargeOriginDistanceInches { get; }

        /// <summary>True when the hits come from a damage spell (ResolveSpellDamageStage) rather than a
        /// weapon; drives spell-facet save-side rules (Resistance's ignore-on-2+).</summary>
        public bool IsSpell { get; }

        /// <summary>#197 (P15): the Unpredictable die rolled once for this whole attack action (1-3 -> AP(+1),
        /// 4-6 -> +1 to hit; <see cref="EUnpredictableBranch.None"/> when no Unpredictable rule applies).
        /// Fills <c>IHasUnpredictableBranch</c> on the hit/save contexts so both the +1-hit hook (72) and the
        /// AP/save hook (73) gate on the SAME die.</summary>
        public EUnpredictableBranch UnpredictableBranch { get; }

        /// <summary>#276: this attack's position within a #157 split burst (a Takedown volley fires its
        /// copies as separate single-shot attacks); 0 for ordinary unsplit attacks. Presentation only —
        /// the attack beat rotates the firing model by it so each split shot animates from a different
        /// carrier instead of all of them at once.</summary>
        public int BurstShotIndex => 0;

        //TODO: Next value can replace everything after?
        public void AddResult<TResult>(TResult result);

        public bool QueryForResult<TResult>(out TResult result);
    }

    public class CombatMetadata : ICombatMetadata
    {
        public IGameContext GameContext { get; private set; }

        public IWeapon WeaponType { get; private set; }

        public int WeaponCount { get; private set; }

        public DataBinding<UnitData> AttackingUnit { get; private set; }

        public DataBinding<UnitData> DefendingUnit { get; private set; }

        public bool AttackerMoved { get; private set; }

        public bool IsMelee { get; private set; }

        public bool IsCharging { get; private set; }

        public float ChargeOriginDistanceInches { get; private set; }

        public bool IsSpell { get; private set; }

        public EUnpredictableBranch UnpredictableBranch { get; private set; }

        public int BurstShotIndex { get; private set; }


        private QueryableResults _queryableResults = new QueryableResults();

        public CombatMetadata(IGameContext gameContext, DataBinding<UnitData> attackingUnit,
            DataBinding<UnitData> defendingUnit, IWeapon weaponType, int weaponCount,
            bool attackerMoved = false, bool isMelee = false, bool isCharging = false, bool isSpell = false,
            float chargeOriginDistanceInches = 0f,
            EUnpredictableBranch unpredictableBranch = EUnpredictableBranch.None,
            int burstShotIndex = 0)
        {
            GameContext = gameContext;
            AttackingUnit = attackingUnit;
            DefendingUnit = defendingUnit;
            WeaponType = weaponType;
            WeaponCount = weaponCount;
            AttackerMoved = attackerMoved;
            IsMelee = isMelee;
            IsCharging = isCharging;
            IsSpell = isSpell;
            ChargeOriginDistanceInches = chargeOriginDistanceInches;
            UnpredictableBranch = unpredictableBranch;
            BurstShotIndex = burstShotIndex;
        }

        public void AddResult<TResult>(TResult result)
        {
            _queryableResults.AddResult(result);
        }

        public bool QueryForResult<TResult>(out TResult result)
        {
            return _queryableResults.QueryForResult(out result);
        }
    }

    public static class ICombatMetaDataExtensions
    {
        public static IDiceRoller DiceRoller(this ICombatMetadata metadata)
        {
            return metadata.GameContext.DiceRoller;
        }

        public static ITextOutput TextOutput(this ICombatMetadata metadata)
        {
            return metadata.GameContext.TextOutput;
        }
    }
}