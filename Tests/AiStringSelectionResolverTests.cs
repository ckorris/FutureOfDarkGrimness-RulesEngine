using FDG.Ai.Resolvers;
using FDG.Data;
using FDG.Stages;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    [TestFixture]
    public class AiStringSelectionResolverTests
    {
        // The AI's reserve placement isn't tactical (see AiPlaceObjectsResolver), so it must decline to
        // hold a unit in Ambush and deploy it normally instead. Guards against the prompt-ordering trap
        // where the catch-all "first option" fallback would pick "Hold in Ambush".
        [Test]
        public async Task Resolve_AmbushHoldOrDeploy_AlwaysDeploysNormally()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var player = new PlayerID(System.Guid.NewGuid());
            var resolver = new AiStringSelectionResolver(new TableState(store), player);

            var request = new StringSelectionRequest(player,
                "Deploy Infiltrators now, or hold it in Ambush?",
                new List<string>
                {
                    ChooseUnitToDeployStage.HoldChoiceFor("Ambush"), // listed first — the trap
                    ChooseUnitToDeployStage.DEPLOY_NORMALLY_CHOICE,
                    ChooseUnitToDeployStage.BACK_TO_LIST_CHOICE,
                },
                new List<StringSelectionRequest.InvalidOption>());

            string choice = await resolver.Resolve(request);

            Assert.That(choice, Is.EqualTo(ChooseUnitToDeployStage.DEPLOY_NORMALLY_CHOICE),
                "the AI never holds a unit in reserve — it deploys normally.");
        }
    }
}
