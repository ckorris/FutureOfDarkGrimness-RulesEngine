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

            Assert.That(ChooseRangedAttackStage.ComputeHasCover(attacker, defender, terrain,
                    applyProximityExceptions: true), Is.False,
                "corpses behind the wall must not grant the lone survivor in the open a cover bonus");

            // Converse: when the LIVING model is the one behind the wall, cover applies (1 of 1).
            var aliveCovered = MakeUnit(store, enemy, "Defender2",
                new[] { MakeModel(store, new Position(20, 5)) });
            Assert.That(ChooseRangedAttackStage.ComputeHasCover(attacker, aliveCovered, terrain,
                applyProximityExceptions: true), Is.True);
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

        // #314: "Takedown attacks must be resolved before other weapons" — the same resolve-first gate
        // Deadly uses, so a sniper's ordinary weapons are unselectable while its Takedown weapon can fire.
        [Test]
        public async Task Enter_TakedownWeaponFireable_MarksOtherWeaponsUnselectable()
        {
            var requester = new CapturingRangedRequester();
            var sniperRifle = TakedownWeapon("Sniper Rifle", range: 24f);
            var rifle = Rifle();
            var (ctx, attackerBinding) = BuildTwoTeamWorld(
                attackerPos: new Position(0, 0, 0),
                enemyPositions: new[] { new Position(5, 0, 0) }, // both weapons (24") can reach
                rifleRange: 24f,
                attackerWeapons: new[] { sniperRifle, rifle },
                playerRequester: requester);

            var combatCtx = new CombatActionContext(ctx, attackerBinding, isMelee: false);
            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            BindAllStageEvents(stage);
            await stage.Enter(combatCtx);

            Assert.That(requester.Captured, Is.Not.Null);
            var sniperOption = requester.Captured!.WeaponOptions.Single(o => o.Weapon.Name == "Sniper Rifle");
            var rifleOption = requester.Captured!.WeaponOptions.Single(o => o.Weapon.Name == "Rifle");

            Assert.That(sniperOption.WeaponTargetStats.All(t => t.UnselectableReason == null), Is.True,
                "The Takedown weapon itself must stay selectable.");
            Assert.That(rifleOption.WeaponTargetStats.All(t => t.UnselectableReason != null), Is.True,
                "The ordinary weapon's targets must be gated while a Takedown weapon is fireable.");
            Assert.That(rifleOption.WeaponTargetStats.First().UnselectableReason, Does.Contain("Takedown"),
                "the reason must name the rule doing the gating, not Deadly.");
        }

        // #314 edge, mirroring #028's: a Takedown weapon that can't reach anyone must NOT lock out the
        // unit's other weapons — the same anyPriorityFireable guard, now exercised on the new source.
        [Test]
        public async Task Enter_TakedownWeaponOutOfRange_DoesNotGateOtherWeapons()
        {
            var requester = new CapturingRangedRequester();
            var sniperRifle = TakedownWeapon("Sniper Rifle", range: 6f); // short range
            var rifle = Rifle(24f);
            var (ctx, attackerBinding) = BuildTwoTeamWorld(
                attackerPos: new Position(0, 0, 0),
                enemyPositions: new[] { new Position(10, 0, 0) }, // 9" base-to-base: rifle reaches, sniper (6") doesn't
                rifleRange: 24f,
                attackerWeapons: new[] { sniperRifle, rifle },
                playerRequester: requester);

            var combatCtx = new CombatActionContext(ctx, attackerBinding, isMelee: false);
            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            BindAllStageEvents(stage);
            await stage.Enter(combatCtx);

            Assert.That(requester.Captured, Is.Not.Null);
            var rifleOption = requester.Captured!.WeaponOptions.Single(o => o.Weapon.Name == "Rifle");
            Assert.That(rifleOption.WeaponTargetStats.All(t => t.UnselectableReason == null), Is.True,
                "An unreachable Takedown weapon must not gate the unit's other weapons.");
        }

        // #314: Deadly and Takedown share ONE priority class — a unit carrying both must fire both before
        // its ordinary weapons, and neither gates the other (no precedence between them in the rules).
        [Test]
        public async Task Enter_DeadlyAndTakedown_GateOrdinaryWeaponsButNotEachOther()
        {
            var requester = new CapturingRangedRequester();
            var heavy = DeadlyWeapon("Heavy Rifle", range: 24f, x: 3);
            var sniperRifle = TakedownWeapon("Sniper Rifle", range: 24f);
            var rifle = Rifle();
            var (ctx, attackerBinding) = BuildTwoTeamWorld(
                attackerPos: new Position(0, 0, 0),
                enemyPositions: new[] { new Position(5, 0, 0) },
                rifleRange: 24f,
                attackerWeapons: new[] { heavy, sniperRifle, rifle },
                playerRequester: requester);

            var combatCtx = new CombatActionContext(ctx, attackerBinding, isMelee: false);
            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            BindAllStageEvents(stage);
            await stage.Enter(combatCtx);

            Assert.That(requester.Captured, Is.Not.Null);
            var heavyOption = requester.Captured!.WeaponOptions.Single(o => o.Weapon.Name == "Heavy Rifle");
            var sniperOption = requester.Captured!.WeaponOptions.Single(o => o.Weapon.Name == "Sniper Rifle");
            var rifleOption = requester.Captured!.WeaponOptions.Single(o => o.Weapon.Name == "Rifle");

            Assert.That(heavyOption.WeaponTargetStats.All(t => t.UnselectableReason == null), Is.True,
                "Deadly must not be gated by Takedown.");
            Assert.That(sniperOption.WeaponTargetStats.All(t => t.UnselectableReason == null), Is.True,
                "Takedown must not be gated by Deadly.");
            Assert.That(rifleOption.WeaponTargetStats.All(t => t.UnselectableReason != null), Is.True,
                "the ordinary weapon waits for both.");
            string reason = rifleOption.WeaponTargetStats.First().UnselectableReason!;
            Assert.That(reason, Does.Contain("Deadly").And.Contain("Takedown"),
                "the reason names both rules holding the ordinary weapon back.");
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
            // #315: and it says so in a form the UI can render without parsing the reason string.
            Assert.That(rocketOption.LimitedAlreadyFired, Is.True);
            Assert.That(rifleOption.LimitedAlreadyFired, Is.False);
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
        public async Task DeadDeadlyCarrier_DoesNotGateTheSurvivorsWeapons()
        {
            // Chris's follow-up to #200: if the ONLY model carrying a Deadly weapon dies, the unit's
            // other weapons must not be locked behind "fire Deadly first". Safe by construction today
            // (weapon pools and shooters both enumerate LIVING models only) - pinned here so it stays so.
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

            var rocketeer = MakeModel(store, new Position(0, 0, 0), rocket);
            var rifleman = MakeModel(store, new Position(1, 0, 0), Rifle());
            var attackerUnit = MakeUnit(store, attackerPlayer, "Squad", new[] { rocketeer, rifleman });
            store.Create(new ArmyData(attackerPlayer, new List<DataBinding<UnitData>> { attackerUnit }));
            var enemyUnit = MakeUnit(store, enemyPlayer, "Enemy",
                new[] { MakeModel(store, new Position(10, 0, 0)) });
            store.Create(new ArmyData(enemyPlayer, new List<DataBinding<UnitData>> { enemyUnit }));

            rocketeer.GetValue().DealWounds(rocketeer.GetValue().TotalWounds); // the rocketeer dies

            Assert.That(ChooseRangedAttackStage.HasAnyFireableTarget(attackerUnit, ctx), Is.True,
                "the surviving rifleman can fire");

            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            BindAllStageEvents(stage);
            await stage.Enter(new CombatActionContext(ctx, attackerUnit, isMelee: false));

            Assert.That(requester.Captured, Is.Not.Null, "the stage must offer the rifle");
            Assert.That(requester.Captured!.WeaponOptions.Select(o => o.Weapon.Name),
                Does.Not.Contain("Rocket"), "a dead model's weapon is not offered at all");
            var rifleStats = requester.Captured!.WeaponOptions.Single(o => o.Weapon.Name == "Rifle")
                .WeaponTargetStats.Single();
            Assert.That(rifleStats.UnselectableReason, Is.Null,
                "the rifle must not be gated behind a dead model's Deadly weapon");
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

        // ── #276: only models that can shoot the chosen target contribute attack dice ─────────────
        // GDF checks range and line of sight per model; the pooled weapon count used to fire every
        // living copy as long as ANY model could see the target.

        [Test]
        public async Task Enter_OccludedCarrier_TrimsWeaponCountToEligibleShooters()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var requester = new CapturingRangedRequester
            {
                Reply = req =>
                {
                    var opt = req.WeaponOptions.Single();
                    var target = opt.WeaponTargetStats.First(t => t.UnselectableReason == null);
                    return new Selected<RangedAttackChoice>(new RangedAttackChoice(opt.Weapon, target.TargetUnit));
                }
            };
            var ctx = new TestGameContextWithRequester(store, requester);
            var attackerPlayer = new PlayerID(Guid.NewGuid());
            var enemyPlayer = new PlayerID(Guid.NewGuid());
            store.Create(new TeamData(0, new List<PlayerID> { attackerPlayer }));
            store.Create(new TeamData(1, new List<PlayerID> { enemyPlayer }));

            // Wall x 8..12, z 3..7 blocks the z=5 fire lane; the z=12 carrier sees past it.
            store.Create(new TerrainData(ETerrainType.Blocking, new RectangularZone(8, 12, 3, 7)));
            var blocked = MakeModel(store, new Position(1, 5), Rifle());
            var clear = MakeModel(store, new Position(1, 12), Rifle());
            var attackerUnit = MakeUnit(store, attackerPlayer, "Squad", new[] { blocked, clear });
            store.Create(new ArmyData(attackerPlayer, new List<DataBinding<UnitData>> { attackerUnit }));
            var enemyUnit = MakeUnit(store, enemyPlayer, "Enemy",
                new[] { MakeModel(store, new Position(21, 5)) });
            store.Create(new ArmyData(enemyPlayer, new List<DataBinding<UnitData>> { enemyUnit }));

            var combatCtx = new CombatActionContext(ctx, attackerUnit, isMelee: false);
            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            BindAllStageEvents(stage);
            await stage.Enter(combatCtx);

            Assert.That(combatCtx.HasPendingAttack, Is.True, "the shot was committed");
            ICombatMetadata metadata = combatCtx.ConsumeAttackIntoContext(ctx);
            Assert.That(metadata.WeaponCount, Is.EqualTo(1),
                "the occluded carrier's rifle must not add attack dice - only the model with line of sight fires");
        }

        [Test]
        public async Task Enter_AllCarriersClear_KeepsFullWeaponCount()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var requester = new CapturingRangedRequester
            {
                Reply = req =>
                {
                    var opt = req.WeaponOptions.Single();
                    var target = opt.WeaponTargetStats.First(t => t.UnselectableReason == null);
                    return new Selected<RangedAttackChoice>(new RangedAttackChoice(opt.Weapon, target.TargetUnit));
                }
            };
            var ctx = new TestGameContextWithRequester(store, requester);
            var attackerPlayer = new PlayerID(Guid.NewGuid());
            var enemyPlayer = new PlayerID(Guid.NewGuid());
            store.Create(new TeamData(0, new List<PlayerID> { attackerPlayer }));
            store.Create(new TeamData(1, new List<PlayerID> { enemyPlayer }));

            var attackerUnit = MakeUnit(store, attackerPlayer, "Squad", new[]
            {
                MakeModel(store, new Position(1, 5), Rifle()),
                MakeModel(store, new Position(1, 12), Rifle()),
            });
            store.Create(new ArmyData(attackerPlayer, new List<DataBinding<UnitData>> { attackerUnit }));
            var enemyUnit = MakeUnit(store, enemyPlayer, "Enemy",
                new[] { MakeModel(store, new Position(21, 5)) });
            store.Create(new ArmyData(enemyPlayer, new List<DataBinding<UnitData>> { enemyUnit }));

            var combatCtx = new CombatActionContext(ctx, attackerUnit, isMelee: false);
            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            BindAllStageEvents(stage);
            await stage.Enter(combatCtx);

            ICombatMetadata metadata = combatCtx.ConsumeAttackIntoContext(ctx);
            Assert.That(metadata.WeaponCount, Is.EqualTo(2), "both carriers can shoot - nothing is trimmed");
        }

        // ──────────────────────────────────────────────────────────────────────
        // #308: the STAGE owns "may the player back out?" and "what did the last weapon shoot?".
        // Both were previously guessed by the GUI resolver, which kept a per-attacker fire counter it
        // only reset when the ATTACKING UNIT changed - so a unit that shot once never saw Back again,
        // on that activation or any later one.
        // ──────────────────────────────────────────────────────────────────────

        [Test]
        public async Task Enter_BeforeAnythingHasFired_AllowsCancel_AndHasNoPreviousTarget()
        {
            var requester = new CapturingRangedRequester(); // default reply: Cancelled
            var (ctx, attackerBinding) = BuildTwoTeamWorld(
                attackerPos: new Position(0, 0, 0),
                enemyPositions: new[] { new Position(5, 0, 0) },
                rifleRange: 24f,
                playerRequester: requester);

            var combatCtx = new CombatActionContext(ctx, attackerBinding, isMelee: false);
            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            BindAllStageEvents(stage);
            await stage.Enter(combatCtx);

            Assert.That(requester.Captured!.AllowCancel, Is.True,
                "nothing has fired, so backing out to Choose Action costs the player nothing.");
            Assert.That(requester.Captured!.PreviousTarget, Is.Null,
                "the first weapon of a shoot action has no previous target to inherit.");
        }

        [Test]
        public async Task Enter_AfterAWeaponHasFired_ForbidsCancel_AndCarriesTheLastTarget()
        {
            // Reply: always take the first fireable option. The first Enter commits the rifle; the second
            // (a fresh Enter, as the weapon loop does) is the one whose request we inspect.
            var requester = new CapturingRangedRequester
            {
                Reply = req =>
                {
                    var opt = req.WeaponOptions.First(o =>
                        o.WeaponTargetStats.Any(t => t.UnselectableReason == null && t.modelsThatCanShoot.Count > 0));
                    var target = opt.WeaponTargetStats.First(t =>
                        t.UnselectableReason == null && t.modelsThatCanShoot.Count > 0);
                    return new Selected<RangedAttackChoice>(new RangedAttackChoice(opt.Weapon, target.TargetUnit));
                },
            };
            var (ctx, attackerBinding) = BuildTwoTeamWorld(
                attackerPos: new Position(0, 0, 0),
                enemyPositions: new[] { new Position(5, 0, 0) },
                rifleRange: 24f,
                attackerWeapons: new[] { Rifle(), new Weapon("Pistol", 12f, 1, 0) },
                playerRequester: requester);

            var combatCtx = new CombatActionContext(ctx, attackerBinding, isMelee: false);
            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            BindAllStageEvents(stage);

            await stage.Enter(combatCtx);                       // first weapon committed
            DataBinding<UnitData> firstTarget = combatCtx.DefendingUnit;
            Assert.That(combatCtx.AlreadyUsedWeapons.Count, Is.EqualTo(1), "test setup: one weapon fired");

            await stage.Enter(combatCtx);                       // the second weapon's choice

            Assert.That(requester.Captured!.AllowCancel, Is.False,
                "a weapon has fired - there is no un-firing it, so Back has nowhere to return to.");
            Assert.That(requester.Captured!.PreviousTarget, Is.Not.Null);
            Assert.That(requester.Captured!.PreviousTarget!.Reference, Is.EqualTo(firstTarget.Reference),
                "the next weapon starts aimed where the last one fired.");
        }

        // A Cancelled reply that arrives anyway (out-of-date or ill-behaved resolver) must not rewind an
        // activation that has already shot - that would hand the unit a second action from the menu.
        [Test]
        public async Task Enter_CancelledAfterFiring_EndsTheShoot_RatherThanReturningToChooseAction()
        {
            bool firstCall = true;
            var requester = new CapturingRangedRequester
            {
                Reply = req =>
                {
                    if (!firstCall) return new Cancelled<RangedAttackChoice>();
                    firstCall = false;
                    var opt = req.WeaponOptions.First(o =>
                        o.WeaponTargetStats.Any(t => t.UnselectableReason == null && t.modelsThatCanShoot.Count > 0));
                    var target = opt.WeaponTargetStats.First(t =>
                        t.UnselectableReason == null && t.modelsThatCanShoot.Count > 0);
                    return new Selected<RangedAttackChoice>(new RangedAttackChoice(opt.Weapon, target.TargetUnit));
                },
            };
            var (ctx, attackerBinding) = BuildTwoTeamWorld(
                attackerPos: new Position(0, 0, 0),
                enemyPositions: new[] { new Position(5, 0, 0) },
                rifleRange: 24f,
                attackerWeapons: new[] { Rifle(), new Weapon("Pistol", 12f, 1, 0) },
                playerRequester: requester);

            var combatCtx = new CombatActionContext(ctx, attackerBinding, isMelee: false);
            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            var transitions = new List<string>();
            stage.OnChoseWeapon.Bind("test-on-chose-weapon");
            stage.BackToChooseAction.Bind("test-back-to-choose-action");
            stage.OnNoValidShots.Bind("test-on-no-valid-shots");
            stage.BackToChooseAction.OnWillActivate += _ => transitions.Add("back");
            stage.OnNoValidShots.OnWillActivate += _ => transitions.Add("no-valid-shots");

            await stage.Enter(combatCtx);   // fires
            await stage.Enter(combatCtx);   // cancels

            Assert.That(transitions, Does.Not.Contain("back"),
                "backing out after firing would re-offer the action menu to a unit that already shot.");
            Assert.That(transitions, Does.Contain("no-valid-shots"), "the shoot action ends instead.");
        }

        // ──────────────────────────────────────────────────────────────────────
        // #315: Hold fire / Done shooting. Before this, a unit was FORCED to fire every weapon that had a
        // legal target - Back vanished the moment the first weapon fired - so a once-per-game Limited
        // weapon got burned by the shoot loop whether the player wanted to spend it or not.
        // ──────────────────────────────────────────────────────────────────────

        // Holding fire with a Limited weapon must leave it unspent and still available NEXT game turn -
        // the whole point of being able to decline it.
        [Test]
        public async Task Enter_HoldFireOnLimitedWeapon_LeavesItUnspent_AndOffersTheRemainingWeapons()
        {
            var rocket = LimitedWeapon("Rocket");
            var requests = new List<ChooseRangedAttackRequest>();
            var requester = new CapturingRangedRequester
            {
                Reply = req =>
                {
                    requests.Add(req);
                    var rocketOption = req.WeaponOptions.FirstOrDefault(o => o.Weapon.Name == "Rocket");
                    if (rocketOption != null) return new Selected<RangedAttackChoice>(
                        RangedAttackChoice.HoldFire(rocketOption.Weapon));
                    return FireFirstFireable(req);
                },
            };
            var (ctx, attackerBinding) = BuildTwoTeamWorld(
                attackerPos: new Position(0, 0, 0),
                enemyPositions: new[] { new Position(5, 0, 0) },
                rifleRange: 24f,
                attackerWeapons: new[] { rocket, Rifle() },
                playerRequester: requester);

            var combatCtx = new CombatActionContext(ctx, attackerBinding, isMelee: false);
            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            BindAllStageEvents(stage);
            await stage.Enter(combatCtx);

            Assert.That(LimitedRules.IsSpent(attackerBinding.GetValue(), rocket), Is.False,
                "a weapon that never fired must keep its once-per-game shot.");
            Assert.That(combatCtx.DeclinedWeapons.Keys.Any(w => w.Name == "Rocket"), Is.True,
                "the declined weapon is recorded as held-fire...");
            Assert.That(combatCtx.AlreadyUsedWeapons.Keys.Any(w => w.Name == "Rocket"), Is.False,
                "...and NOT as fired - that flag decides whether the action can still be backed out of.");
            Assert.That(combatCtx.AlreadyUsedWeapons.Keys.Any(w => w.Name == "Rifle"), Is.True,
                "the unit's other weapon still fires in the same shoot action.");
            Assert.That(requests, Has.Count.EqualTo(2), "declining re-offers the remaining weapons.");
            Assert.That(requests[1].WeaponOptions.Any(o => o.Weapon.Name == "Rocket"), Is.False,
                "the held-fire weapon is not offered again this action.");
        }

        // The scenario a plain "end the shoot" exit could not serve, and the reason hold-fire is
        // per-weapon: a Deadly+Limited rocket is a RESOLVE-FIRST weapon, so while it is on offer it gates
        // the unit's ordinary weapons ("Must fire Deadly weapons first"). Declining it has to release them,
        // or declining would cost the player their whole shoot.
        [Test]
        public async Task Enter_HoldFireOnResolveFirstLimitedWeapon_UnlocksTheOrdinaryWeapons()
        {
            var rocket = new Weapon("Rocket", 24f, 1, 0);
            rocket.AttachRuleDefinition(new ResolvedRule("Deadly", CoreRuleCatalog.Deadly,
                new RuleArgument[] { new RuleArgument.Int(3) }));
            rocket.AttachRuleDefinition(new ResolvedRule("Limited", CoreRuleCatalog.Limited));

            var requests = new List<ChooseRangedAttackRequest>();
            var requester = new CapturingRangedRequester
            {
                Reply = req =>
                {
                    requests.Add(req);
                    var rocketOption = req.WeaponOptions.FirstOrDefault(o => o.Weapon.Name == "Rocket");
                    if (rocketOption != null) return new Selected<RangedAttackChoice>(
                        RangedAttackChoice.HoldFire(rocketOption.Weapon));
                    return FireFirstFireable(req);
                },
            };
            var (ctx, attackerBinding) = BuildTwoTeamWorld(
                attackerPos: new Position(0, 0, 0),
                enemyPositions: new[] { new Position(5, 0, 0) },
                rifleRange: 24f,
                attackerWeapons: new[] { rocket, Rifle() },
                playerRequester: requester);

            var combatCtx = new CombatActionContext(ctx, attackerBinding, isMelee: false);
            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            BindAllStageEvents(stage);
            await stage.Enter(combatCtx);

            var gatedRifle = requests[0].WeaponOptions.Single(o => o.Weapon.Name == "Rifle");
            Assert.That(gatedRifle.WeaponTargetStats.All(t => t.UnselectableReason != null), Is.True,
                "test setup: while the Deadly rocket is on offer it gates the rifle.");

            var freedRifle = requests[1].WeaponOptions.Single(o => o.Weapon.Name == "Rifle");
            Assert.That(freedRifle.WeaponTargetStats.Any(t => t.UnselectableReason == null), Is.True,
                "a declined resolve-first weapon must stop demanding to be resolved first.");
            Assert.That(combatCtx.AlreadyUsedWeapons.Keys.Any(w => w.Name == "Rifle"), Is.True,
                "so the rifle can actually fire.");
            Assert.That(LimitedRules.IsSpent(attackerBinding.GetValue(), rocket), Is.False,
                "and the rocket keeps its once-per-game shot.");
        }

        // Holding fire with everything, having fired nothing, spends no action: the player lands back on
        // the action menu, exactly as the no-valid-target path does.
        [Test]
        public async Task Enter_HoldFireOnEveryWeapon_NothingFired_ReturnsToChooseAction()
        {
            var requester = new CapturingRangedRequester
            {
                Reply = req => new Selected<RangedAttackChoice>(
                    RangedAttackChoice.HoldFire(req.WeaponOptions[0].Weapon)),
            };
            var (ctx, attackerBinding) = BuildTwoTeamWorld(
                attackerPos: new Position(0, 0, 0),
                enemyPositions: new[] { new Position(5, 0, 0) },
                rifleRange: 24f,
                attackerWeapons: new[] { LimitedWeapon("Rocket"), Rifle() },
                playerRequester: requester);

            var combatCtx = new CombatActionContext(ctx, attackerBinding, isMelee: false);
            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            var transitions = new List<string>();
            BindAllStageEvents(stage);
            stage.BackToChooseAction.OnWillActivate += _ => transitions.Add("back");
            stage.OnNoValidShots.OnWillActivate += _ => transitions.Add("no-valid-shots");

            await stage.Enter(combatCtx);

            Assert.That(transitions, Is.EqualTo(new[] { "back" }),
                "declining every weapon without firing costs the unit nothing.");
            Assert.That(combatCtx.AvailableWeapons, Is.Empty);
        }

        // Same, but a weapon HAS fired: the shoot action ends through the normal exit (morale, post-shoot)
        // rather than rewinding to the action menu, which would hand the unit a second action.
        [Test]
        public async Task Enter_HoldFireOnLastWeapon_AfterFiring_EndsTheShootAction()
        {
            bool rifleFired = false;
            var requester = new CapturingRangedRequester
            {
                Reply = req =>
                {
                    if (!rifleFired)
                    {
                        rifleFired = true;
                        var rifleOption = req.WeaponOptions.Single(o => o.Weapon.Name == "Rifle");
                        var target = rifleOption.WeaponTargetStats.First(t =>
                            t.UnselectableReason == null && t.modelsThatCanShoot.Count > 0);
                        return new Selected<RangedAttackChoice>(
                            new RangedAttackChoice(rifleOption.Weapon, target.TargetUnit));
                    }
                    return new Selected<RangedAttackChoice>(
                        RangedAttackChoice.HoldFire(req.WeaponOptions[0].Weapon));
                },
            };
            var rocket = LimitedWeapon("Rocket");
            var (ctx, attackerBinding) = BuildTwoTeamWorld(
                attackerPos: new Position(0, 0, 0),
                enemyPositions: new[] { new Position(5, 0, 0) },
                rifleRange: 24f,
                attackerWeapons: new[] { rocket, Rifle() },
                playerRequester: requester);

            var combatCtx = new CombatActionContext(ctx, attackerBinding, isMelee: false);
            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            var transitions = new List<string>();
            BindAllStageEvents(stage);
            stage.BackToChooseAction.OnWillActivate += _ => transitions.Add("back");
            stage.OnNoValidShots.OnWillActivate += _ => transitions.Add("no-valid-shots");

            await stage.Enter(combatCtx);   // fires the rifle
            await stage.Enter(combatCtx);   // holds fire with the rocket - nothing left

            Assert.That(transitions, Does.Not.Contain("back"));
            Assert.That(transitions, Does.Contain("no-valid-shots"),
                "the shoot ends through the same exit a shoot with no targets left takes.");
            Assert.That(LimitedRules.IsSpent(attackerBinding.GetValue(), rocket), Is.False,
                "the declined Limited weapon is still unspent.");
        }

        // The request tells the resolver WHICH exit to label, and the two are mutually exclusive: Back
        // (rewinds, nothing fired) or Done shooting (ends the action, something fired).
        [Test]
        public async Task Enter_AllowStopShooting_IsOfferedOnlyAfterAWeaponHasFired()
        {
            var requester = new CapturingRangedRequester { Reply = FireFirstFireable };
            var (ctx, attackerBinding) = BuildTwoTeamWorld(
                attackerPos: new Position(0, 0, 0),
                enemyPositions: new[] { new Position(5, 0, 0) },
                rifleRange: 24f,
                attackerWeapons: new[] { Rifle(), new Weapon("Pistol", 12f, 1, 0) },
                playerRequester: requester);

            var combatCtx = new CombatActionContext(ctx, attackerBinding, isMelee: false);
            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            BindAllStageEvents(stage);

            await stage.Enter(combatCtx);
            Assert.That(requester.Captured!.AllowStopShooting, Is.False,
                "nothing has fired yet - the exit is Back, not Done.");
            Assert.That(requester.Captured!.AllowCancel, Is.True);

            await stage.Enter(combatCtx);
            Assert.That(requester.Captured!.AllowStopShooting, Is.True,
                "a weapon has fired - the player may still decline the rest.");
            Assert.That(requester.Captured!.AllowCancel, Is.False,
                "exactly one of the two exits is offered.");
        }

        // The resolvers cannot read a weapon's rules off the wire (RuleDefinitions is [JsonIgnore]), so the
        // once-per-game rule is named on the option itself - both while it can still be fired and after.
        [Test]
        public async Task Enter_LimitedRule_IsNamedOnTheOption_ForLimitedWeaponsOnly()
        {
            var requester = new CapturingRangedRequester();
            var (ctx, attackerBinding) = BuildTwoTeamWorld(
                attackerPos: new Position(0, 0, 0),
                enemyPositions: new[] { new Position(5, 0, 0) },
                rifleRange: 24f,
                attackerWeapons: new[] { LimitedWeapon("Rocket"), Rifle() },
                playerRequester: requester);

            var combatCtx = new CombatActionContext(ctx, attackerBinding, isMelee: false);
            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            BindAllStageEvents(stage);
            await stage.Enter(combatCtx);

            var rocketOption = requester.Captured!.WeaponOptions.Single(o => o.Weapon.Name == "Rocket");
            Assert.That(rocketOption.LimitedRule, Is.EqualTo("Limited"),
                "the unspent once-per-game weapon is named, so the UI can warn.");
            Assert.That(rocketOption.LimitedAlreadyFired, Is.False,
                "it can still be fired - the warning is 'this will spend it', not 'it is spent'.");
            Assert.That(requester.Captured!.WeaponOptions.Single(o => o.Weapon.Name == "Rifle").LimitedRule,
                Is.Null, "an ordinary weapon carries no such warning.");
        }

        // The hold-fire reply and the Limited badge both cross the network on a remote player's turn.
        [Test]
        public void HoldFireChoice_AndLimitedRule_RoundTripThroughJson()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var playerID = new PlayerID(Guid.NewGuid());
            var attackerBinding = MakeUnit(store, playerID, "Attacker",
                new[] { MakeModel(store, new Position(0, 0, 0), Rifle()) });
            var rocket = LimitedWeapon("Rocket");

            var option = new WeaponOption(rocket, new List<WeaponTargetStats>(), LimitedRule: "Limited");
            var request = new ChooseRangedAttackRequest(playerID, "ChooseRanged",
                attackerBinding, new List<WeaponOption> { option }, allowCancel: false,
                allowStopShooting: true);

            string requestJson = JsonConvert.SerializeObject(request, store.GetJsonSettings());
            var roundTrippedRequest = JsonConvert.DeserializeObject<ChooseRangedAttackRequest>(
                requestJson, store.GetJsonSettings());

            Assert.That(roundTrippedRequest!.WeaponOptions[0].LimitedRule, Is.EqualTo("Limited"));
            Assert.That(roundTrippedRequest!.AllowStopShooting, Is.True);
            Assert.That(roundTrippedRequest!.AllowCancel, Is.False);

            CancellableResult<RangedAttackChoice> reply =
                new Selected<RangedAttackChoice>(RangedAttackChoice.HoldFire(rocket));
            string replyJson = JsonConvert.SerializeObject(reply,
                typeof(CancellableResult<RangedAttackChoice>), store.GetJsonSettings());
            var roundTrippedReply = JsonConvert.DeserializeObject<CancellableResult<RangedAttackChoice>>(
                replyJson, store.GetJsonSettings());

            Assert.That(roundTrippedReply, Is.InstanceOf<Selected<RangedAttackChoice>>());
            var choice = ((Selected<RangedAttackChoice>)roundTrippedReply!).Value;
            Assert.That(choice.IsHoldFire, Is.True, "a null target is what says 'do not fire this weapon'.");
            Assert.That(choice.Weapon.Name, Is.EqualTo("Rocket"));
        }

        // Reply helper: fire the first weapon that has a selectable target with shooters in range.
        private static CancellableResult<RangedAttackChoice> FireFirstFireable(ChooseRangedAttackRequest req)
        {
            var option = req.WeaponOptions.First(o =>
                o.WeaponTargetStats.Any(t => t.UnselectableReason == null && t.modelsThatCanShoot.Count > 0));
            var target = option.WeaponTargetStats.First(t =>
                t.UnselectableReason == null && t.modelsThatCanShoot.Count > 0);
            return new Selected<RangedAttackChoice>(new RangedAttackChoice(option.Weapon, target.TargetUnit));
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

        private static Weapon TakedownWeapon(string name, float range = 24f)
        {
            var weapon = new Weapon(name, range, 1, 0);
            weapon.AttachRuleDefinition(new ResolvedRule("Takedown", CoreRuleCatalog.Takedown));
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
