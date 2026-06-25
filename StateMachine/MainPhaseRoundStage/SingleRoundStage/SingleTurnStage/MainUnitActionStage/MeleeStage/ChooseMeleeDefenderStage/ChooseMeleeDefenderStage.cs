
using FDG.Data;
using FDG.StageResolution;
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
            
            List<CancellableSelectionRequest<UnitData>.ValidOption> validDefenders = new List<CancellableSelectionRequest<UnitData>.ValidOption>();
            List<CancellableSelectionRequest<UnitData>.InvalidOption> invalidDefenders = new List<CancellableSelectionRequest<UnitData>.InvalidOption>();

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
                    // #022: a unit is engageable only if some model pair is within the melee cylinder
                    // (2" horizontal AND 4" vertical) — the same gate the post-pile-in strike check uses,
                    // so we never offer a defender no model could actually strike.
                    if(MeleeRangeUtilities.AreUnitsInMeleeRange(context.AttackingUnit.GetValue(), potentialDefender.GetValue()))
                    {
                        validDefenders.Add(new CancellableSelectionRequest<UnitData>.ValidOption(potentialDefender, potentialDefender.GetValue().Name));
                    }
                    else
                    {
                        invalidDefenders.Add(new CancellableSelectionRequest<UnitData>.InvalidOption(potentialDefender, potentialDefender.GetValue().Name,
                            "This unit is too far away."));
                    }
                }
            }

            async Task ChooseDefender(DataBinding<UnitData> defender)
            {
                //Set the defender on the context.
                GameContext.Log($"Chose {defender.GetValue().Name} as defender.");
                context.SetDefender(defender);
                await OnDefenderChosen.Activate(context);
            }

            if (validDefenders.Count == 0)
            {
                GameContext.Log("No potential melee units found.");
                await BackToChooseAction.Activate(context);
                return;
            }
            else if(validDefenders.Count == 1)
            {
                //No need to pose the request, just attack.
                await ChooseDefender(validDefenders.First().Option);
                return;
            }

            //If we're here, we have multiple potential targets. Ask the user who to attack.
            CancellableSelectionRequest<UnitData> request = new CancellableSelectionRequest<UnitData>(attackingPlayer, "Choose defending unit",
                validDefenders, invalidDefenders);

            CancellableResult<DataBinding<UnitData>> defenderResult
                = await GameContext.PlayerRequester
                .RequestDecision<CancellableSelectionRequest<UnitData>, CancellableResult<DataBinding<UnitData>>>(request);

            if (defenderResult is Cancelled<DataBinding<UnitData>>)
            {
                await BackToChooseAction.Activate(context);
                return;
            }

            await ChooseDefender(((Selected<DataBinding<UnitData>>)defenderResult).Value);
        }
    }
}
