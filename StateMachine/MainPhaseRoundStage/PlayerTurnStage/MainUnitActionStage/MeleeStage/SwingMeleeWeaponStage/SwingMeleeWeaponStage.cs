
using System.Collections.Generic;

namespace FDG.Stages
{
    public class SwingMeleeWeaponStage : ParentStage<IMeleeContext, ISingleAttackContext<IMeleeCombatMetadata>>
    {
        private const string SWING_TO_CHILD_ENTRANCE_TRANSITION = "SwingToChildEntrance";

        private readonly SingleMeleeAttackContext _attackContext;

        public StageBinding FinishedSwinging;
        public SwingMeleeWeaponStage(IGameContext gameContext, IStateMachineLayer<IMeleeContext> parent) : base(gameContext, parent)
        {
            FinishedSwinging = new StageBinding(this);
        }

        public override void Enter(IMeleeContext context)
        {
            GameContext.Log("Swinging.");

            base.Enter(context);
        }

        protected override ISingleAttackContext<IMeleeCombatMetadata> GetNewChildContext(IMeleeContext contextSelf)
        {
            MeleeCombatMetadata meleeCombatMetadata = new MeleeCombatMetadata(contextSelf.AttackingUnit, contextSelf.DefendingUnit,
                GameContext.DiceRoller, GameContext.TextOutput);
            SingleMeleeAttackContext singleMeleeAttackContext = new SingleMeleeAttackContext(GameContext);
            singleMeleeAttackContext.SetCombatMetadata(meleeCombatMetadata);
            return singleMeleeAttackContext;
        }

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<ISingleAttackContext<IMeleeCombatMetadata>> startingChild)
        {
            Dictionary<string, Transition> dictionary = new TransitionSetBuilder(this)
                .AddChild(new BuildTargetListStage<IMeleeCombatMetadata>(GameContext, this), out var buildTargetList)
                .AddChild(new DetermineHitRollNeededStage<IMeleeCombatMetadata>(GameContext, this), out var determineHitRollNeeded)
                .AddChild(new RollToHitStage<IMeleeCombatMetadata>(GameContext, this), out var rollToHit)
                .AddChild(new DetermineSaveRollsNeededStage<IMeleeCombatMetadata>(GameContext, this), out var determineSaveRollsNeeded)
                .AddChild(new RollToSaveStage<IMeleeCombatMetadata>(GameContext, this), out var rollToSave)
                .AddChild(new AssignWoundsStage<IMeleeCombatMetadata>(GameContext, this), out var assignWounds)
                .AddChild(new ApplyWoundsStage<IMeleeCombatMetadata>(GameContext, this), out var applyWounds)
                .AddSibling(nameof(FinishedSwinging), FinishedSwinging, out string finishedSwingingEvent)
                .Build();

            startingChild = buildTargetList;

            buildTargetList.BindNextStage(determineHitRollNeeded)
                .BindNextStage(rollToHit)
                .BindNextStage(determineSaveRollsNeeded)
                .BindNextStage(rollToSave)
                .BindNextStage(assignWounds)
                .BindNextStage(applyWounds)
                .BindToEvent(finishedSwingingEvent);

            return dictionary;
        }

    }
}
