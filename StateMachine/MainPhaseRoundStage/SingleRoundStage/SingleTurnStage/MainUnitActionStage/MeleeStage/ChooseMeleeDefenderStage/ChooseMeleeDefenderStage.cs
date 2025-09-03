
using FDG.Data;
using FDG.StageResolution.Requests;
using FDG.Utilities;

namespace FDG.Stages
{
    public class ChooseMeleeDefenderStage : StageBase<ICombatActionContext>
    {
        public StageBinding OnDefenderChosen;
        public StageBinding BackToChooseAction;

        public ChooseMeleeDefenderStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent)
            : base(gameContext, parent)
        {
            OnDefenderChosen = new StageBinding(this);
            BackToChooseAction = new StageBinding(this);
        }

        public override async Task Enter(ICombatActionContext context)
        {
            GameContext.Log("Entered Choose Melee Defender.");

            List<ActionChoice> choices = new List<ActionChoice>();

            PlayerID attackingPlayer = context.AttackingUnit.PlayerID();
            
            List<SelectionRequest<UnitData>.ValidOption> validDefenders = new List<SelectionRequest<UnitData>.ValidOption>();
            List<SelectionRequest<UnitData>.InvalidOption> invalidDefenders = new List<SelectionRequest<UnitData>.InvalidOption>();

            //There has GOT to be a better way to look up what team a player is on.
            TeamData? playerTeam = GameContext.GameDataStore.GetAllValues<TeamData>()
                .FirstOrDefault(teamData => teamData.IsPlayerOnTeam(attackingPlayer));

            IReadOnlyList<PlayerID> alliedPlayers;

            if (playerTeam == null)
            {
                alliedPlayers = new List<PlayerID>() { attackingPlayer }; 
            }
            else
            {
                alliedPlayers = playerTeam.Players;
            }


            foreach(ArmyData army in GameContext.GameDataStore.GetAllValues<ArmyData>()
                .Where(a => alliedPlayers.Contains(a.PlayerID) == false))
            {
                foreach(DataBinding<UnitData> potentialDefender in army.UnitBindings)
                {
                    float minDistanceToUnit = UnitCompareUtilities.MinDistanceBetweenUnits(context.AttackingUnit.GetValue(), potentialDefender.GetValue(),
                        out _, out _, includeVertical: false);
                    if(minDistanceToUnit <= GameWideConstants.MELEE_RANGE_INCHES_HORIZONTAL)
                    {
                        //TODO: Deal with vertical, but we might leave that for pile-in.
                        validDefenders.Add(new SelectionRequest<UnitData>.ValidOption(potentialDefender, potentialDefender.GetValue().Name));
                    }
                    else
                    {
                        invalidDefenders.Add(new SelectionRequest<UnitData>.InvalidOption(potentialDefender, potentialDefender.GetValue().Name,
                            "This unit is too far away."));
                    }
                }
            }

            void ChooseDefender(DataBinding<UnitData> defender)
            {
                //Set the defender on the context.
                GameContext.Log($"Chose {defender.GetValue().Name} as defender.");
                context.BeginNewAttack(defender);
                OnDefenderChosen.Activate(context);
            }

            if (validDefenders.Count == 0)
            {
                GameContext.Log("No potential melee units found.");
                BackToChooseAction.Activate(context); //TODO: Tell the user somehow besides the log?
            }
            else if(validDefenders.Count == 1)
            {
                //No need to pose the request, just attack.
                ChooseDefender(validDefenders.First().Option);
                return;
            }

            //If we're here, we have multiple potential targets. Ask the user who to attack.
            SelectionRequest<UnitData> request = new SelectionRequest<UnitData>(attackingPlayer, "Choose defending unit",
                validDefenders, invalidDefenders);

            DataBinding<UnitData> chosenDefender
                = await GameContext.PlayerRequester
                .RequestDecision<SelectionRequest<UnitData>, DataBinding<UnitData>>(request);

            ChooseDefender(chosenDefender);
        }
    }
}
