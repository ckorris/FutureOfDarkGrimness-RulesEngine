using FDG.Data;
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

        IReadOnlyList<ISpecialRule_Combat> AllSpecialRules { get; }

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


        public IReadOnlyList<ISpecialRule_Combat> AllSpecialRules { get; private set; }


        private QueryableResults _queryableResults = new QueryableResults();

        public CombatMetadata(IGameContext gameContext, DataBinding<UnitData> attackingUnit,
            DataBinding<UnitData> defendingUnit, IWeapon weaponType, int weaponCount,
            bool attackerMoved = false, bool isMelee = false, bool isCharging = false)
        {
            GameContext = gameContext;
            AttackingUnit = attackingUnit;
            DefendingUnit = defendingUnit;
            WeaponType = weaponType;
            WeaponCount = weaponCount;
            AttackerMoved = attackerMoved;
            IsMelee = isMelee;
            IsCharging = isCharging;

            AllSpecialRules = GetAllSpecialRules(attackingUnit, defendingUnit, weaponType);
        }

        public void AddResult<TResult>(TResult result)
        {
            _queryableResults.AddResult(result);
        }

        public bool QueryForResult<TResult>(out TResult result)
        {
            return _queryableResults.QueryForResult(out result);
        }

        private List<ISpecialRule_Combat> GetAllSpecialRules(DataBinding<UnitData> attackingUnit, DataBinding<UnitData> defendingUnit,
            IWeapon weaponType)
        {
            List<ISpecialRule_Combat> specialRules = new List<ISpecialRule_Combat>();

            specialRules.AddRange(attackingUnit.GetAttackerSpecialRules());
            specialRules.AddRange(defendingUnit.GetDefenderSpecialRules()); //TODO: Need to differentiate attacker and defender.
            //#027: weapon rules are #042 RuleDefinitions dispatched per-weapon by RuleEvaluator;
            //they no longer ride this legacy combat-rule list.

            return specialRules;
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