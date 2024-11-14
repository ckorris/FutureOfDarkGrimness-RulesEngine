

using FDG.Stages;

namespace FDG
{
    public class SingleMeleeAttackContext : SingleAttackContext<IMeleeCombatMetadata>, 
        ISingleAttackContext<ICombatMetadata>
    {
        public SingleMeleeAttackContext( ITextOutput textOutput, IDiceRoller diceRoller, StageHandlerRegistry handlers)
            : base(textOutput, diceRoller, handlers)
        {
        }

        ICombatMetadata ISingleAttackContext<ICombatMetadata>.CombatMetaData => CombatMetaData;
    }
}
