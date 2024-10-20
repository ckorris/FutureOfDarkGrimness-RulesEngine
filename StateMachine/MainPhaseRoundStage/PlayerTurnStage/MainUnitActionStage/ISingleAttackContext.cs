
namespace FDG
{ 
    public interface ISingleAttackContext<TMetadata> : ISingleAttackContext
    {
        TMetadata CombatMetadata { get; }
    }

    public interface ISingleAttackContext : ICommonContextItems
    {
        IReadOnlyList<ISpecialRule_Combat> AllSpecialRules { get; }
    }

    public abstract class SingleAttackContext<TMetadata> : ISingleAttackContext<TMetadata>
        where TMetadata : ICombatMetadata
    {
        public IReadOnlyList<ISpecialRule_Combat> AllSpecialRules { get; private set; }

        public TMetadata CombatMetadata { get; private set; }

        public ITextOutput TextOutput { get; private set; }

        public IDiceRoller DiceRoller { get; private set; }

        public SingleAttackContext(ITextOutput textOutput, IDiceRoller diceRoller)
        {
            TextOutput = textOutput;
            DiceRoller = diceRoller;
        }

        public void SetCombatMetadata(TMetadata combatMetadata)
        {
            if (combatMetadata.IsSetUp == false)
            {
                throw new ArgumentException($"Passed in {typeof(TMetadata)} to {GetType()} " +
                    "that was not fully set up. Make sure required data is assigned first.");
            }

            CombatMetadata = combatMetadata;
            AllSpecialRules = GetAllSpecialRules(combatMetadata);
        }


        public void ClearCurrentAttack()
        {
            //TODO: Flags?
            CombatMetadata = default;
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
}
