using FDG.Ai.Resolvers;
using FDG.Data;
using FDG.Rules.Dispatch;
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

        // Regression: when the AI has already moved (so Charge/Move/Shoot aren't offered) and Pass is
        // GATED OUT (unit rushed -> "must engage"), the only valid option can be one the AI doesn't model
        // (e.g. Cast). The Choose-Action fallback must return a VALID option, not Pass -- returning Pass
        // when it isn't offered faults ChooseActionStage ("Request option was Pass, but that wasn't an option").
        [Test]
        public async Task Resolve_ChooseAction_PassNotOffered_ReturnsValidOptionNotPass()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var player = new PlayerID(System.Guid.NewGuid());
            var resolver = new AiStringSelectionResolver(new TableState(store), player);

            var request = new StringSelectionRequest(player, "Choose Action",
                new List<string> { ChooseActionStage.CAST_CHOICE_NAME },
                new List<StringSelectionRequest.InvalidOption>
                {
                    new StringSelectionRequest.InvalidOption(ChooseActionStage.PASS_CHOICE_NAME, "must engage in melee"),
                });

            string choice = await resolver.Resolve(request);

            Assert.That(choice, Is.EqualTo(ChooseActionStage.CAST_CHOICE_NAME));
            Assert.That(choice, Is.Not.EqualTo(ChooseActionStage.PASS_CHOICE_NAME));
        }

        // #331: the AI never embarks (owner's call - a ride only pays off if someone planned the drop-off).
        // Embark is a rule-NAMED action, so no ranked branch can return it; the position-based tail could,
        // and this pins that it doesn't. Pass isn't offered here, or it would mask the filter.
        [Test]
        public async Task Resolve_ChooseAction_NeverEmbarksFromTheFallback()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var player = new PlayerID(System.Guid.NewGuid());
            var resolver = new AiStringSelectionResolver(new TableState(store), player);

            var request = new StringSelectionRequest(player, "Choose Action",
                new List<string>
                {
                    CoreRuleCatalog.EmbarkRuleName,       // listed first - the trap
                    ChooseActionStage.CAST_CHOICE_NAME,
                },
                new List<StringSelectionRequest.InvalidOption>());

            string choice = await resolver.Resolve(request);

            Assert.That(choice, Is.EqualTo(ChooseActionStage.CAST_CHOICE_NAME),
                "an action it cannot follow through on loses to any other valid action.");
        }

        // ...but the fallback must stay INSIDE ValidOptions: returning something unoffered faults
        // ChooseActionStage, and a fault is worse than one unwanted ride.
        [Test]
        public async Task Resolve_ChooseAction_EmbarkIsTheOnlyOption_StillAnswersWithIt()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var player = new PlayerID(System.Guid.NewGuid());
            var resolver = new AiStringSelectionResolver(new TableState(store), player);

            var request = new StringSelectionRequest(player, "Choose Action",
                new List<string> { CoreRuleCatalog.EmbarkRuleName },
                new List<StringSelectionRequest.InvalidOption>());

            string choice = await resolver.Resolve(request);

            Assert.That(choice, Is.EqualTo(CoreRuleCatalog.EmbarkRuleName));
        }

        // The AI still passes when passing IS allowed and nothing more useful is offered.
        [Test]
        public async Task Resolve_ChooseAction_PassOffered_PassesWhenNothingBetter()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var player = new PlayerID(System.Guid.NewGuid());
            var resolver = new AiStringSelectionResolver(new TableState(store), player);

            var request = new StringSelectionRequest(player, "Choose Action",
                new List<string> { ChooseActionStage.PASS_CHOICE_NAME },
                new List<StringSelectionRequest.InvalidOption>());

            string choice = await resolver.Resolve(request);

            Assert.That(choice, Is.EqualTo(ChooseActionStage.PASS_CHOICE_NAME));
        }
    }
}
