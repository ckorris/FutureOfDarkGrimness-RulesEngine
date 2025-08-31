using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Stages;

namespace FDG
{ 
    /*
    public interface ISingleAttackContext<TMetadata> : ISingleAttackContext
    {
        TMetadata CombatMetaData { get; }
    }

    public interface ISingleAttackContext : IGameContextAccessor
    {
        IReadOnlyList<ISpecialRule_Combat> AllSpecialRules { get; }
    }

    public abstract class SingleAttackContext<TMetadata> : ISingleAttackContext<TMetadata>
        where TMetadata : ICombatMetadata
    {
        public IReadOnlyList<ISpecialRule_Combat> AllSpecialRules { get; private set; }

        public TMetadata CombatMetaData { get; private set; }

        public IGameContext GameContext { get; private set; }

        public StageHandlerRegistry Handlers { get; }

        public SingleAttackContext(IGameContext gameContext)
        {
            GameContext = gameContext;
        }

        public void SetCombatMetadata(TMetadata combatMetadata)
        {
            CombatMetaData = combatMetadata;
            AllSpecialRules = GetAllSpecialRules(combatMetadata);
        }


        public void ClearCurrentAttack()
        {
            //TODO: Flags?
            CombatMetaData = default;
            AllSpecialRules = null;
        }

        private IReadOnlyList<ISpecialRule_Combat> GetAllSpecialRules(TMetadata combatMetaData)
        {
            List<ISpecialRule_Combat> allSpecialRules = new List<ISpecialRule_Combat>();
            allSpecialRules.AddRange(combatMetaData.WeaponType.SpecialRules);

            //Add the attacker's offsensive rules.
            allSpecialRules.AddRange(new List<ISpecialRule_Combat>(combatMetaData.AttackingUnit.SpecialRules.OfType<ISpecialRule_Attacker>()));

            //Add the defender's defensive rules.
            //Note: We are counting on the unit to handle cases where parts of the model have a rule and the rest don't.
            //TODO: Make sure this is clear from the code itself, or documentation in a more appropriate place. 

            //Prevent duplicates.
            HashSet<Type> addedDefenderRules = new HashSet<Type>();

            foreach (ISpecialRule_Defender defenderRule in combatMetaData.DefendingUnit.SpecialRules)
            {
                Type ruleType = defenderRule.GetType();
                if (addedDefenderRules.Contains(ruleType))
                {
                    continue;
                }

                allSpecialRules.Add(defenderRule);

                addedDefenderRules.Add(ruleType);
            }

            return allSpecialRules;
        }
    }
    */
}
