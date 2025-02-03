using FDG.Data;

namespace FDG.Stages
{
    public class PlayerSetupStage : StageBase<IGameContext>
    {
        public StageBinding ToArmySetup;

        public PlayerSetupStage(IGameContext gameContext, IStateMachineLayer<IGameContext> parent)
            : base(gameContext, parent)
        {
            ToArmySetup = new StageBinding(this);
        }

        public override void Enter(IGameContext context)
        {
            context.Log($"Entered {nameof(PlayerSetupStage)}.");
            GameContext.GetHandler<IPlayerSetupHandler>()
                .Handle((teams) => OnPlayersSetUp(context, teams));
        }

        private void OnPlayersSetUp(IGameContext context, List<ITeam> teams)
        {
            if(teams.Count != 2)
            {
                throw new ArgumentException($"{nameof(PlayerSetupStage)} doesn't support team count " +
                    $"other than 2. Number of teams provided: {teams.Count}");
            }

            foreach (ITeam team in teams)
            {
                List<DataReference> playerDatas = new List<DataReference>();

                foreach (IPlayerInfo player in team.Players)
                {
                    PlayerData playerData = new PlayerData(player.Name, player.ID);
                    DataReference playerReference = context.GameDataStore.Create(playerData);
                    playerDatas.Add(playerReference);
                }

                TeamData teamData = new TeamData(team.TeamNumber, playerDatas,
                    context.GameDataStore, context.CommandProcessor);

                DataReference teamReference = context.GameDataStore.Create(teamData);
            }

            ToArmySetup.Activate(context);
        }
    }

    public interface IPlayerSetupHandler
    {
        void Handle(Action<List<ITeam>> teamsWithPlayers);
    }
}
