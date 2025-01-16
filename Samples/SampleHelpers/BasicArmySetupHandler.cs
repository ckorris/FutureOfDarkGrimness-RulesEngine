using FDG.Stages;

namespace FDG.Samples.SampleHelpers
{
    public class BasicArmySetupHandler : IArmySetupHandler
    {
        private List<IArmyTemplate> _armiesToChoose;

        public BasicArmySetupHandler(List<IArmyTemplate> armiesToChoose)
        {
            _armiesToChoose = armiesToChoose;
        }

        public void Handle(Action<List<IArmyTemplate>> onArmiesChosen)
        {
            onArmiesChosen.Invoke(_armiesToChoose);
        }
    }
}
