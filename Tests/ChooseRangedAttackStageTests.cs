using FDG.Data;
using FDG.Players;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Presentation;
using FDG.TempVisuals;
using Newtonsoft.Json;
using NUnit.Framework;
using FDG.Rules.Dispatch;
using static FDG.StageResolution.Requests.ChooseRangedAttackRequest;

namespace FDG.Tests
{
    [TestFixture]
    public class ChooseRangedAttackStageTests
    {
        // ──────────────────────────────────────────────────────────────────────
        // Pure data tests — no stage entry required.
        // ──────────────────────────────────────────────────────────────────────

        [Test]
        public void RegisterAttackedDefender_DedupesByReference()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var playerID = new PlayerID(Guid.NewGuid());
            var attackerBinding = MakeUnit(store, playerID, "Attacker",
                new[] { MakeModel(store, new Position(0, 0, 0), Rifle()) });
            var enemyBinding = MakeUnit(store, new PlayerID(Guid.NewGuid()), "Enemy",
                new[] { MakeModel(store, new Position(5, 0, 0)) });

            var ctx = new CombatActionContext(
                gameContext: new TestGameContext(store, new FixedDiceRoller(4)),
                attackingUnit: attackerBinding, isMelee: false);

            ctx.RegisterAttackedDefender(enemyBinding);
            ctx.RegisterAttackedDefender(enemyBinding); // same reference — must dedupe

            Assert.That(ctx.AttackedDefenderRefs.Count, Is.EqualTo(1));
            Assert.That(ctx.AttackedDefenderRefs, Does.Contain(enemyBinding.Reference));
        }

        [Test]
        public void WeaponTargetStats_UnselectableReason_RoundTripsThroughJson()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var playerID = new PlayerID(Guid.NewGuid());
            var attackerBinding = MakeUnit(store, playerID, "Attacker",
                new[] { MakeModel(store, new Position(0, 0, 0), Rifle()) });
            var targetBinding = MakeUnit(store, playerID, "Target",
                new[] { MakeModel(store, new Position(5, 0, 0)) });

            var targetStats = new WeaponTargetStats(targetBinding,
                new HashSet<DataBinding<ModelData>>(), new HashSet<DataBinding<ModelData>>(),
                HasCover: false, UnselectableReason: "Already targeting 2 units this shoot action.");
            var weaponOpt = new WeaponOption(Rifle(), new List<WeaponTargetStats> { targetStats });
            var request = new ChooseRangedAttackRequest(playerID, "ChooseRanged",
                attackerBinding, new List<WeaponOption> { weaponOpt });

            string json = JsonConvert.SerializeObject(request, store.GetJsonSettings());
            var deserialized = JsonConvert.DeserializeObject<ChooseRangedAttackRequest>(json, store.GetJsonSettings());

