
using System.Collections.Generic;

namespace FDG.Stages
{

    public class FireStage : ParentStage<ICombatActionContext, ICombatMetadata>
    {
        public StageBinding OnFinishedFiring;

        public FireStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent) : base(gameContext, parent)
        {
            
        }

        public override void Enter(ICombatActionContext context)
        {
            GameContext.Log("Firing.");

            base.Enter(context);
        }

        protected override ICombatMetadata GetNewChildContext(ICombatActionContext contextSelf)
        {
            return contextSelf.ConsumeAttackIntoContext(GameContext);
        }

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<ICombatMetadata> startingChild)
        {
            OnFinishedFiring = new StageBinding(this);

            Dictionary<string, Transition> dictionary = new TransitionSetBuilder(this)
                .AddChild(new BuildTargetListStage<ICombatMetadata>(GameContext, this), out var buildTargetList)
                .AddChild(new RangeCheckStage(GameContext, this), out var rangeCheck)
                .AddChild(new OcclusionCheckStage(GameContext, this), out var occlusionCheck)
                .AddChild(new CoverCheckStage(GameContext, this), out var coverCheck)
                .AddChild(new DetermineHitRollNeededStage<ICombatMetadata>(GameContext, this), out var determineHitRollNeeded)
                .AddChild(new RollToHitStage<ICombatMetadata>(GameContext, this), out var rollToHit)
                .AddChild(new DetermineSaveRollsNeededStage<ICombatMetadata>(GameContext, this), out var determineSaveRollNeeded)
                .AddChild(new RollToSaveStage<ICombatMetadata>(GameContext, this), out var rollToSave)
                .AddChild(new AssignWoundsStage<ICombatMetadata>(GameContext, this), out var assignWounds)
                .AddChild(new ApplyWoundsStage<ICombatMetadata>(GameContext, this), out var applyWounds)
                .AddSibling(nameof(OnFinishedFiring), OnFinishedFiring, out string finishedFiringName)
                .Build();

            startingChild = buildTargetList;

            buildTargetList.BindNextStage(rangeCheck)
                .BindNextStage(occlusionCheck)
                .BindNextStage(coverCheck)
                .BindNextStage(determineHitRollNeeded)
                .BindNextStage(rollToHit)
                .BindNextStage(determineSaveRollNeeded)
                .BindNextStage(rollToSave)
                .BindNextStage(assignWounds)
                .BindNextStage(applyWounds)
                .BindToEvent(finishedFiringName);

            return dictionary;
        }
    }
}