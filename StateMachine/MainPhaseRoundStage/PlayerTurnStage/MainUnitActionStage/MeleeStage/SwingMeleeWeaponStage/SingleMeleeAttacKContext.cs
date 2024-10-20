

using FDG.Stages;

namespace FDG
{
    public class SingleMeleeAttackContext : SingleAttackContext<IMeleeCombatMetadata>, 
        ISingleAttackContext<ICombatMetadata>
    {
        public SingleMeleeAttackContext(SingleCombatHandlers singleCombatHandlers, ITextOutput textOutput, IDiceRoller diceRoller)
            : base(singleCombatHandlers, textOutput, diceRoller)
        {
        }

        ICombatMetadata ISingleAttackContext<ICombatMetadata>.CombatMetaData => CombatMetaData;
    }
}
