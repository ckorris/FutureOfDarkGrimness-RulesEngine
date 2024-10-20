

namespace FDG
{
    public class SingleMeleeAttackContext : SingleAttackContext<IMeleeCombatMetadata>
    {
        public SingleMeleeAttackContext(ITextOutput textOutput, IDiceRoller diceRoller)
            : base(textOutput, diceRoller)
        {
        }
    }
}
