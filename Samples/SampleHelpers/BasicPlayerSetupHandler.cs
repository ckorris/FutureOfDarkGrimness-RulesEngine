using FDG.Stages;

namespace FDG.Samples.SampleHelpers
{
    public class BasicPlayerSetupHandler : IPlayerSetupHandler
    {
        private List<ITeam> _teamsWithPlayers;

        public BasicPlayerSetupHandler(List<ITeam> teamsWithPlayers)
        {
            _teamsWithPlayers = teamsWithPlayers;
        }

        public void Handle(Action<List<ITeam>> teamsWithPlayers)
        {
            teamsWithPlayers.Invoke(_teamsWithPlayers);
        }
    }
}