            Assert.That(deserialized!.WeaponOptions[0].WeaponTargetStats[0].UnselectableReason,
                Is.EqualTo("Already targeting 2 units this shoot action."));
        }

        // ──────────────────────────────────────────────────────────────────────
        // HasAnyFireableTarget — used by ChooseActionStage to gray out Shoot.
        // ──────────────────────────────────────────────────────────────────────

        [Test]
        public void HasAnyFireableTarget_ReturnsFalse_WhenAllEnemiesOutOfRange()
        {
            var (ctx, attackerBinding) = BuildTwoTeamWorld(
                attackerPos: new Position(0, 0, 0),
                enemyPositions: new[] { new Position(100, 0, 0) }, // 100" away, rifle is 24"
                rifleRange: 24f);

            Assert.That(ChooseRangedAttackStage.HasAnyFireableTarget(attackerBinding, ctx), Is.False);
        }

        [Test]
        public void HasAnyFireableTarget_ReturnsTrue_WhenEnemyInRange()
        {
            var (ctx, attackerBinding) = BuildTwoTeamWorld(
                attackerPos: new Position(0, 0, 0),
                enemyPositions: new[] { new Position(5, 0, 0) },
                rifleRange: 24f);

            Assert.That(ChooseRangedAttackStage.HasAnyFireableTarget(attackerBinding, ctx), Is.True);
        }

        [Test]
        public void HasAnyFireableTarget_HandlesMultipleModelsWithSameNamedWeapon()
        {
            // GetRangedWeapons returns one Weapon entry per model that has it. The targeting
            // helper must dedupe by name — otherwise BuildWeaponOptions throws on the duplicate
            // dictionary key and silently faults whoever called us (e.g. ChooseActionStage).
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var ctx = new TestGameContextWithRequester(store, new CapturingRangedRequester());
            var attackerPlayer = new PlayerID(Guid.NewGuid());
            var enemyPlayer    = new PlayerID(Guid.NewGuid());
            store.Create(new TeamData(0, new List<PlayerID> { attackerPlayer }));
            store.Create(new TeamData(1, new List<PlayerID> { enemyPlayer }));

            // Five attacker models, each with its own Rifle instance (same name).
            var attackerModels = new List<DataBinding<ModelData>>();
            for (int i = 0; i < 5; i++)
                attackerModels.Add(MakeModel(store, new Position(i, 0, 0), Rifle()));
            var attackerUnit = MakeUnit(store, attackerPlayer, "Attacker", attackerModels);
            store.Create(new ArmyData(attackerPlayer, new List<DataBinding<UnitData>> { attackerUnit }));

            var enemyUnit = MakeUnit(store, enemyPlayer, "Enemy",
                new[] { MakeModel(store, new Position(10, 0, 0)) });
            store.Create(new ArmyData(enemyPlayer, new List<DataBinding<UnitData>> { enemyUnit }));

            Assert.That(ChooseRangedAttackStage.HasAnyFireableTarget(attackerUnit, ctx), Is.True);
        }

        // ──────────────────────────────────────────────────────────────────────
        // ChooseRangedAttackStage.Enter — integration through a captured layer.
        // ──────────────────────────────────────────────────────────────────────

        [Test]
        public async Task Enter_TwoTargetsAlreadyAttacked_MarksThirdAsUnselectable()
        {
            var requester = new CapturingRangedRequester { Reply = _ => new Cancelled<RangedAttackChoice>() };
            var (ctx, attackerBinding) = BuildTwoTeamWorld(
                attackerPos: new Position(0, 0, 0),
                enemyPositions: new[] { new Position(2, 0, 0), new Position(3, 0, 0), new Position(4, 0, 0) },
                rifleRange: 24f,
                playerRequester: requester);

            var combatCtx = new CombatActionContext(ctx, attackerBinding, isMelee: false);
            // Mark first two enemies as already attacked this shoot action.
            var enemies = ctx.GameDataStore.GetAllDataBindings<ArmyData>()
                .First(a => a.GetValue().PlayerID != attackerBinding.GetValue().PlayerID)
                .GetValue().UnitBindings;
            combatCtx.RegisterAttackedDefender(enemies[0]);
            combatCtx.RegisterAttackedDefender(enemies[1]);

            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            BindAllStageEvents(stage);
            await stage.Enter(combatCtx);

            Assert.That(requester.Captured, Is.Not.Null, "Resolver should have been called.");
            var targetsForRifle = requester.Captured!.WeaponOptions.Single().WeaponTargetStats;
            string? reasonForEnemy0 = targetsForRifle.Single(t => t.TargetUnit.Reference.Equals(enemies[0].Reference)).UnselectableReason;
            string? reasonForEnemy1 = targetsForRifle.Single(t => t.TargetUnit.Reference.Equals(enemies[1].Reference)).UnselectableReason;
            string? reasonForEnemy2 = targetsForRifle.Single(t => t.TargetUnit.Reference.Equals(enemies[2].Reference)).UnselectableReason;
            Assert.That(reasonForEnemy0, Is.Null);
            Assert.That(reasonForEnemy1, Is.Null);
            Assert.That(reasonForEnemy2, Is.Not.Null);
            Assert.That(reasonForEnemy2, Does.Contain("Already targeting"));
        }

        [Test]
        public async Task Enter_NoFireableTargets_NoPriorFire_ActivatesBackToChooseAction()
        {
            var requester = new CapturingRangedRequester();
            var (ctx, attackerBinding) = BuildTwoTeamWorld(
                attackerPos: new Position(0, 0, 0),
                enemyPositions: new[] { new Position(100, 0, 0) },
                rifleRange: 24f,
                playerRequester: requester);

            var combatCtx = new CombatActionContext(ctx, attackerBinding, isMelee: false);
            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            BindAllStageEvents(stage);

            bool backActivated = false;
            bool noShotsActivated = false;
            stage.BackToChooseAction.OnWillActivate += _ => backActivated = true;
            stage.OnNoValidShots.OnWillActivate    += _ => noShotsActivated = true;

            await stage.Enter(combatCtx);

            Assert.That(requester.Captured, Is.Null, "Resolver must not be called when no options are fireable.");
            Assert.That(backActivated, Is.True);
            Assert.That(noShotsActivated, Is.False);
        }

        [Test]
        public async Task Enter_NoFireableTargets_AfterFire_ActivatesOnNoValidShots()
        {
            var requester = new CapturingRangedRequester();
            // Attacker has a rifle AND a pistol — distinct names so they aren't deduped by WeaponComparer.
            // We'll mark the rifle as already used; the pistol remains "available" but still can't hit anyone.
            var rifle  = Rifle();
            var pistol = new Weapon("Pistol", 12f, 1, 0, new HashSet<ISpecialRule_Weapon>());
            var (ctx, attackerBinding) = BuildTwoTeamWorld(
                attackerPos: new Position(0, 0, 0),
                enemyPositions: new[] { new Position(100, 0, 0) },
                rifleRange: 24f,
                attackerWeapons: new[] { rifle, pistol },
                playerRequester: requester);

            var combatCtx = new CombatActionContext(ctx, attackerBinding, isMelee: false);
            // Consume the rifle to simulate a prior fire.
            Weapon consumeMe = combatCtx.AvailableWeapons.Keys.First(w => w.Name == "Rifle");
            combatCtx.SetAttackWeapon(consumeMe, out _);

            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            BindAllStageEvents(stage);
            bool backActivated = false;
            bool noShotsActivated = false;
            stage.BackToChooseAction.OnWillActivate += _ => backActivated = true;
            stage.OnNoValidShots.OnWillActivate    += _ => noShotsActivated = true;

            await stage.Enter(combatCtx);

            Assert.That(requester.Captured, Is.Null);
            Assert.That(noShotsActivated, Is.True);
            Assert.That(backActivated, Is.False);
        }

        // ──────────────────────────────────────────────────────────────────────
        // Helpers
        // ──────────────────────────────────────────────────────────────────────

        // StageBinding.Activate refuses to fire if the binding hasn't been .Bind()'d to a target.
        // The NoOpLayer drops every transition on the floor, so the event names are irrelevant —
        // we just need *something* bound.
        private static void BindAllStageEvents(ChooseRangedAttackStage stage)
        {
            stage.OnChoseWeapon.Bind("test-on-chose-weapon");
            stage.BackToChooseAction.Bind("test-back-to-choose-action");
            stage.OnNoValidShots.Bind("test-on-no-valid-shots");
        }

        private static Weapon Rifle(float range = 24f) =>
            new Weapon("Rifle", range, 1, 0, new HashSet<ISpecialRule_Weapon>());

        private static DataBinding<ModelData> MakeModel(GameDataStore store, Position position, params Weapon[] weapons)
        {
            var model = new ModelData(
                baseRadiusInches: 0.5f,
                weapons: weapons.ToList(),
                specialRules: new List<SpecialRule>(),
                initialPosition: position,
                gameDataStore: store);
            var modelRef = store.Create(model);
            return store.GetDataBinding<ModelData>(modelRef);
        }

        private static DataBinding<UnitData> MakeUnit(GameDataStore store, PlayerID playerID, string name,
            IEnumerable<DataBinding<ModelData>> models)
        {
            var unit = new UnitData(playerID, name, quality: 4, defense: 4,
                specialRules: new List<SpecialRule>(),
                modelBindings: models.ToList());
            var unitRef = store.Create(unit);
            return store.GetDataBinding<UnitData>(unitRef);
        }

        private (TestGameContextWithRequester ctx, DataBinding<UnitData> attacker) BuildTwoTeamWorld(
            Position attackerPos, Position[] enemyPositions, float rifleRange,
            Weapon[]? attackerWeapons = null,
            IPlayerRequestByID? playerRequester = null)
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var attackerPlayer = new PlayerID(Guid.NewGuid());
            var enemyPlayer    = new PlayerID(Guid.NewGuid());

            var ctx = new TestGameContextWithRequester(store,
                playerRequester ?? new CapturingRangedRequester());

            // Teams — DataState auto-tracks anything created through the store.
            store.Create(new TeamData(0, new List<PlayerID> { attackerPlayer }));
            store.Create(new TeamData(1, new List<PlayerID> { enemyPlayer }));

            // Attacker army (1 unit with one model holding the given weapons)
            var attackerWeaponList = (attackerWeapons ?? new[] { Rifle(rifleRange) }).ToList();
            var attackerModel = MakeModel(store, attackerPos, attackerWeaponList.ToArray());
            var attackerUnit  = MakeUnit(store, attackerPlayer, "Attacker", new[] { attackerModel });
            store.Create(new ArmyData(attackerPlayer, new List<DataBinding<UnitData>> { attackerUnit }));

            // Enemy army: one unit per enemy position, each with a single model.
            var enemyUnits = new List<DataBinding<UnitData>>();
            for (int i = 0; i < enemyPositions.Length; i++)
            {
                var enemyModel = MakeModel(store, enemyPositions[i]);
                enemyUnits.Add(MakeUnit(store, enemyPlayer, $"Enemy{i}", new[] { enemyModel }));
            }
            store.Create(new ArmyData(enemyPlayer, enemyUnits));

            return (ctx, attackerUnit);
        }

        // ──────────────────────────────────────────────────────────────────────
        // Test doubles
        // ──────────────────────────────────────────────────────────────────────

        // TestGameContext from TerrainTestHelpers hardcodes its PlayerRequester; this variant
        // lets each test inject its own to capture the request that ChooseRangedAttackStage emits.
        internal class TestGameContextWithRequester : IGameContext
        {
            public ITextOutput TextOutput { get; } = new EmptyTextOutput();
            public IDiceRoller DiceRoller { get; } = new FixedDiceRoller(4);
            public RuleEvaluator RuleEvaluator { get; } = new(new FixedDiceRoller(4));
            public IPlayerRequestByID PlayerRequester { get; }
            public TableState TableState { get; }
            public IReadWriteableGameDataStore GameDataStore { get; }
            public ITempVisualDrawer TempVisualDrawer { get; } = new NullTempVisualDrawer();
            public IPresenter Presenter { get; } = new LocalPresenter(null, new InstantPresentationClock());
            public GameSettings Settings { get; } = GameSettings.GetDefault();
            public List<ITeam>? FirstDeploymentRollOrder => null;
            IGameContext IGameContextAccessor.GameContext => this;

            public TestGameContextWithRequester(GameDataStore store, IPlayerRequestByID requester)
            {
                GameDataStore = store;
                TableState = new TableState(store);
                PlayerRequester = requester;
            }

            public void SetFirstDeploymentRollOrder(List<ITeam> order) { }
            public void NotifyGameEnded(string result) { }
        }

        internal class CapturingRangedRequester : IPlayerRequestByID
        {
            public ChooseRangedAttackRequest? Captured { get; private set; }
            public Func<ChooseRangedAttackRequest, CancellableResult<RangedAttackChoice>> Reply { get; set; }
                = _ => new Cancelled<RangedAttackChoice>();

            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply>
            {
                if (request is ChooseRangedAttackRequest cr)
                {
                    Captured = cr;
                    object reply = Reply(cr)!;
                    return Task.FromResult((TReply)reply);
                }
                throw new InvalidOperationException("Unexpected request type: " + request.GetType());
            }
        }
    }
}
