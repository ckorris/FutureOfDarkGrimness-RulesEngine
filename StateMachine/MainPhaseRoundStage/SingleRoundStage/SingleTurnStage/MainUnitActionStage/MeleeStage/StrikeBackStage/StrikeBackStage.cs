
using System.Collections.Generic;

namespace FDG.Stages
{

    public class StrikeBackStage : ParentStage<ICombatActionContext, ICombatActionContext>
    {
        public StageBinding FinishedStrikingBack;
        public StageBinding OnAttackerKilled;

        public StrikeBackStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent) : base(gameContext, parent)
        {

        }

        protected override ICombatActionContext GetNewChildContext(ICombatActionContext contextSelf)
        {
            CombatActionContext meleeContext = new CombatActionContext(contextSelf.GameContext, contextSelf.DefendingUnit);
            meleeContext.SetDefender(contextSelf.AttackingUnit); //Purposefully reversed.
            return meleeContext;
        }

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<ICombatActionContext> startingChild)
        {
            FinishedStrikingBack = new StageBinding(this);
            OnAttackerKilled = new StageBinding(this);

            Dictionary<string, Transition> dictionary = new TransitionSetBuilder(this)
                .AddChild(new ChooseMeleeWeaponStage(GameContext, this), out var chooseMeleeWeapon)
                .AddChild(new SwingMeleeWeaponStage(GameContext, this), out var swingMeleeWeapon)
                .AddChild(new DetermineCanKeepSwingingStage(GameContext, this), out var determineCanKeepSwinging)
                .AddSibling(nameof(FinishedStrikingBack), FinishedStrikingBack, out string finishedStrikingBackEvent)
                .AddSibling(nameof(OnAttackerKilled), OnAttackerKilled, out string onAttackerKilledEvent)
                .Build();

            startingChild = chooseMeleeWeapon;

            chooseMeleeWeapon.OnChosen.Bind(swingMeleeWeapon);
            swingMeleeWeapon.FinishedSwinging.Bind(determineCanKeepSwinging);
            determineCanKeepSwinging.ReturnToChooseWeapon.Bind(chooseMeleeWeapon);
            determineCanKeepSwinging.OnOutOfWeapons.Bind(finishedStrikingBackEvent);
            determineCanKeepSwinging.OnDefenderKilled.Bind(onAttackerKilledEvent);

            return dictionary;
        }
    }
}