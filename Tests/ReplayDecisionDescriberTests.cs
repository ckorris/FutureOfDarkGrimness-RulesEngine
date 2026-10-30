using FDG.Data;
using FDG.Network;
using FDG.Replay;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using Newtonsoft.Json;
using NUnit.Framework;

namespace FDG.Tests
{
    // #454: one line per handled request type. Requests and replies are real objects serialized exactly as
    // they go on the wire (RequestMessageSender / StageResolverRegistry), so each line is read from the JSON
    // shape a recording holds; the wound reply, which only an attack resolution builds, is a recorded shape.
    [TestFixture]
    public class ReplayDecisionDescriberTests
    {
        private static readonly PlayerID PlayerA = new PlayerID(Guid.NewGuid());
        private static readonly PlayerID PlayerB = new PlayerID(Guid.NewGuid());

        private GameDataStore _store = null!;
        private DataReference _warriors;
        private DataReference _grunts;
        private List<DataReference> _warriorModels = null!;
        private List<DataReference> _gruntModels = null!;

        private readonly ReplayHeader _header = new ReplayHeader
        {
            Settings = GameSettings.GetDefault(),
            Slots = new List<ReplaySlot>
            {
                new ReplaySlot { SlotId = 0, PlayerId = PlayerA, Name = "Alpha" },
                new ReplaySlot { SlotId = 1, PlayerId = PlayerB, Name = "Bravo" },
            },
        };

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            (_warriors, _warriorModels) = CreateUnit(PlayerA, "Warriors", models: 5, x: 5);
            (_grunts, _gruntModels) = CreateUnit(PlayerB, "Grunts", models: 3, x: 30);
        }

        private (DataReference Unit, List<DataReference> Models) CreateUnit(PlayerID player, string name, int models, float x)
        {
            var modelRefs = new List<DataReference>();
            for (int i = 0; i < models; i++)
            {
                modelRefs.Add(_store.Create(new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
                    initialPosition: new Position(x + i, 5), gameDataStore: _store)));
            }

