
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Stages
{

    public class ArmySetupStage : StageBase<IGameContext>
    {
        public StageBinding ToMapSetup;

        public ArmySetupStage(IGameContext gameContext, IStateMachineLayer<IGameContext> parent) : base(gameContext, parent)
        {
            ToMapSetup = new StageBinding(this);
        }

        public override async Task Enter(IGameContext context)
        {
            context.Log($"Entered {nameof(ArmySetupStage)}.");

            List<Task<DataBinding<ArmyData>>> requestTasks = new List<Task<DataBinding<ArmyData>>>();

            foreach (ITeam team in context.TableState.Teams.Objects)
            {
                foreach (PlayerID playerID in team.Players)
                {
                    SingleBindingRequest<ArmyData> armyRequest = new SingleBindingRequest<ArmyData>(playerID, "Choose an Army.");
                    Task<DataBinding<ArmyData>> task = context.PlayerRequester.RequestDecision<SingleBindingRequest<ArmyData>, 
                        DataBinding<ArmyData>>(armyRequest);

                    requestTasks.Add(task);
                }
            }

            await Task.WhenAll(requestTasks);

            //TODO: In order for everyone to get here, they must have uploaded an army. 
            //But then, it's already in the back, and we probably don't need to do anything about it.
            //Maybe in the future we can validate that it's correct, but I don't want to focus on that now.
            //So just assume all the armies have been uploaded correctly and continue.
            //Probably in the future we want to send the saveable army list object and then convert it to an army. 
            ToMapSetup.Activate(context);

        }
        /* //Commenting out because this isn't used BUT it could be used to convert an IArmyTemplate in the form of a
           //saveable army list, into the actual uploaded armies.
        private void OnArmiesChosen(IGameContext gameContext, List<IArmyTemplate> armies)
        {
            foreach(IArmyTemplate armyToCopy in armies)
            {
                List<DataReference> newUnitCopies = new List<DataReference>();

                foreach(IUnitTemplate unitToCopy in armyToCopy.Units)
                {
                    List<DataReference> newModelCopies = new List<DataReference>(); 

                    foreach(IModelTemplate modelToCopy in unitToCopy.Models)
                    {
                        ModelData modelData = new ModelData(modelToCopy, gameContext.GameDataStore);
                        DataReference modelDataRef = gameContext.GameDataStore.Create(modelData);
                        newModelCopies.Add(modelDataRef);
                    }

                    UnitData unitData = new UnitData(unitToCopy, newModelCopies, gameContext.GameDataStore);
                    DataReference unitDataRef = gameContext.GameDataStore.Create(unitData);
                    newUnitCopies.Add(unitDataRef);
                }

                ArmyData armyData = new ArmyData(armyToCopy, newUnitCopies, gameContext.GameDataStore);
                gameContext.GameDataStore.Create(armyData);   
            }

            ToMapSetup.Activate(gameContext);
        }
        */
    }
}