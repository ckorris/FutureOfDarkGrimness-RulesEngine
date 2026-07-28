using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #197 P22: Rapid Ambush - "counts as having Ambush, but may be deployed at the start of any round,
    // including the first". DeferDeployment gained MinArrivalRound (default 2, so core Ambush and every
    // pre-existing LaterRound authoring keep their round-2 gate untouched); the round-start arrival pass
    // now runs every round and gates PER UNIT. AmbushRuleIntegrationTests pins the core rule's round-1
    // hold; this fixture pins the delta.
    [TestFixture]
    public class RapidAmbushRuleIntegrationTests
    {
        private static readonly SpecialRuleDefinition RapidAmbush = new("Rapid Ambush",
            new[]
            {
                new HookEntry(EHookID.Deployment_OnPreDeploymentSelect, new Condition.Always(),
                    new Effect.DeferDeployment(EDeferTiming.LaterRound, PlacementRangeInches: 9f,
                        MinArrivalRound: 1),
                    ELifetime.UntilEndOfGame),
            },
            System.Array.Empty<ActivatedAbility>());

        private GameDataStore _store = null!;
        private PlayerID _player;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _player = new PlayerID(System.Guid.NewGuid());
        }

        [Test]
        public async Task RoundOne_RapidAmbushArrives_CoreAmbushIsStillHeld()
        {
            DataBinding<UnitData> rapid = MakeReservedUnit("Rapid", RapidAmbush);
            DataBinding<UnitData> core = MakeReservedUnit("Core", CoreRuleCatalog.Ambush);
            var requester = new AmbushArrivalRequester(accept: true, destX: 20f, destZ: 20f);

            await RunStage(requester, roundCount: 1);

            Assert.That(ReserveRules.IsInReserve(rapid.GetValue()), Is.False,
                "'may be deployed at the start of any round, including the first'");
            Assert.That(rapid.GetValue().Tokens.HasToken(TokenType.ArrivedFromReserve), Is.True);
            Assert.That(requester.PlaceRequest!.MinDistanceFromEnemiesInches, Is.EqualTo(9f).Within(0.001f),
                "it still 'counts as having Ambush' - the over-9\" arrival constraint holds");

            Assert.That(ReserveRules.IsInReserve(core.GetValue()), Is.True,
                "the gate is PER UNIT: core Ambush keeps its round-2 earliest arrival");
        }

        [Test]
        public async Task RoundTwo_RapidAmbushHeldAtRoundOne_IsOfferedAgain()
        {
            DataBinding<UnitData> rapid = MakeReservedUnit("Rapid", RapidAmbush);

            await RunStage(new AmbushArrivalRequester(accept: false, destX: 20f, destZ: 20f), roundCount: 1);
            Assert.That(ReserveRules.IsInReserve(rapid.GetValue()), Is.True, "declining round 1 holds it");

            await RunStage(new AmbushArrivalRequester(accept: true, destX: 20f, destZ: 20f), roundCount: 2);
            Assert.That(ReserveRules.IsInReserve(rapid.GetValue()), Is.False,
                "'any round' - a declined round-1 arrival is offered again like any Ambush");
        }

        [Test]
        public void MinArrivalRound_DefaultsToTwo_SoEveryExistingAuthoringIsUntouched()
        {
            var operations = new List<RuleOperation>();
            new Effect.DeferDeployment(EDeferTiming.LaterRound, PlacementRangeInches: 9f)
                .Apply(null!, operations);

            var defer = (RuleOperation.DeferDeployment)operations.Single();
            Assert.That(defer.MinArrivalRound, Is.EqualTo(2),
                "an authoring that never names the round keeps the core Ambush gate");
        }

        private async Task RunStage(IPlayerRequestByID requester, int roundCount)
        {
            var ctx = new TriggeredMoveTestContext(_store, requester);
            var stage = new StartOfRoundExtraActionStage(ctx, new NoOpLayer<IMainPhaseContext>());
            stage.OnFinished.Bind("done");
            await stage.Enter(new TestMainPhaseContext(ctx, roundCount));
        }

        private DataBinding<UnitData> MakeReservedUnit(string name, SpecialRuleDefinition rule)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < 2; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon>(), new Position(0f, 0f), _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(_player, name, quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            binding.GetValue().AttachRuleDefinition(new ResolvedRule(rule.Name, rule));
            ReserveRules.PlaceInReserve(binding.GetValue());

            _store.Create(new ArmyData(_player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }
    }
}
