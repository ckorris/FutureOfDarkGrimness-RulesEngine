using FDG.Ai.Resolvers;
using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
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

        // #335 / #191 A5-10: the AI never embarks MID-GAME (the surviving half of #335 - deploy-time
        // loading is now taken, but a mid-game re-board still has no plan behind it).
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

        // #191 A5-10 companion: the solo get-out rule. Cargo disembarks when its loaded transport
        // stands within the trigger distance of an objective (or an enemy) - and keeps riding when
        // the boat is still in transit, which is the pre-A5-10 ride-until-it-dies gap this closes.
        [Test]
        public async Task Resolve_ChooseAction_LoadedTransportNearObjective_Disembarks()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var player = new PlayerID(System.Guid.NewGuid());
            var resolver = new AiStringSelectionResolver(new TableState(store), player);
            MakeLoadedTransport(store, player, transportAt: new Position(20f, 20f));
            store.Create(new ObjectiveData(new Position(26f, 24f), store)); // ~7.2" away

            string choice = await resolver.Resolve(ChooseActionRequest(player,
                CoreRuleCatalog.DisembarkRuleName, ChooseActionStage.PASS_CHOICE_NAME));

            Assert.That(choice, Is.EqualTo(CoreRuleCatalog.DisembarkRuleName),
                "the ride has arrived - an objective is in reach of the wreck-side placement.");
        }

        [Test]
        public async Task Resolve_ChooseAction_LoadedTransportFarFromEverything_KeepsRiding()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var player = new PlayerID(System.Guid.NewGuid());
            var resolver = new AiStringSelectionResolver(new TableState(store), player);
            MakeLoadedTransport(store, player, transportAt: new Position(20f, 20f));
            store.Create(new ObjectiveData(new Position(60f, 44f), store)); // way out of trigger range

            string choice = await resolver.Resolve(ChooseActionRequest(player,
                CoreRuleCatalog.DisembarkRuleName, ChooseActionStage.PASS_CHOICE_NAME));

            Assert.That(choice, Is.EqualTo(ChooseActionStage.PASS_CHOICE_NAME),
                "nothing worth leaving for yet - stay aboard while the transport closes the distance.");
        }

        [Test]
        public async Task Resolve_ChooseAction_EnemyNearTheLoadedTransport_Disembarks()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var player = new PlayerID(System.Guid.NewGuid());
            var enemy = new PlayerID(System.Guid.NewGuid());
            var resolver = new AiStringSelectionResolver(new TableState(store), player);
            MakeLoadedTransport(store, player, transportAt: new Position(20f, 20f));
            MakeUnit(store, enemy, "Raiders", new Position(28f, 20f)); // 8" away

            string choice = await resolver.Resolve(ChooseActionRequest(player,
                CoreRuleCatalog.DisembarkRuleName, ChooseActionStage.PASS_CHOICE_NAME));

            Assert.That(choice, Is.EqualTo(CoreRuleCatalog.DisembarkRuleName),
                "an enemy in reach means get out and fight, not ride past.");
        }

        // --- A5-10 fixtures ---

        private static StringSelectionRequest ChooseActionRequest(PlayerID player, params string[] options) =>
            new StringSelectionRequest(player, "Choose Action", options.ToList(),
                new List<StringSelectionRequest.InvalidOption>());

        private static DataBinding<UnitData> MakeUnit(GameDataStore store, PlayerID owner,
            string name, Position at)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), at, store);
            var modelBindings = new List<DataBinding<ModelData>>
            {
                store.GetDataBinding<ModelData>(store.Create(model)),
            };
            var unit = new UnitData(owner, name, quality: 4, defense: 4, modelBindings: modelBindings);
            return store.GetDataBinding<UnitData>(store.Create(unit));
        }

        // A deployed transport carrying one embarked squad (the squad's models sit at origin,
        // exactly as deploy-time embarking leaves them).
        private static void MakeLoadedTransport(GameDataStore store, PlayerID owner, Position transportAt)
        {
            DataBinding<UnitData> transport = MakeUnit(store, owner, "Rhino", transportAt);
            transport.GetValue().AttachRuleDefinition(new ResolvedRule(
                TransportUtilities.TransportRuleName, CoreRuleCatalog.Transport,
                new RuleArgument[] { new RuleArgument.Int(6) }));
            DataBinding<UnitData> cargo = MakeUnit(store, owner, "Grunts", new Position(0f, 0f));
            TransportUtilities.Embark(cargo.GetValue(), transport.GetValue());
        }
    }
}
