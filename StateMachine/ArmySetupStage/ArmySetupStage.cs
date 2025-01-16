
using FDG.Data;

namespace FDG.Stages
{

    public class ArmySetupStage : StageBase<IGameContext>
    {
        public StageBinding ToMapSetup;

        public ArmySetupStage(IGameContext gameContext, IStateMachineLayer<IGameContext> parent) : base(gameContext, parent)
        {
            ToMapSetup = new StageBinding(this);
        }

        public override void Enter(IGameContext context)
        {
            context.Log($"Entered {nameof(ArmySetupStage)}.");
            GameContext.GetHandler<IArmySetupHandler>()
                .Handle((armies) => OnArmiesChosen(context, armies));
        }

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
                        ModelData modelData = new ModelData(modelToCopy, gameContext.GameDataStore,
                            gameContext.CommandProcessor);
                        DataReference modelDataRef = gameContext.GameDataStore.Create(modelData);
                        newModelCopies.Add(modelDataRef);
                    }

                    UnitData unitData = new UnitData(unitToCopy, newModelCopies, gameContext.GameDataStore,
                            gameContext.CommandProcessor);
                    DataReference unitDataRef = gameContext.GameDataStore.Create(unitData);
                    newUnitCopies.Add(unitDataRef);
                }

                ArmyData armyData = new ArmyData(armyToCopy, newUnitCopies, gameContext.GameDataStore,
                            gameContext.CommandProcessor);
                gameContext.GameDataStore.Create(armyData);   
            }

            ToMapSetup.Activate(gameContext);
        }
    }

    public interface IArmySetupHandler
    {
        void Handle(Action<List<IArmyTemplate>> onArmiesChosen);
    }
}