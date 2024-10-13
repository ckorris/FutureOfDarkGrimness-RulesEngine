
namespace FDG.Stages
{

    public interface IMeleeContext : ICommonContextItems
    {
        public IOfferStrikeBackHandler OfferStrikeBackHandler {get;}
    }

    public class MeleeContext : IMeleeContext
    {
        public ITextOutput TextOutput { get; private set; }

        public IOfferStrikeBackHandler OfferStrikeBackHandler { get; private set; }

        public IDiceRoller DiceRoller { get; private set; }

        public MeleeContext(IOfferStrikeBackHandler offerStrikeBackHandler, ITextOutput textOutput, IDiceRoller diceRoller)
        {
            OfferStrikeBackHandler = offerStrikeBackHandler;
            TextOutput = textOutput;
            DiceRoller = diceRoller;
        }
    }
}