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
    // #197 P17b: Split - "when this unit is fully destroyed, you may place a new unit of X fully within
    // 6\" of it before removing the last model." Rides P17a's whole creation machinery; the only new
    // engine work is the killer-less self-destroyed seam (Lifecycle_OnSelfDestroyed, fired by
    // UnitDestructionNotifier before its killer-attribution early-return, so a rout or
    // dangerous-terrain death still splits - the existing destroyed hook is the KILLER's and requires
    // one). The successor joins the round in progress like any P17 creation.
    [TestFixture]
    public class SplitRuleIntegrationTests
    {
        private const string SpecText = "Changelings [10]";

        private static SpecialRuleDefinition SplitDefinition() => new("Split",
            new[]
            {
                new HookEntry(EHookID.Lifecycle_OnSelfDestroyed, new Condition.Always(),
                    new Effect.SpawnUnit(RadiusInches: 6f), ELifetime.UntilEndOfGame),
            },
            System.Array.Empty<ActivatedAbility>(),
            EngineArgumentCount: 1);

        private GameDataStore _store = null!;
        private RuleResolver _resolver = null!;
        private PlayerID _player;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _resolver = CoreRuleCatalog.CreateResolver();
            _resolver.Register(SplitDefinition());
            _player = new PlayerID(System.Guid.NewGuid());
        }

        [Test]
        public async Task AKillerlessDeath_StillOffersTheSplit_AndPlacesTheSuccessor()
        {
            (DataBinding<UnitData> horrors, DataBinding<ArmyData> army) = MakeSplitterArmy();
            KillEveryModel(horrors);
            var requester = new SplitRequester { Accept = true, DestX = 21f, DestZ = 20f };

            // killer: null is the rout / dangerous-terrain path - the one the killer-seat hook skips.
            await UnitDestructionNotifier.NotifyUnitDestroyed(Ctx(requester), horrors.GetValue(), killer: null);

            Assert.That(requester.YesNoAsked, Is.EqualTo(1), "'you may' - the split is offered");
            Assert.That(army.GetValue().UnitBindings, Has.Count.EqualTo(2), "the successor registered");
            UnitData successor = army.GetValue().UnitBindings[1].GetValue();
            Assert.That(successor.Name, Is.EqualTo("Changelings"));
            Assert.That(successor.Models.Count, Is.EqualTo(10));
            Assert.That(successor.Tokens.HasToken(TokenType.JoinsRoundInProgress), Is.True,
                "a mid-round creation may activate this round (owner-ruled), the destruction path included");

            var zone = requester.PlaceRequest!.DeploymentZone as CircularZone;
            Assert.That(zone, Is.Not.Null, "'fully within 6\" of it' - centred on the corpse");
            Assert.That(zone!.Radius, Is.EqualTo(6f).Within(0.001f));
            Assert.That(zone.Center.X, Is.EqualTo(20f).Within(0.001f),
                "'before removing the last model' - the dead models' positions still give the centre");
        }

        [Test]
        public async Task Declined_PlacesNothing()
        {
            (DataBinding<UnitData> horrors, DataBinding<ArmyData> army) = MakeSplitterArmy();
            KillEveryModel(horrors);

            await UnitDestructionNotifier.NotifyUnitDestroyed(
                Ctx(new SplitRequester { Accept = false }), horrors.GetValue(), killer: null);

            Assert.That(army.GetValue().UnitBindings, Has.Count.EqualTo(1), "declining places nothing");
        }

        [Test]
        public async Task AUnitWithoutSplit_NeverPrompts()
        {
            (DataBinding<UnitData> horrors, _) = MakeSplitterArmy(attachSplit: false);
            KillEveryModel(horrors);
            var requester = new SplitRequester { Accept = true };

            await UnitDestructionNotifier.NotifyUnitDestroyed(Ctx(requester), horrors.GetValue(), killer: null);

            Assert.That(requester.YesNoAsked, Is.EqualTo(0));
        }

        private TriggeredMoveTestContext Ctx(IPlayerRequestByID requester) =>
            new(_store, requester, ruleResolver: _resolver);

        private static void KillEveryModel(DataBinding<UnitData> unit)
        {
            foreach (DataBinding<ModelData> model in unit.GetValue().ModelBindings)
            {
                model.GetValue().DealWounds(model.GetValue().TotalWounds);
            }
        }

        private (DataBinding<UnitData> Splitter, DataBinding<ArmyData> Army) MakeSplitterArmy(
            bool attachSplit = true)
        {
            var model = new ModelData(0.75f, new List<Weapon>(), new Position(20f, 20f), _store);
            var splitter = new UnitData(_player, "Change Horrors", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>>
                {
                    _store.GetDataBinding<ModelData>(_store.Create(model)),
                });
            if (attachSplit)
            {
                splitter.AttachRuleDefinition(new ResolvedRule("Split", SplitDefinition(),
                    new RuleArgument[] { new RuleArgument.Str(SpecText) }));
            }

            DataBinding<UnitData> splitterBinding = _store.GetDataBinding<UnitData>(_store.Create(splitter));

            var aux = new UnitFileEntry
            {
                Name = "Changelings",
                Id = SpecText,
                ModelCount = 10,
                Quality = 5,
                Defense = 6,
                Weapons = new List<WeaponFileEntry>
                {
                    new WeaponFileEntry { Name = "Claws", Quantity = 1, RangeInches = 0, Attacks = 1 },
                },
            };

            var armyData = new ArmyData(_player, new List<DataBinding<UnitData>> { splitterBinding });
            armyData.PersistRuleData(new List<SpecialRuleDefinition>(), new List<SpellDefinition>(),
                new List<UnitFileEntry> { aux });
            DataBinding<ArmyData> armyBinding = _store.GetDataBinding<ArmyData>(_store.Create(armyData));

            _store.Create(new TeamData(0, new List<PlayerID> { _player }));
            return (splitterBinding, armyBinding);
        }

        private sealed class SplitRequester : IPlayerRequestByID
        {
            public bool Accept;
            public float DestX = 21f, DestZ = 20f;
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
