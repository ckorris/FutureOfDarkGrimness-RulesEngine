
using System;
using System.Collections.Generic;
using System.Linq;

namespace FDG.Stages
{

    public interface ISingleRangedAttackContext : ICommonContextItems
    {
        IReadOnlyList<ISpecialRule_Combat> AllSpecialRules { get; }

        IRangedCombatMetadata CombatMetaData { get; }

    }


    public class SingleRangedAttackContext : ISingleRangedAttackContext
    {
        public ITextOutput TextOutput { get; private set; }

        public IDiceRoller DiceRoller { get; private set; }

        public IReadOnlyList<ISpecialRule_Combat> AllSpecialRules { get; private set; }

        public IRangedCombatMetadata CombatMetaData { get; private set; }


        public SingleRangedAttackContext(ITextOutput textOutput, IDiceRoller diceRoller)
        {
            TextOutput = textOutput;
            DiceRoller = diceRoller;
        }

        public void SetCombatMetadata(IRangedCombatMetadata combatMetaData)
        {
            if(combatMetaData.IsSetUp == false)
            {
                throw new System.ArgumentException($"Passed in {nameof(IRangedCombatMetadata)} to {nameof(SingleRangedAttackContext)} " + 
                    "that was not fully set up. Make sure required data is assigned first.");
            }

            CombatMetaData = combatMetaData;
            AllSpecialRules = GetAllSpecialRules(combatMetaData);
        }

        public void ClearCurrentAttack()
        {
            //TODO: Flags?
            CombatMetaData = null;
            AllSpecialRules = null;
        }


        private IReadOnlyList<ISpecialRule_Combat> GetAllSpecialRules(IRangedCombatMetadata combatMetaData)
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
}