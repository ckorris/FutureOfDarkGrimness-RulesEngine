using FDG.Data;
using FDG.Players;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Presentation;
using Newtonsoft.Json;
using NUnit.Framework;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
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

        // ── #158: dead models must not contaminate targeting ──────────────────

        // Pins TWO dead-model rules at once: (1) a wiped-out unit is not offered as a target (enemy[0]
        // dies and vanishes from the option list), and (2) a corpse does not block line of sight — the
        // dead unit at (2,0,0) lies exactly between the attacker (0,0,0) and the living target (4,0,0),
        // so this test only passes because BuildModelBlockers skips dead models.
        [Test]
        public async Task Enter_FullyDeadEnemyUnit_IsNotOffered()
        {
            var requester = new CapturingRangedRequester { Reply = _ => new Cancelled<RangedAttackChoice>() };
            var (ctx, attackerBinding) = BuildTwoTeamWorld(
                attackerPos: new Position(0, 0, 0),
                enemyPositions: new[] { new Position(2, 0, 0), new Position(4, 0, 0) },
                rifleRange: 24f,
                playerRequester: requester);

            var enemies = ctx.GameDataStore.GetAllDataBindings<ArmyData>()
                .First(a => a.GetValue().PlayerID != attackerBinding.GetValue().PlayerID)
                .GetValue().UnitBindings;
            foreach (var mb in enemies[0].GetValue().ModelBindings)
            {
                var m = mb.GetValue();
                m.DealWounds(m.TotalWounds - m.WoundsDealt);
            }

            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            BindAllStageEvents(stage);
            await stage.Enter(new CombatActionContext(ctx, attackerBinding, isMelee: false));

            Assert.That(requester.Captured, Is.Not.Null, "Resolver should have been called.");
            var targets = requester.Captured!.WeaponOptions.Single().WeaponTargetStats;
            Assert.That(targets.Select(t => t.TargetUnit.Reference), Does.Not.Contain(enemies[0].Reference),
                "a wiped-out unit must not be offered as a target at all");
            Assert.That(targets.Single().TargetUnit.Reference, Is.EqualTo(enemies[1].Reference));
        }

        [Test]
        public void ComputeHasCover_CountsOnlyLivingModels()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var player = new PlayerID(Guid.NewGuid());
            var enemy  = new PlayerID(Guid.NewGuid());

            // Attacker at (0,5). Defender: TWO dead models behind the cover wall (their sight lines cross
            // it) and ONE living model on a clear line. Counting corpses would call the unit "in cover"
            // (2 of 3); only the living model matters, and its line is clear.
            var attacker = MakeUnit(store, player, "Attacker",
                new[] { MakeModel(store, new Position(0, 5), Rifle()) });
            var deadA = MakeModel(store, new Position(20, 5));
            var deadB = MakeModel(store, new Position(20, 5));
            var alive = MakeModel(store, new Position(20, 30));
            var defender = MakeUnit(store, enemy, "Defender", new[] { deadA, deadB, alive });
            foreach (var mb in new[] { deadA, deadB })
            {
                var m = mb.GetValue();
                m.DealWounds(m.TotalWounds - m.WoundsDealt);
            }

            List<ITerrain> terrain = new()
            {
                new TerrainData(ETerrainType.Cover, new RectangularZone(8, 12, 3, 7))
            };

            Assert.That(ChooseRangedAttackStage.ComputeHasCover(attacker, defender, terrain), Is.False,
                "corpses behind the wall must not grant the lone survivor in the open a cover bonus");

            // Converse: when the LIVING model is the one behind the wall, cover applies (1 of 1).
            var aliveCovered = MakeUnit(store, enemy, "Defender2",
                new[] { MakeModel(store, new Position(20, 5)) });
            Assert.That(ChooseRangedAttackStage.ComputeHasCover(attacker, aliveCovered, terrain), Is.True);
        }

        // #028: while the unit holds an un-fired Deadly (wound-multiplier) weapon that can reach a target,
        // every non-Deadly weapon's targets are gated so the player must resolve Deadly first.
        [Test]
        public async Task Enter_DeadlyWeaponFireable_MarksNonDeadlyWeaponsUnselectable()
        {
            var requester = new CapturingRangedRequester(); // default reply: Cancelled, so Enter returns after capture
            var heavy = DeadlyWeapon("Heavy Rifle", range: 24f, x: 3);
            var rifle = Rifle();
            var (ctx, attackerBinding) = BuildTwoTeamWorld(
                attackerPos: new Position(0, 0, 0),
                enemyPositions: new[] { new Position(5, 0, 0) }, // both weapons (24") can reach
                rifleRange: 24f,
                attackerWeapons: new[] { heavy, rifle },
                playerRequester: requester);

            var combatCtx = new CombatActionContext(ctx, attackerBinding, isMelee: false);
            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            BindAllStageEvents(stage);
            await stage.Enter(combatCtx);

            Assert.That(requester.Captured, Is.Not.Null);
            var heavyOption = requester.Captured!.WeaponOptions.Single(o => o.Weapon.Name == "Heavy Rifle");
            var rifleOption = requester.Captured!.WeaponOptions.Single(o => o.Weapon.Name == "Rifle");

            Assert.That(heavyOption.WeaponTargetStats.All(t => t.UnselectableReason == null), Is.True,
                "The Deadly weapon itself must stay selectable.");
            Assert.That(rifleOption.WeaponTargetStats.All(t => t.UnselectableReason != null), Is.True,
                "The non-Deadly weapon's targets must be gated while a Deadly weapon is fireable.");
            Assert.That(rifleOption.WeaponTargetStats.First().UnselectableReason, Does.Contain("Deadly"));
        }

        // #028 edge: a Deadly weapon that can't reach anyone must NOT lock out the unit's other weapons.
        [Test]
        public async Task Enter_DeadlyWeaponOutOfRange_DoesNotGateOtherWeapons()
        {
            var requester = new CapturingRangedRequester();
            var heavy = DeadlyWeapon("Heavy Rifle", range: 6f, x: 3); // short range
            var rifle = Rifle(24f);
            var (ctx, attackerBinding) = BuildTwoTeamWorld(
                attackerPos: new Position(0, 0, 0),
                enemyPositions: new[] { new Position(10, 0, 0) }, // 9" base-to-base: rifle reaches, heavy (6") doesn't
                rifleRange: 24f,
                attackerWeapons: new[] { heavy, rifle },
                playerRequester: requester);

            var combatCtx = new CombatActionContext(ctx, attackerBinding, isMelee: false);
            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            BindAllStageEvents(stage);
            await stage.Enter(combatCtx);

            Assert.That(requester.Captured, Is.Not.Null);
            var rifleOption = requester.Captured!.WeaponOptions.Single(o => o.Weapon.Name == "Rifle");
            Assert.That(rifleOption.WeaponTargetStats.All(t => t.UnselectableReason == null), Is.True,
                "An unreachable Deadly weapon must not gate the unit's other weapons.");
        }

        // #032 Limited: a weapon already fired this game (per-model spent token) is no longer offered — its
        // targets are gated, while the unit's non-Limited weapons stay selectable.
        [Test]
        public async Task Enter_SpentLimitedWeapon_MarksItsTargetsUnselectable()
        {
            var requester = new CapturingRangedRequester();
            var rocket = LimitedWeapon("Rocket");
            var rifle = Rifle();
            var (ctx, attackerBinding) = BuildTwoTeamWorld(
                attackerPos: new Position(0, 0, 0),
                enemyPositions: new[] { new Position(5, 0, 0) },
                rifleRange: 24f,
                attackerWeapons: new[] { rocket, rifle },
                playerRequester: requester);

            LimitedRules.MarkFired(attackerBinding.GetValue(), rocket); // simulate a prior firing this game

            var combatCtx = new CombatActionContext(ctx, attackerBinding, isMelee: false);
            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            BindAllStageEvents(stage);
            await stage.Enter(combatCtx);

            Assert.That(requester.Captured, Is.Not.Null);
            var rocketOption = requester.Captured!.WeaponOptions.Single(o => o.Weapon.Name == "Rocket");
            var rifleOption = requester.Captured!.WeaponOptions.Single(o => o.Weapon.Name == "Rifle");
            Assert.That(rocketOption.WeaponTargetStats.All(t => t.UnselectableReason != null), Is.True,
                "a spent Limited weapon must be unselectable.");
            Assert.That(rocketOption.WeaponTargetStats.First().UnselectableReason, Does.Contain("Limited"));
            Assert.That(rifleOption.WeaponTargetStats.All(t => t.UnselectableReason == null), Is.True,
                "the non-Limited weapon stays selectable.");
        }

        // #032 Limited: choosing a Limited weapon commits it to fire, so it's marked spent for the game.
        [Test]
        public async Task Enter_ChoosingLimitedWeapon_MarksItSpentForTheGame()
        {
            var rocket = LimitedWeapon("Rocket");
            var requester = new CapturingRangedRequester
            {
                Reply = req =>
                {
                    var opt = req.WeaponOptions.Single(o => o.Weapon.Name == "Rocket");
                    var target = opt.WeaponTargetStats.First(t => t.UnselectableReason == null);
                    return new Selected<RangedAttackChoice>(new RangedAttackChoice(opt.Weapon, target.TargetUnit));
                }
            };
            var (ctx, attackerBinding) = BuildTwoTeamWorld(
                attackerPos: new Position(0, 0, 0),
                enemyPositions: new[] { new Position(5, 0, 0) },
                rifleRange: 24f,
                attackerWeapons: new[] { rocket },
                playerRequester: requester);

            Assert.That(LimitedRules.IsSpent(attackerBinding.GetValue(), rocket), Is.False, "available beforehand.");

            var combatCtx = new CombatActionContext(ctx, attackerBinding, isMelee: false);
            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            BindAllStageEvents(stage);
            await stage.Enter(combatCtx);

            Assert.That(LimitedRules.IsSpent(attackerBinding.GetValue(), rocket), Is.True,
                "firing the Limited weapon marks it spent for the rest of the game.");
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
            var pistol = new Weapon("Pistol", 12f, 1, 0);
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
        // ── #200: a spent Limited+Deadly weapon must not lock out the unit's other weapons ─────────
        // The Orks-pool livelock: Deadly-first gating ran BEFORE Limited-spent gating, so an empty
        // rocket still demanded to be "fired first" and every option went unselectable - while the
        // Shoot-action gate (which skipped the spent rocket but never applied Deadly gating) kept
        // saying "fireable". Deterministic AI: Choose Action <-> Shoot forever. Both now share one
        // gating pipeline; these tests pin the two halves.

        [Test]
        public async Task Enter_SpentLimitedDeadlyWeapon_OthersStillFireable_AndGateAgrees()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var requester = new CapturingRangedRequester { Reply = _ => new Cancelled<RangedAttackChoice>() };
            var ctx = new TestGameContextWithRequester(store, requester);
            var attackerPlayer = new PlayerID(Guid.NewGuid());
            var enemyPlayer = new PlayerID(Guid.NewGuid());
            store.Create(new TeamData(0, new List<PlayerID> { attackerPlayer }));
            store.Create(new TeamData(1, new List<PlayerID> { enemyPlayer }));

            Weapon rocket = new Weapon("Rocket", rangeInches: 18f, attacks: 1, armorPenetration: 1);
            rocket.AttachRuleDefinition(new ResolvedRule("Deadly", CoreRuleCatalog.Deadly,
                new RuleArgument[] { new RuleArgument.Int(3) }));
            rocket.AttachRuleDefinition(new ResolvedRule("Limited", CoreRuleCatalog.Limited));

            var attackerUnit = MakeUnit(store, attackerPlayer, "Bikers",
                new[] { MakeModel(store, new Position(0, 0, 0), rocket, Rifle()) });
            store.Create(new ArmyData(attackerPlayer, new List<DataBinding<UnitData>> { attackerUnit }));
            var enemyUnit = MakeUnit(store, enemyPlayer, "Enemy",
                new[] { MakeModel(store, new Position(10, 0, 0)) });
            store.Create(new ArmyData(enemyPlayer, new List<DataBinding<UnitData>> { enemyUnit }));

            Rules.Dispatch.LimitedRules.MarkFired(attackerUnit.GetValue(), rocket);

            // The gate half: the rifle is still fireable, so Shoot must stay offered...
            Assert.That(ChooseRangedAttackStage.HasAnyFireableTarget(attackerUnit, ctx), Is.True,
                "the rifle can fire - the spent rocket must not gray out Shoot");

            // ...and the stage half must AGREE: it requests a choice (no bounce), the rifle's target
            // selectable, the spent rocket's gated by Limited - never by "fire Deadly first".
            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            BindAllStageEvents(stage);
            await stage.Enter(new CombatActionContext(ctx, attackerUnit, isMelee: false));

            Assert.That(requester.Captured, Is.Not.Null,
                "the stage must offer a weapon choice, not bounce back to Choose Action (#200)");
            var rifleStats = requester.Captured!.WeaponOptions.Single(o => o.Weapon.Name == "Rifle")
                .WeaponTargetStats.Single();
            var rocketStats = requester.Captured!.WeaponOptions.Single(o => o.Weapon.Name == "Rocket")
                .WeaponTargetStats.Single();
            Assert.That(rifleStats.UnselectableReason, Is.Null, "the rifle's target is selectable");
            Assert.That(rocketStats.UnselectableReason, Does.Contain("Limited"),
                "the spent rocket is gated by Limited, not by Deadly-first");
        }

        [Test]
        public async Task GateAndStage_AgreeWhenOnlyWeaponIsSpent()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var requester = new CapturingRangedRequester { Reply = _ => new Cancelled<RangedAttackChoice>() };
            var ctx = new TestGameContextWithRequester(store, requester);
            var attackerPlayer = new PlayerID(Guid.NewGuid());
            var enemyPlayer = new PlayerID(Guid.NewGuid());
            store.Create(new TeamData(0, new List<PlayerID> { attackerPlayer }));
            store.Create(new TeamData(1, new List<PlayerID> { enemyPlayer }));

            Weapon rocket = new Weapon("Rocket", rangeInches: 18f, attacks: 1, armorPenetration: 1);
            rocket.AttachRuleDefinition(new ResolvedRule("Limited", CoreRuleCatalog.Limited));
            var attackerUnit = MakeUnit(store, attackerPlayer, "Bikers",
                new[] { MakeModel(store, new Position(0, 0, 0), rocket) });
            store.Create(new ArmyData(attackerPlayer, new List<DataBinding<UnitData>> { attackerUnit }));
            var enemyUnit = MakeUnit(store, enemyPlayer, "Enemy",
                new[] { MakeModel(store, new Position(10, 0, 0)) });
            store.Create(new ArmyData(enemyPlayer, new List<DataBinding<UnitData>> { enemyUnit }));

            Rules.Dispatch.LimitedRules.MarkFired(attackerUnit.GetValue(), rocket);

            Assert.That(ChooseRangedAttackStage.HasAnyFireableTarget(attackerUnit, ctx), Is.False,
                "nothing can fire - Shoot must be grayed out");

            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            BindAllStageEvents(stage);
            await stage.Enter(new CombatActionContext(ctx, attackerUnit, isMelee: false));
            Assert.That(requester.Captured, Is.Null, "the stage agrees: nothing to offer");
        }

        private static void BindAllStageEvents(ChooseRangedAttackStage stage)
        {
            stage.OnChoseWeapon.Bind("test-on-chose-weapon");
            stage.BackToChooseAction.Bind("test-back-to-choose-action");
            stage.OnNoValidShots.Bind("test-on-no-valid-shots");
        }

        private static Weapon Rifle(float range = 24f) =>
            new Weapon("Rifle", range, 1, 0);

        private static Weapon DeadlyWeapon(string name, float range, int x)
        {
            var weapon = new Weapon(name, range, 1, 0);
            weapon.AttachRuleDefinition(new ResolvedRule("Deadly", CoreRuleCatalog.Deadly,
                new RuleArgument[] { new RuleArgument.Int(x) }));
            return weapon;
        }

        private static Weapon LimitedWeapon(string name, float range = 24f)
        {
            var weapon = new Weapon(name, range, 1, 0);
            weapon.AttachRuleDefinition(new ResolvedRule("Limited", CoreRuleCatalog.Limited));
            return weapon;
        }

        private static DataBinding<ModelData> MakeModel(GameDataStore store, Position position, params Weapon[] weapons)
        {
            var model = new ModelData(
                baseRadiusInches: 0.5f,
                weapons: weapons.ToList(),
                initialPosition: position,
                gameDataStore: store);
            var modelRef = store.Create(model);
            return store.GetDataBinding<ModelData>(modelRef);
        }

        private static DataBinding<UnitData> MakeUnit(GameDataStore store, PlayerID playerID, string name,
            IEnumerable<DataBinding<ModelData>> models)
        {
            var unit = new UnitData(playerID, name, quality: 4, defense: 4,
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
            // #193: tests get a fixed-seed stream so any Rng-driven stage behaves reproducibly.
            public Random Rng { get; } = new Random(20260709);
            public RuleEvaluator RuleEvaluator { get; } = new(new FixedDiceRoller(4));
            public IPlayerRequestByID PlayerRequester { get; }
            public TableState TableState { get; }
            public IReadWriteableGameDataStore GameDataStore { get; }
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
            public void NotifyGameCompleted(GameResult result) { }
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
