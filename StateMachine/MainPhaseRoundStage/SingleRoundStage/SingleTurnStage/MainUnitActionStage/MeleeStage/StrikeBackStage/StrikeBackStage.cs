
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

        public override async Task Enter(ICombatActionContext context)
        {
            // #020: reaching this stage means the defender is actually striking back — record it so
            // ApplyFatigueStage fatigues the striker. (The charger's own fatigue is handled there too.)
            context.RegisterDefenderStruckBack();
            await base.Enter(context);
        }

        protected override ICombatActionContext GetNewChildContext(ICombatActionContext contextSelf)
        {
            CombatActionContext meleeContext = new CombatActionContext(contextSelf.GameContext,
                contextSelf.DefendingUnit, isMelee: true);
            meleeContext.SetDefender(contextSelf.AttackingUnit); //Purposefully reversed.

            // #017: only the defending models within melee range may strike back. The roles are reversed
            // here, so the parent's in-range defenders are this context's in-range attackers (and vice versa).
            meleeContext.SetInRangeAttackers(contextSelf.InRangeDefendingModels);
            meleeContext.SetInRangeDefenders(contextSelf.InRangeAttackingModels);
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