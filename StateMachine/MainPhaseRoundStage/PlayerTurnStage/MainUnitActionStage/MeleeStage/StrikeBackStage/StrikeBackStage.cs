
using System.Collections.Generic;

namespace FDG.Stages
{

    public class StrikeBackStage : ParentStage<IMeleeContext, IMeleeContext>
    {
        public StageBinding FinishedStrikingBack;
        public StageBinding OnAttackerKilled;

        public StrikeBackStage(IGameContext gameContext, IStateMachineLayer<IMeleeContext> parent) : base(gameContext, parent)
        {
            FinishedStrikingBack = new StageBinding(this);
            OnAttackerKilled = new StageBinding(this);
        }

        protected override IMeleeContext GetNewChildContext(IMeleeContext contextSelf)
        {
            MeleeContext meleeContext = new MeleeContext(GameContext, contextSelf.DefendingUnit);
            meleeContext.BeginNewAttack(contextSelf.AttackingUnit); //Purposefully reversed.
            return meleeContext;
        }

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<IMeleeContext> startingChild)
        {
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