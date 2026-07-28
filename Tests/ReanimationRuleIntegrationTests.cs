using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using FDG.StageResolution;
using NUnit.Framework;

namespace FDG.Tests
{
    // #197 P17d: Reanimation - "when a unit where all models have this rule is activated, roll as many
    // dice as the max. number of models/wounds it could restore. For each 5+ you may restore one
    // model/wound." Owner-ruled 2026-07-28: wounds-first (top up wounded living models, then revive
    // dead ones at one wound each), revives auto-placed in coherency, no per-die prompts. The dice are
    // DECISIVE (binary per-die outcomes - the ClearTokenOnRoll/Storm precedent), so probabilistic
    // games roll real faces here.
    [TestFixture]
    public class ReanimationRuleIntegrationTests
    {
        private const string RuleName = "Reanimation";

        private static SpecialRuleDefinition Definition() => new(RuleName,
            new[]
            {
                new HookEntry(EHookID.Activation_OnActivationStart, new Condition.AllModelsHaveThisRule(),
                    new Effect.RestoreWounds(MinRoll: 5), ELifetime.UntilEndOfGame),
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
        public async Task DeadModels_ReviveAtOneWound_PlacedInCoherency()
        {
            DataBinding<UnitData> unit = MakeUnit(modelCount: 3);
            KillModel(unit, 1);
            KillModel(unit, 2);

            await RunActivationStart(unit, face: 5);   // pool = 2 missing wounds, both dice succeed

            Assert.That(unit.GetValue().Models.Count(m => m.GetIsAlive()), Is.EqualTo(3),
                "both casualties return");
            foreach (IModel model in unit.GetValue().Models.Skip(1))
            {
                Position pos = model.Position;
                float dx = pos.x - 20f, dz = pos.z - 20f;
                Assert.That(MathF.Sqrt(dx * dx + dz * dz), Is.LessThanOrEqualTo(4f),
                    "'may only be restored if they can be placed in coherency' - beside the survivor");
                Assert.That(pos.x == 0f && pos.z == 0f, Is.False);
            }
        }

        [Test]
        public async Task WoundedLivingModels_HealBeforeAnyoneRevives()
        {
            // Model 0 (list-first): a dead 1-wound grunt. Model 1: a living Tough hero at 1/3.
            DataBinding<UnitData> unit = MakeUnit(modelCount: 2, toughSecondModel: 3);
            KillModel(unit, 0);
            ((ModelData)unit.GetValue().Models[1]).DealWounds(2f);

            await RunActivationStart(unit, face: 5);   // pool = 3 missing, all succeed

            ModelData hero = (ModelData)unit.GetValue().Models[1];
            Assert.That(hero.WoundsDealt, Is.EqualTo(0f).Within(0.001f),
                "wounds first (owner-ruled): the living hero tops up before any body returns");
            Assert.That(unit.GetValue().Models[0].GetIsAlive(), Is.True,
                "the remaining success then revives the grunt");
        }

        [Test]
        public async Task FailedDice_RestoreNothing()
        {
            DataBinding<UnitData> unit = MakeUnit(modelCount: 2);
            KillModel(unit, 1);

            await RunActivationStart(unit, face: 4);   // 4 < the 5+ threshold

            Assert.That(unit.GetValue().Models[1].GetIsAlive(), Is.False);
        }

        [Test]
        public async Task AFullStrengthUnit_RollsNothing()
        {
            DataBinding<UnitData> unit = MakeUnit(modelCount: 2);

            await RunActivationStart(unit, face: 5);

            Assert.That(unit.GetValue().Models.All(m => m.GetIsAlive()), Is.True, "nothing to restore, no dice");
        }

        private void KillModel(DataBinding<UnitData> unit, int index)
        {
            var model = (ModelData)unit.GetValue().Models[index];
            model.DealWounds(model.TotalWounds - model.WoundsDealt);
        }

        private DataBinding<UnitData> MakeUnit(int modelCount, int toughSecondModel = 0)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon>(), new Position(20f + i * 1.2f, 20f), _store);
                if (i == 1 && toughSecondModel > 0)
                {
                    model.SetMaxWounds(toughSecondModel);
                }

                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(_player, "Warriors", quality: 4, defense: 4,
                modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            binding.GetValue().AttachRuleDefinition(new ResolvedRule(RuleName, Definition()));

            _store.Create(new ArmyData(_player, new List<DataBinding<UnitData>> { binding }));
            _store.Create(new TeamData(0, new List<PlayerID> { _player }));
            return binding;
        }

        private async Task RunActivationStart(DataBinding<UnitData> unit, int face)
        {
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester(),
                diceRoller: new FixedDiceRoller(face));
            var unitContext = new UnitActionContext(ctx, unit);
            unitContext.Reset(unit);

            var stage = new ActivationStartStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("finish");
            await stage.Enter(unitContext);
        }
    }
}
