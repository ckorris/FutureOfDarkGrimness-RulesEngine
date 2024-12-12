
using System.Collections.Generic;

namespace FDG.Stages
{

    public class FireStage : ParentStage<IRangedContext, IRangedCombatMetadata>
    {
        public StageBinding OnFinishedFiring;

        public FireStage(IGameContext gameContext, IStateMachineLayer<IRangedContext> parent) : base(gameContext, parent)
        {
            
        }

        public override void Enter(IRangedContext context)
        {
            GameContext.Log("Firing.");

            base.Enter(context);
        }

        protected override IRangedCombatMetadata GetNewChildContext(IRangedContext contextSelf)
        {
            throw new System.NotImplementedException();

            //return new RangedCombatMetadata(GameContext);
        }

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<IRangedCombatMetadata> startingChild)
        {
            OnFinishedFiring = new StageBinding(this);

            Dictionary<string, Transition> dictionary = new TransitionSetBuilder(this)
                .AddChild(new BuildTargetListStage<IRangedCombatMetadata>(GameContext, this), out var buildTargetList)
                .AddChild(new RangeCheckStage(GameContext, this), out var rangeCheck)
                .AddChild(new OcclusionCheckStage(GameContext, this), out var occlusionCheck)
                .AddChild(new CoverCheckStage(GameContext, this), out var coverCheck)
                .AddChild(new DetermineHitRollNeededStage<IRangedCombatMetadata>(GameContext, this), out var determineHitRollNeeded)
                .AddChild(new RollToHitStage<IRangedCombatMetadata>(GameContext, this), out var rollToHit)
                .AddChild(new DetermineSaveRollsNeededStage<IRangedCombatMetadata>(GameContext, this), out var determineSaveRollNeeded)
                .AddChild(new RollToSaveStage<IRangedCombatMetadata>(GameContext, this), out var rollToSave)
                .AddChild(new AssignWoundsStage<IRangedCombatMetadata>(GameContext, this), out var assignWounds)
                .AddChild(new ApplyWoundsStage<IRangedCombatMetadata>(GameContext, this), out var applyWounds)
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