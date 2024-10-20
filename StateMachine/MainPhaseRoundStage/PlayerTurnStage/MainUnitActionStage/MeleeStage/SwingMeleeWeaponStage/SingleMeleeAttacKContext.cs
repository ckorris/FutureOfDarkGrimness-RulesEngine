

namespace FDG
{
    public class SingleMeleeAttackContext : SingleAttackContext<IMeleeCombatMetadata>, 
        ISingleAttackContext<ICombatMetadata>
    {
        public SingleMeleeAttackContext(ITextOutput textOutput, IDiceRoller diceRoller)
            : base(textOutput, diceRoller)
        {
        }

        ICombatMetadata ISingleAttackContext<ICombatMetadata>.CombatMetadata => CombatMetadata;
    }
}
