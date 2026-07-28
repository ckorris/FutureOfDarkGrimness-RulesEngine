using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.SaveLoad;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #197 P17: Spawn - "once per game, when this model is activated, you may place a new unit of X
    // fully within 6\" of it." X is the rule instance's TEXT argument (RuleArgument.Str, the growth the
    // type's doc anticipated) naming an auxiliary unit spec the army persisted at load. The creation
    // service builds the new unit through the SAME path a deploying unit takes (UnitData ctor + rule
    // attach + creation-time rules), registers it with the army (which is what replicates it to network
    // clients), places it via the normal placement flow in a 6" circular zone, and marks it to join the
    // round in progress - owner-ruled 2026-07-28: a mid-round creation may activate this round.
    [TestFixture]
    public class SpawnRuleIntegrationTests
    {
        private const string SpecText = "Spores [5]";

        private static SpecialRuleDefinition SpawnDefinition() => new("Spawn",
            System.Array.Empty<HookEntry>(),
            new[]
            {
                new ActivatedAbility(EHookID.Activation_OnActivationStart,
                    new Cost.OncePerGame(),
                    new TargetSelector(RangeInches: 0f, MinCount: 1, MaxCount: 1, ETargetAffinity.Self,
                        RequireLineOfSight: false),
                    new Effect.SpawnUnit(RadiusInches: 6f),
                    new Condition.Always(),
                    Label: "Spawn"),
            },
            EngineArgumentCount: 1);

        private GameDataStore _store = null!;
        private RuleResolver _resolver = null!;
        private PlayerID _player;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _resolver = CoreRuleCatalog.CreateResolver();
            _resolver.Register(SpawnDefinition());
            _player = new PlayerID(System.Guid.NewGuid());
        }

        [Test]
        public async Task Accepted_BuildsPlacesAndRegistersTheNewUnit()
        {
            (DataBinding<UnitData> spawner, DataBinding<ArmyData> army) = MakeSpawnerArmy();
            var requester = new SpawnRequester { Accept = true, DestX = 22f, DestZ = 20f };

            await RunActivationStart(requester, spawner);

            Assert.That(army.GetValue().UnitBindings, Has.Count.EqualTo(2),
                "the spawned unit registered with the army");
            UnitData spawned = army.GetValue().UnitBindings[1].GetValue();
            Assert.That(spawned.Name, Is.EqualTo("Spores"));
            Assert.That(spawned.Models.Count, Is.EqualTo(5), "the spec's [5] sized the unit");
            Assert.That(spawned.PlayerID, Is.EqualTo(_player));
            foreach (IModel model in spawned.Models)
            {
                Assert.That(model.Position.x, Is.EqualTo(22f).Within(0.001f));
            }

            Assert.That(requester.PlaceRequest, Is.Not.Null);
            var zone = requester.PlaceRequest!.DeploymentZone as CircularZone;
            Assert.That(zone, Is.Not.Null, "'fully within 6\" of it' is a circular zone around the placer");
            Assert.That(zone!.Radius, Is.EqualTo(6f).Within(0.001f));

            Assert.That(spawned.Tokens.HasToken(TokenType.JoinsRoundInProgress), Is.True,
                "marked to join the round in progress");
        }

        [Test]
        public async Task TheSpecsRules_AttachAndItsCreationRulesApply()
        {
            (DataBinding<UnitData> spawner, DataBinding<ArmyData> army) = MakeSpawnerArmy(
                auxRules: new List<SpecialRuleEntry> { new SpecialRuleEntry_CoreNumeric("Tough", 3) });

            await RunActivationStart(new SpawnRequester { Accept = true, DestX = 22f, DestZ = 20f }, spawner);

            UnitData spawned = army.GetValue().UnitBindings[1].GetValue();
            Assert.That(spawned.RuleDefinitions.Any(r => r.Definition == CoreRuleCatalog.Tough), Is.True,
                "the aux spec's rules attach through the same path a deploying unit's do");
            Assert.That(spawned.Models[0].TotalWounds, Is.EqualTo(3),
                "creation-time rules (Tough's max wounds) apply to a spawned unit like a deployed one");
        }

        [Test]
        public async Task TheSpawnedUnit_JoinsTheRoundInProgress()
        {
            (DataBinding<UnitData> spawner, DataBinding<ArmyData> army) = MakeSpawnerArmy();
            var ctx = new TriggeredMoveTestContext(_store, new SpawnRequester { Accept = true, DestX = 22f, DestZ = 20f },
                ruleResolver: _resolver);
            var round = new SingleRoundContext(ctx, TeamsOf(_player), roundCount: 1);
            Assert.That(round.UnactivatedUnits[_player], Has.Count.EqualTo(1), "the round starts with one unit");

            await RunActivationStart(ctx, spawner);

            Assert.That(round.DoesPlayerHaveRemainingActivations(_player), Is.True,
                "the pool's own query seam adopts the mid-round creation");
            Assert.That(round.UnactivatedUnits[_player], Has.Count.EqualTo(2),
                "the spawned unit is in this round's pool (owner-ruled: it may activate this round)");
            UnitData spawned = army.GetValue().UnitBindings[1].GetValue();
            Assert.That(spawned.Tokens.HasToken(TokenType.JoinsRoundInProgress), Is.False,
                "adoption spends the marker");
        }

        [Test]
        public async Task OncePerGame_ASecondActivationOffersNothing()
        {
            (DataBinding<UnitData> spawner, DataBinding<ArmyData> army) = MakeSpawnerArmy();
            await RunActivationStart(new SpawnRequester { Accept = true, DestX = 22f, DestZ = 20f }, spawner);

            var second = new SpawnRequester { Accept = true, DestX = 30f, DestZ = 20f };
            await RunActivationStart(second, spawner);

            Assert.That(second.YesNoAsked, Is.EqualTo(0), "the once-per-game gate is spent");
            Assert.That(army.GetValue().UnitBindings, Has.Count.EqualTo(2), "no second spawn");
        }

        [Test]
        public async Task Declined_SavesTheUseForALaterActivation()
        {
            (DataBinding<UnitData> spawner, DataBinding<ArmyData> army) = MakeSpawnerArmy();
            await RunActivationStart(new SpawnRequester { Accept = false }, spawner);

            Assert.That(army.GetValue().UnitBindings, Has.Count.EqualTo(1), "declining spawns nothing");

            var later = new SpawnRequester { Accept = true, DestX = 22f, DestZ = 20f };
            await RunActivationStart(later, spawner);
            Assert.That(later.YesNoAsked, Is.EqualTo(1), "declining did not spend the gate");
            Assert.That(army.GetValue().UnitBindings, Has.Count.EqualTo(2));
        }

        [Test]
        public async Task ANameMatchingNoSpec_WarnsAndSpawnsNothing()
        {
            (DataBinding<UnitData> spawner, DataBinding<ArmyData> army) = MakeSpawnerArmy(
                argText: "No Such Unit [3]");

            await RunActivationStart(new SpawnRequester { Accept = true, DestX = 22f, DestZ = 20f }, spawner);

            Assert.That(army.GetValue().UnitBindings, Has.Count.EqualTo(1),
                "a dangling spec name does nothing rather than throwing mid-stage");
        }

        [Test]
        public void RoundStartSnapshot_SweepsAStrayJoinMarker()
        {
            (DataBinding<UnitData> spawner, _) = MakeSpawnerArmy();
            spawner.GetValue().Tokens.AddToken(Rules.Tokens.TokenDefinitionCatalog.Create(
                TokenType.JoinsRoundInProgress));
            var ctx = new TriggeredMoveTestContext(_store, new SpawnRequester(), ruleResolver: _resolver);

            var round = new SingleRoundContext(ctx, TeamsOf(_player), roundCount: 2);

            Assert.That(spawner.GetValue().Tokens.HasToken(TokenType.JoinsRoundInProgress), Is.False,
                "the fresh scan already includes every living unit, so the stray marker is swept");
            Assert.That(round.UnactivatedUnits[_player], Has.Count.EqualTo(1), "and nothing joins twice");
        }

        // --- scaffolding -----------------------------------------------------------------------------

        private (DataBinding<UnitData> Spawner, DataBinding<ArmyData> Army) MakeSpawnerArmy(
            string argText = SpecText, List<SpecialRuleEntry>? auxRules = null)
        {
            var model = new ModelData(0.75f, new List<Weapon>(), new Position(20f, 20f), _store);
            var spawner = new UnitData(_player, "Spawning Beast", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>>
                {
                    _store.GetDataBinding<ModelData>(_store.Create(model)),
                });
            spawner.AttachRuleDefinition(new ResolvedRule("Spawn", SpawnDefinition(),
                new RuleArgument[] { new RuleArgument.Str(argText) }));
            DataBinding<UnitData> spawnerBinding = _store.GetDataBinding<UnitData>(_store.Create(spawner));

            var aux = new UnitFileEntry
            {
                Name = "Spores",
                Id = SpecText,     // keyed by the rule's exact argument text
                ModelCount = 5,
                Quality = 4,
                Defense = 4,
                SpecialRules = auxRules ?? new List<SpecialRuleEntry>(),
                Weapons = new List<WeaponFileEntry>
                {
                    new WeaponFileEntry { Name = "Spore Burst", Quantity = 1, RangeInches = 12, Attacks = 1 },
                },
            };

            var armyData = new ArmyData(_player, new List<DataBinding<UnitData>> { spawnerBinding });
            armyData.PersistRuleData(new List<SpecialRuleDefinition>(), new List<SpellDefinition>(),
                new List<UnitFileEntry> { aux });
            DataBinding<ArmyData> armyBinding = _store.GetDataBinding<ArmyData>(_store.Create(armyData));

            _store.Create(new TeamData(0, new List<PlayerID> { _player }));
            return (spawnerBinding, armyBinding);
        }

        private static List<ITeam> TeamsOf(PlayerID player) =>
            new() { new TeamData(0, new List<PlayerID> { player }) };

        private async Task RunActivationStart(IPlayerRequestByID requester, DataBinding<UnitData> unit)
        {
            var ctx = new TriggeredMoveTestContext(_store, requester, ruleResolver: _resolver);
            await RunActivationStart(ctx, unit);
        }

        private static async Task RunActivationStart(TriggeredMoveTestContext ctx, DataBinding<UnitData> unit)
        {
            var unitContext = new UnitActionContext(ctx, unit);
            unitContext.Reset(unit);

            var stage = new ActivationStartStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("finish");
            await stage.Enter(unitContext);
        }

        // Answers the activation-start "Use Spawn?" and the spawn placement, counting the Yes/No prompts.
        private sealed class SpawnRequester : IPlayerRequestByID
        {
            public bool Accept;
            public float DestX = 20f, DestZ = 20f;
            public int YesNoAsked;
            public PlaceObjectsRequest<ModelData>? PlaceRequest;

            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply>
            {
                switch (request)
                {
                    case YesNoRequest:
                        YesNoAsked++;
                        return Task.FromResult((TReply)(object)Accept);
                    case PlaceObjectsRequest<ModelData> place:
                        PlaceRequest = place;
                        var dest = new Position(DestX, DestZ);
                        var entries = place.ModelsToPlace
                            .Select(m => new PlacedObjectEntry<ModelData>(m, dest))
                            .ToList();
                        return Task.FromResult(
                            (TReply)(object)new Selected<List<PlacedObjectEntry<ModelData>>>(entries));
                    default:
                        throw new System.InvalidOperationException(
                            "Unexpected request: " + request.GetType());
                }
            }
        }
    }
}