            DataReference unitRef = _store.Create(new UnitData(player, name, quality: 4, defense: 4,
                modelBindings: modelRefs.Select(_store.GetDataBinding<ModelData>).ToList()));
            return (unitRef, modelRefs);
        }

        private DataBinding<UnitData> Unit(DataReference reference) => _store.GetDataBinding<UnitData>(reference);

        private string Describe<TReply>(IStageTaskRequest<TReply> request, TReply reply, DataReference? activatingUnit = null) =>
            Describe(request, JsonConvert.SerializeObject(reply, typeof(TReply), WireJsonSettings.For(_store)), activatingUnit, typeof(TReply));

        private string Describe(IStageTaskRequest request, string replyJson, DataReference? activatingUnit = null, Type? replyType = null)
        {
            var asked = new RequestEvent(request.TaskID, request.TargetPlayerID, request.GetType().FullName!,
                replyType?.FullName ?? "System.Object", request.DisplayName,
                JsonConvert.SerializeObject(request, WireJsonSettings.For(_store))) { Seq = 0 };
            var recorded = new ReplayRequest(asked, round: 1, activation: 1, activatingUnit)
            {
                Reply = new ReplyEvent(request.TaskID, replyJson) { Seq = 1 },
            };

            string line = ReplayDecisionDescriber.Describe(recorded, _store, _header);
            Assert.That(line.All(c => c >= ' ' && c <= '~'), Is.True, $"ASCII: {line}");
            return line;
        }

        [Test]
        public void ChooseUnitToActivate_NamesThePlayerTheUnitAndItsLivingModels()
        {
            _store.GetValue<ModelData>(_warriorModels[0]).RemainingWoundsBinding.SetValue(0f);
            var request = new ChooseUnitToActivateRequest(PlayerA,
                new[] { new SelectionRequest<UnitData>.ValidOption(Unit(_warriors), "Warriors") },
                Array.Empty<SelectionRequest<UnitData>.InvalidOption>());

            Assert.That(Describe(request, Unit(_warriors)), Is.EqualTo("P1 activates Warriors (4)"));
        }

        [Test]
        public void ChooseAction_NamesTheUnitAndTheAction()
        {
            var request = new ChooseActionRequest(PlayerA, _store.GetValue<UnitData>(_warriors).ID,
                new[] { "Move", "Shoot" }, Array.Empty<StringSelectionRequest.InvalidOption>());

            Assert.That(Describe(request, "Shoot"), Is.EqualTo("Warriors: Shoot"));
        }

        [Test]
        public void ChooseRangedAttack_NamesTargetAndWeapon_HoldFire_AndBack()
        {
            var request = new ChooseRangedAttackRequest(PlayerA, "Choosing a Ranged Weapon", Unit(_warriors),
                new List<ChooseRangedAttackRequest.WeaponOption>());
            var rifle = new Weapon("Rifle", 24f, 1, 0);

            Assert.That(Describe<CancellableResult<ChooseRangedAttackRequest.RangedAttackChoice>>(request,
                    new Selected<ChooseRangedAttackRequest.RangedAttackChoice>(new(rifle, Unit(_grunts)))),
                Is.EqualTo("Warriors shoot Grunts with Rifle"));
            Assert.That(Describe<CancellableResult<ChooseRangedAttackRequest.RangedAttackChoice>>(request,
                    new Selected<ChooseRangedAttackRequest.RangedAttackChoice>(new(rifle, null))),
                Is.EqualTo("Warriors hold fire with Rifle"));
            Assert.That(Describe<CancellableResult<ChooseRangedAttackRequest.RangedAttackChoice>>(request,
                    new Cancelled<ChooseRangedAttackRequest.RangedAttackChoice>()),
                Is.EqualTo("Warriors: Back"));
        }

        [Test]
        public void DefineMovementPath_MeasuresTheLongestModelPathFromWhereItStood()
        {
            var request = new DefineMovementPathRequest(PlayerA, "Moving Warriors", Unit(_warriors), 6f, 12f, 12f);
            // Model 0 stands at (5, 5): 3-4-5 to (8, 9), then 1 more to (9, 9). Model 1 moves 2.
            var moves = new List<ModelMoveEntry>
            {
                new(_store.GetDataBinding<ModelData>(_warriorModels[0]), new List<Position> { new(8, 9), new(9, 9) }),
                new(_store.GetDataBinding<ModelData>(_warriorModels[1]), new List<Position> { new(8, 5) }),
            };

            Assert.That(Describe<CancellableResult<List<ModelMoveEntry>>>(request, new Selected<List<ModelMoveEntry>>(moves)),
                Is.EqualTo("Warriors move 6.0in"));
            Assert.That(Describe<CancellableResult<List<ModelMoveEntry>>>(request, new Selected<List<ModelMoveEntry>>(new())),
                Is.EqualTo("Warriors stay put"));
            Assert.That(Describe<CancellableResult<List<ModelMoveEntry>>>(request, new Cancelled<List<ModelMoveEntry>>()),
                Is.EqualTo("Warriors: Back"));
        }

        [Test]
        public void ChooseMeleeDefender_NamesTheActivatingUnitAndItsTarget()
        {
            var request = new ChooseMeleeDefenderRequest(PlayerA, "Charge whom?",
                new[] { new CancellableSelectionRequest<UnitData>.ValidOption(Unit(_grunts), "Grunts") },
                Array.Empty<CancellableSelectionRequest<UnitData>.InvalidOption>());

            Assert.That(Describe<CancellableResult<DataBinding<UnitData>>>(request,
                new Selected<DataBinding<UnitData>>(Unit(_grunts)), activatingUnit: _warriors), Is.EqualTo("Warriors charge Grunts"));
            Assert.That(Describe<CancellableResult<DataBinding<UnitData>>>(request,
                new Selected<DataBinding<UnitData>>(Unit(_grunts))), Is.EqualTo("Charge Grunts"), "no activation named yet");
        }

        [Test]
        public void AssignWounds_CountsWoundsAndTheModelsTheyLandedOn()
        {
            var request = new AssignWoundsRequest(PlayerB, "Assigning Wounds", Unit(_grunts), new List<WoundPacket>());
            string Ref(DataReference r) => $"{{\"TypeID\":{{\"ID\":{r.TypeID.ID}}},\"Index\":{r.Index},\"Generation\":{r.Generation}}}";
            string reply = "{\"Packets\":[{\"Wounds\":3.0,\"Confined\":false,\"Weight\":1.0,\"OriginalWounds\":3.0,\"Ignored\":0.0," +
                "\"WeightedWounds\":3.0}],\"PacketsCommitted\":1,\"HeadPoured\":0.0,\"WoundsLostSoFar\":0.0,\"Commits\":[" +
                $"{{\"PacketIndex\":0,\"Model\":{Ref(_gruntModels[0])},\"Landed\":1.0,\"Lost\":0.0}}," +
                $"{{\"PacketIndex\":0,\"Model\":{Ref(_gruntModels[1])},\"Landed\":2.0,\"Lost\":1.0}}," +
                $"{{\"PacketIndex\":0,\"Model\":{Ref(_gruntModels[2])},\"Landed\":0.0,\"Lost\":0.0}}" +
                "],\"_heroModelId\":null,\"PendingWounds\":[]}";

            Assert.That(Describe(request, reply), Is.EqualTo("3 wounds -> Grunts: 2 models"));
        }

        [Test]
        public void YesNo_StringSelection_AbilityEffect_AndNamedSelections_ReadAsDisplayNameAndChoice()
        {
            Assert.That(Describe(new YesNoRequest(PlayerB, "Strike back?", displayName: "Deciding Whether to Strike Back"), false),
                Is.EqualTo("Deciding Whether to Strike Back: No"));

            var weapon = new StringSelectionRequest(PlayerA, "Pick", new[] { "CCW", "Claws" },
                Array.Empty<StringSelectionRequest.InvalidOption>(), displayName: "Choosing a Melee Weapon");
            Assert.That(Describe(weapon, "Claws"), Is.EqualTo("Choosing a Melee Weapon: Claws"));

            var effect = new ChooseAbilityEffectRequest(PlayerA, "Pick", "Rule", "Bearer",
                new[] { new ChooseAbilityEffectRequest.EffectOption("AP(+1)", ""), new ChooseAbilityEffectRequest.EffectOption("Rending", "") },
                allowCancel: true);
            Assert.That(Describe(effect, 1), Is.EqualTo("Choosing an Effect: Rending"));
            Assert.That(Describe(effect, ChooseAbilityEffectRequest.CANCEL), Is.EqualTo("Choosing an Effect: Back"));

            var deploy = new SelectionRequest<UnitData>(PlayerA, "Deploy which?",
                new[] { new SelectionRequest<UnitData>.ValidOption(Unit(_warriors), "Warriors"),
                        new SelectionRequest<UnitData>.ValidOption(Unit(_grunts), "Grunts") },
                Array.Empty<SelectionRequest<UnitData>.InvalidOption>(), displayName: "Choosing a Unit to Deploy");
            Assert.That(Describe(deploy, Unit(_grunts)), Is.EqualTo("Choosing a Unit to Deploy: Grunts"));

            var redeploy = new CancellableSelectionRequest<UnitData>(PlayerA, "Redeploy which?",
                new[] { new CancellableSelectionRequest<UnitData>.ValidOption(Unit(_warriors), "Warriors") },
                Array.Empty<CancellableSelectionRequest<UnitData>.InvalidOption>(), displayName: "Choosing a Unit to Redeploy");
            Assert.That(Describe<CancellableResult<DataBinding<UnitData>>>(redeploy, new Selected<DataBinding<UnitData>>(Unit(_warriors))),
                Is.EqualTo("Choosing a Unit to Redeploy: Warriors"));
            Assert.That(Describe<CancellableResult<DataBinding<UnitData>>>(redeploy, new Cancelled<DataBinding<UnitData>>()),
                Is.EqualTo("Choosing a Unit to Redeploy: Back"));
        }

        [Test]
        public void PlaceObjective_GivesTheSpot()
        {
            var request = new PlaceObjectiveRequest(PlayerA, "Placing Objective 1 of 5", 1, 5,
                new RectangularZone(0f, 72f, 12f, 36f), 9f);

            Assert.That(Describe(request, new Position(68f, 25.5f)), Is.EqualTo("Placing Objective 1 of 5 at (68, 25.5)"));
        }

        [Test]
        public void AGarbageReply_OrAnUnknownRequest_FallsBackToTheDisplayName()
        {
            var request = new ChooseActionRequest(PlayerA, _store.GetValue<UnitData>(_warriors).ID,
                new[] { "Move", "Shoot" }, Array.Empty<StringSelectionRequest.InvalidOption>());

            // Raw reply JSON, not a string answer.
            Assert.That(Describe((IStageTaskRequest)request, "{not json"), Is.EqualTo("Choosing an Action"));
            Assert.That(Describe((IStageTaskRequest)request, "{\"unexpected\":[1,2]}"), Is.EqualTo("Choosing an Action"));

            var deployZone = new ChooseDeploymentZoneRequest(PlayerA, "Choosing a Deployment Zone",
                new List<DataBinding<RectangularZone>>(), new List<DataBinding<RectangularZone>>());
            Assert.That(Describe((IStageTaskRequest)deployZone, "{}"), Is.EqualTo("Choosing a Deployment Zone"), "a type with no line of its own");
        }
    }
}
