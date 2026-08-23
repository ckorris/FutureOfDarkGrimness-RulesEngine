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
        // #345 AttackCounts — the volley's real size vs its full size, shown to the player pre-roll.
        // Must mirror the #276 trim in OfferWeapons exactly, or the preview promises dice the roll
        // will not throw (or hides ones it will).
        // ──────────────────────────────────────────────────────────────────────

        [Test]
        public void AttackCounts_ReportTheTrimmedVolleyAgainstTheFullOne()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var playerID = new PlayerID(Guid.NewGuid());

            // Five riflemen, only three of whom can see the target: A2 rifles, so 6 of a possible 10.
            var shooters = new[]
            {
                MakeModel(store, new Position(0, 0, 0), TwoShotRifle()),
                MakeModel(store, new Position(1, 0, 0), TwoShotRifle()),
                MakeModel(store, new Position(2, 0, 0), TwoShotRifle()),
            };
            var blocked = new[]
            {
                MakeModel(store, new Position(3, 0, 0), TwoShotRifle()),
                MakeModel(store, new Position(4, 0, 0), TwoShotRifle()),
            };
            var targetBinding = MakeUnit(store, new PlayerID(Guid.NewGuid()), "Target",
                new[] { MakeModel(store, new Position(20, 0, 0)) });

            var stats = new WeaponTargetStats(targetBinding,
                new HashSet<DataBinding<ModelData>>(shooters),
                new HashSet<DataBinding<ModelData>>(blocked));
            var option = new WeaponOption(TwoShotRifle(), new List<WeaponTargetStats> { stats },
                CopiesRemaining: 5);

            Assert.That(ChooseRangedAttackStage.AttackCounts(option, stats), Is.EqualTo((6, 10)),
                "3 of 5 carriers have a lane, and each rifle is A2");
        }

        [Test]
        public void AttackCounts_AreEqualWhenTheWholeUnitCanFire()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var playerID = new PlayerID(Guid.NewGuid());
            var shooters = new[]
            {
                MakeModel(store, new Position(0, 0, 0), Rifle()),
                MakeModel(store, new Position(1, 0, 0), Rifle()),
            };
            var targetBinding = MakeUnit(store, new PlayerID(Guid.NewGuid()), "Target",
                new[] { MakeModel(store, new Position(20, 0, 0)) });

            var stats = new WeaponTargetStats(targetBinding,
                new HashSet<DataBinding<ModelData>>(shooters), new HashSet<DataBinding<ModelData>>());
            var option = new WeaponOption(Rifle(), new List<WeaponTargetStats> { stats },
                CopiesRemaining: 2);

            Assert.That(ChooseRangedAttackStage.AttackCounts(option, stats), Is.EqualTo((2, 2)),
                "nothing is held back, so the UI has nothing to warn about");
        }

        [Test]
        public void AttackCounts_AOneAtATimeWeaponFiresOneCopyByRule_NotByBlocking()
        {
            // #340: a Takedown volley is never trimmed - the other copies are aimed SEPARATELY, not
            // blocked, so reporting "1 of 3" would blame terrain for the rule's own behaviour.
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            Weapon sniper = TakedownWeapon("Sniper Rifle");
            var shooters = new[] { MakeModel(store, new Position(0, 0, 0), sniper) };
            var targetBinding = MakeUnit(store, new PlayerID(Guid.NewGuid()), "Target",
                new[] { MakeModel(store, new Position(20, 0, 0)) });

            var stats = new WeaponTargetStats(targetBinding,
                new HashSet<DataBinding<ModelData>>(shooters), new HashSet<DataBinding<ModelData>>());
            var option = new WeaponOption(sniper, new List<WeaponTargetStats> { stats },
                AimedIndividuallyRule: "Takedown", CopiesRemaining: 3);

            Assert.That(ChooseRangedAttackStage.AttackCounts(option, stats), Is.EqualTo((1, 1)));
        }

        [Test]
        public void AttackCounts_NoEligibleCarrier_LeavesTheCountAlone()
        {
            // Mirrors the stage's own "eligible == 0 leaves the count alone" guard: such a target is not
            // selectable in the first place, and reporting 0 would read as "this weapon does nothing".
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var targetBinding = MakeUnit(store, new PlayerID(Guid.NewGuid()), "Target",
                new[] { MakeModel(store, new Position(20, 0, 0)) });

            var stats = new WeaponTargetStats(targetBinding,
                new HashSet<DataBinding<ModelData>>(), new HashSet<DataBinding<ModelData>>());
            var option = new WeaponOption(Rifle(), new List<WeaponTargetStats> { stats },
                CopiesRemaining: 4);

            Assert.That(ChooseRangedAttackStage.AttackCounts(option, stats), Is.EqualTo((4, 4)));
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

        // ── #384: the Unlimited Split Fire house rule lifts the 2-unit cap ────

        [Test]
        public async Task Enter_TwoTargetsAlreadyAttacked_ThirdSelectable_WithUnlimitedSplitFire()
        {
            var requester = new CapturingRangedRequester { Reply = _ => new Cancelled<RangedAttackChoice>() };
            var (ctx, attackerBinding) = BuildTwoTeamWorld(
                attackerPos: new Position(0, 0, 0),
                enemyPositions: new[] { new Position(2, 0, 0), new Position(3, 0, 0), new Position(4, 0, 0) },
                rifleRange: 24f,
                playerRequester: requester);
            GameSettings settings = ctx.Settings;
            settings.UnlimitedSplitFire = true;
            ctx.Settings = settings;

            var combatCtx = new CombatActionContext(ctx, attackerBinding, isMelee: false);
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
            foreach (WeaponTargetStats target in targetsForRifle)
            {
                Assert.That(target.UnselectableReason, Is.Null,
                    "With Unlimited Split Fire on, no target is gated by the 2-unit cap.");
            }
        }

        // ── #384: the See-Through Allies house rule at the targeting stage ────

        [Test]
        public async Task Enter_AllyOnSightLine_BlocksShot_UnderOfficialRules()
        {
            CapturingRangedRequester requester = await RunAllyScreenWorld(seeThroughFriendlyUnits: false);
            // The only enemy sits behind the ally, so no weapon has a fireable target and the stage
            // never even raises the targeting request.
            Assert.That(requester.Captured, Is.Null,
                "Under the official rules (setting off) the allied unit's base cuts the only sight line.");
        }

        [Test]
        public async Task Enter_AllyOnSightLine_DoesNotBlockShot_UnderHouseRule()
        {
            CapturingRangedRequester requester = await RunAllyScreenWorld(seeThroughFriendlyUnits: true);
            Assert.That(requester.Captured, Is.Not.Null, "the shot must be offered");
            WeaponTargetStats stats = requester.Captured!.WeaponOptions.Single().WeaponTargetStats.Single();
            Assert.That(stats.modelsThatCanShoot, Is.Not.Empty,
                "With See-Through Allies on (pre-#384 behavior) the ally is transparent.");
        }

        // One shooter at (0,0), one enemy at (20,0), and a same-player screening unit at (10,0)
        // square on the sight line.
        private async Task<CapturingRangedRequester> RunAllyScreenWorld(
            bool seeThroughFriendlyUnits)
        {
            var requester = new CapturingRangedRequester { Reply = _ => new Cancelled<RangedAttackChoice>() };
            var (ctx, attackerBinding) = BuildTwoTeamWorld(
                attackerPos: new Position(0, 0, 0),
                enemyPositions: new[] { new Position(20, 0, 0) },
                rifleRange: 24f,
                playerRequester: requester);
            // A second unit of the attacker's own player, parked on the sight line. Not in any army -
            // TableState.Units tracks every unit created through the store, which is all the LoS
            // blocker builder reads.
            MakeUnit(ctx.GameDataStore as GameDataStore ?? throw new InvalidOperationException(),
                attackerBinding.GetValue().PlayerID, "AllyScreen",
                new[] { MakeModel((GameDataStore)ctx.GameDataStore, new Position(10, 0, 0)) });

            GameSettings settings = ctx.Settings;
            settings.SeeThroughFriendlyUnits = seeThroughFriendlyUnits;
            ctx.Settings = settings;

            var combatCtx = new CombatActionContext(ctx, attackerBinding, isMelee: false);
            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            BindAllStageEvents(stage);
            await stage.Enter(combatCtx);

            return requester;
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

        // #385: the preview's cover flag and CoverCheckStage now share this one function;
        // CoverMajorityTests pins the stage side of the same rule.
        [Test]
        public void CoverMajority_CountsOnlyLivingModels()
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

            Assert.That(CoverMajority.Evaluate(attacker, defender, terrain,
                    applyProximityExceptions: true).HasCover, Is.False,
                "corpses behind the wall must not grant the lone survivor in the open a cover bonus");

            // Converse: when the LIVING model is the one behind the wall, cover applies (1 of 1).
            var aliveCovered = MakeUnit(store, enemy, "Defender2",
                new[] { MakeModel(store, new Position(20, 5)) });
            Assert.That(CoverMajority.Evaluate(attacker, aliveCovered, terrain,
                applyProximityExceptions: true).HasCover, Is.True);
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
            // #319: and it says so in a form the UI can render without parsing the reason string.
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
        // #371 Declare First — every weapon is aimed before any dice are rolled.
        // ──────────────────────────────────────────────────────────────────────

        // The control case, so the two modes are pinned against each other rather than in isolation:
        // One At A Time asks once and goes straight to the dice.
        [Test]
        public async Task OneAtATime_RoutesToFireAfterASingleChoice()
        {
            var (ctx, attacker, requester) = BuildDeclareFirstWorld(EShootingMode.OneAtATime);
            var combatCtx = new CombatActionContext(ctx, attacker, isMelee: false);
            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            int firedRoutes = 0;
            BindAllStageEvents(stage);
            stage.OnChoseWeapon.OnWillActivate += _ => firedRoutes++;

            await stage.Enter(combatCtx);

            Assert.Multiple(() =>
            {
                Assert.That(requester.Asked, Is.EqualTo(1), "one weapon is aimed per entry");
                Assert.That(firedRoutes, Is.EqualTo(1));
                Assert.That(combatCtx.AvailableWeapons, Has.Count.EqualTo(1),
                    "the pistol has not been touched yet - it is aimed on the NEXT entry");
            });
        }

        // The invariant the five pre-#371 "after a weapon has fired" tests guard implicitly, stated
        // outright: One At A Time must not pick up the declaration machinery. Those tests re-enter the
        // stage without modelling FireStage's consume, so a queued attack is still sitting there - and
        // the stage must still OFFER the next weapon rather than draining the queue. Gating the drain on
        // the queue alone (rather than on the mode) broke all five, which is what caught it.
        [Test]
        public async Task OneAtATime_WithAnAttackStillQueued_StillOffersTheNextWeapon()
        {
            var (ctx, attacker, requester) = BuildDeclareFirstWorld(EShootingMode.OneAtATime);
            var combatCtx = new CombatActionContext(ctx, attacker, isMelee: false);
            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            BindAllStageEvents(stage);

            await stage.Enter(combatCtx);
            Assert.That(combatCtx.HasPendingAttack, Is.True, "test setup: nothing has consumed the attack");

            await stage.Enter(combatCtx);   // re-entered WITHOUT a FireStage consume

            Assert.That(requester.Asked, Is.EqualTo(2),
                "the second entry asks for the pistol's target - it does not silently fire the queue");
        }

        [Test]
        public async Task DeclareFirst_AimsEveryWeaponBeforeRoutingToTheDice()
        {
            var (ctx, attacker, requester) = BuildDeclareFirstWorld(EShootingMode.DeclareFirst);
            var combatCtx = new CombatActionContext(ctx, attacker, isMelee: false);
            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            int firedRoutes = 0;
            BindAllStageEvents(stage);
            stage.OnChoseWeapon.OnWillActivate += _ => firedRoutes++;

            await stage.Enter(combatCtx);

            Assert.Multiple(() =>
            {
                Assert.That(requester.Asked, Is.EqualTo(2), "both weapons are aimed in one entry");
                Assert.That(firedRoutes, Is.EqualTo(1), "the volley starts once, after the last declaration");
                Assert.That(combatCtx.AvailableWeapons, Is.Empty);
                Assert.That(combatCtx.HasPendingAttack, Is.True, "two declarations are queued");
            });
        }

        // The declarations are not merely queued - each one keeps its OWN target, which is the whole
        // reason the pending entry carries it rather than reading the single DefendingUnit field.
        [Test]
        public async Task DeclareFirst_EachDeclarationFiresAtTheUnitItWasAimedAt()
        {
            var (ctx, attacker, _) = BuildDeclareFirstWorld(EShootingMode.DeclareFirst);
            var combatCtx = new CombatActionContext(ctx, attacker, isMelee: false);
            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            BindAllStageEvents(stage);

            await stage.Enter(combatCtx);

            ICombatMetadata first = combatCtx.ConsumeAttackIntoContext(ctx);
            ICombatMetadata second = combatCtx.ConsumeAttackIntoContext(ctx);

            Assert.Multiple(() =>
            {
                Assert.That(first.DefendingUnit.GetValue().Name, Is.EqualTo("Enemy0"));
                Assert.That(second.DefendingUnit.GetValue().Name, Is.EqualTo("Enemy1"));
                Assert.That(combatCtx.AttackedDefenderRefs.Count, Is.EqualTo(2),
                    "both are on the hook for morale, each measured from its own starting wounds");
            });
        }

        // The point of the mode: you commit before you know the outcome. A target killed by an earlier
        // weapon takes the shots declared against it with it.
        [Test]
        public async Task DeclareFirst_ShotsDeclaredAgainstADestroyedTargetAreLost()
        {
            // Both weapons aimed at the same unit, so the first volley can wipe the second one's target.
            var (ctx, attacker, requester) = BuildDeclareFirstWorld(EShootingMode.DeclareFirst,
                aimEverythingAt: "Enemy0");
            var combatCtx = new CombatActionContext(ctx, attacker, isMelee: false);
            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            bool endedShoot = false;
            BindAllStageEvents(stage);
            stage.OnNoValidShots.OnWillActivate += _ => endedShoot = true;

            await stage.Enter(combatCtx);
            Assert.That(requester.Asked, Is.EqualTo(2), "test setup: both weapons declared");

            combatCtx.ConsumeAttackIntoContext(ctx);            // the first volley resolves...
            KillUnit(FindEnemy(ctx, "Enemy0"));                 // ...and wipes the declared target

            int askedBefore = requester.Asked;
            await stage.Enter(combatCtx);                       // the weapon loop comes back for the pistol

            Assert.Multiple(() =>
            {
                Assert.That(requester.Asked, Is.EqualTo(askedBefore),
                    "the lost shots are NOT re-aimed - that would hand back the information the mode withholds");
                Assert.That(combatCtx.HasPendingAttack, Is.False, "the stale declaration was discarded");
                Assert.That(endedShoot, Is.True,
                    "the unit still shot, so the action ends through morale rather than simply stopping");
            });
        }

        // A survivor still gets shot: only the declaration whose own target died is dropped.
        [Test]
        public async Task DeclareFirst_ADeclarationAgainstASurvivorStillFires()
        {
            var (ctx, attacker, _) = BuildDeclareFirstWorld(EShootingMode.DeclareFirst);
            var combatCtx = new CombatActionContext(ctx, attacker, isMelee: false);
            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            BindAllStageEvents(stage);

            await stage.Enter(combatCtx);
            combatCtx.ConsumeAttackIntoContext(ctx);
            KillUnit(FindEnemy(ctx, "Enemy0"));                 // the FIRST target dies, not the second's

            await stage.Enter(combatCtx);

            Assert.That(combatCtx.HasPendingAttack, Is.True,
                "Enemy1 is still standing, so the shots declared against it are still owed");
            Assert.That(combatCtx.ConsumeAttackIntoContext(ctx).DefendingUnit.GetValue().Name,
                Is.EqualTo("Enemy1"));
        }

        // Declare First empties the weapon pool at declaration time, long before the last volley rolls.
        // DetermineCanKeepShootingStage must not read that as "the action is over".
        [Test]
        public async Task DeclareFirst_KeepShootingWaitsForTheQueueNotThePool()
        {
            var (ctx, attacker, _) = BuildDeclareFirstWorld(EShootingMode.DeclareFirst);
            var combatCtx = new CombatActionContext(ctx, attacker, isMelee: false);
            var chooseStage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            BindAllStageEvents(chooseStage);
            await chooseStage.Enter(combatCtx);
            combatCtx.ConsumeAttackIntoContext(ctx);            // the first of two declarations fires

            var keepShooting = new DetermineCanKeepShootingStage(ctx, new NoOpLayer<ICombatActionContext>());
            keepShooting.ReturnToChooseWeapon.Bind("test-return");
            keepShooting.ToFinishShooting.Bind("test-finish");
            bool returned = false, finished = false;
            keepShooting.ReturnToChooseWeapon.OnWillActivate += _ => returned = true;
            keepShooting.ToFinishShooting.OnWillActivate += _ => finished = true;

            await keepShooting.Enter(combatCtx);

            Assert.Multiple(() =>
            {
                Assert.That(combatCtx.AvailableWeapons, Is.Empty, "test setup: nothing left to declare");
                Assert.That(returned, Is.True, "the second declaration still has to be rolled");
                Assert.That(finished, Is.False);
            });
        }

        // ── What the resolvers are told about the standing declarations ──────────────────────────────

        // The GUI has to show the volley taking shape: a player who aims a weapon and is handed the
        // weapon list again reads that as a bug unless the shots already committed stay on screen.
        [Test]
        public async Task DeclareFirst_TheRequestCarriesWhatIsAlreadyDeclared()
        {
            var (ctx, attacker, requester) = BuildDeclareFirstWorld(EShootingMode.DeclareFirst);
            var seen = new List<List<DeclaredShot>>();
            var flags = new List<bool>();
            var chosen = new List<string>();
            var inner = requester.Reply;
            requester.Reply = req =>
            {
                seen.Add(req.Declarations.ToList());
                flags.Add(req.DeclareFirst);
                var reply = inner(req);
                chosen.Add(((Selected<RangedAttackChoice>)reply).Value.Weapon.Name);
                return reply;
            };

            var combatCtx = new CombatActionContext(ctx, attacker, isMelee: false);
            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            BindAllStageEvents(stage);

            await stage.Enter(combatCtx);

            Assert.Multiple(() =>
            {
                Assert.That(flags, Is.EqualTo(new[] { true, true }), "the mode is on both requests");
                Assert.That(seen[0], Is.Empty, "nothing is declared when the first weapon is aimed");
                Assert.That(seen[1], Has.Count.EqualTo(1), "the second request shows the first declaration");
            });
            Assert.Multiple(() =>
            {
                Assert.That(seen[1][0].Weapon.Name, Is.EqualTo(chosen[0]));
                Assert.That(seen[1][0].TargetUnit.GetValue().Name, Is.EqualTo("Enemy0"));
                Assert.That(seen[1][0].Copies, Is.EqualTo(1), "one carrier, so one shot is owed");
            });
        }

        // The mirror of the mode gate on the declaration drain: One At A Time never carries declarations,
        // on any path - including one that re-enters with an attack still queued.
        [Test]
        public async Task OneAtATime_TheRequestNeverCarriesDeclarations()
        {
            var (ctx, attacker, requester) = BuildDeclareFirstWorld(EShootingMode.OneAtATime);
            var seen = new List<List<DeclaredShot>>();
            var flags = new List<bool>();
            var inner = requester.Reply;
            requester.Reply = req =>
            {
                seen.Add(req.Declarations.ToList());
                flags.Add(req.DeclareFirst);
                return inner(req);
            };

            var combatCtx = new CombatActionContext(ctx, attacker, isMelee: false);
            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            BindAllStageEvents(stage);

            await stage.Enter(combatCtx);
            await stage.Enter(combatCtx);   // re-entered WITHOUT a FireStage consume: a queue exists

            Assert.Multiple(() =>
            {
                Assert.That(flags, Is.EqualTo(new[] { false, false }));
                Assert.That(seen.SelectMany(declarations => declarations), Is.Empty,
                    "a queued attack is not a declaration - this mode fires before it offers again");
            });
        }

        // ── #032 Limited under Declare First ─────────────────────────────────────────────────────────
        // Declaring IS the commit in both modes (there is no un-aiming), so a once-per-game weapon is
        // spent the moment it is aimed. These pin that it is spent exactly once and never re-offered.

        [Test]
        public async Task DeclareFirst_ALimitedWeaponIsSpentWhenDeclared_AndIsNotOfferedAgain()
        {
            Weapon rocket = LimitedWeapon("Rocket");
            var (ctx, attacker, requester) = BuildDeclareFirstWorld(EShootingMode.DeclareFirst,
                attackerWeapons: new[] { rocket, new Weapon("Pistol", 24f, 1, 0) });
            // Rocket first, so the second offer is the one that must no longer list it. The pool is a
            // ConcurrentDictionary, so the order has to be fixed here rather than assumed.
            var offered = new List<List<string>>();
            int pass = 0;
            requester.Reply = req =>
            {
                offered.Add(req.WeaponOptions.Select(option => option.Weapon.Name).ToList());
                return FireNamedWeaponAt(req, pass++ == 0 ? "Rocket" : "Pistol", "Enemy0");
            };

            var combatCtx = new CombatActionContext(ctx, attacker, isMelee: false);
            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            BindAllStageEvents(stage);

            await stage.Enter(combatCtx);

            Assert.Multiple(() =>
            {
                Assert.That(requester.Asked, Is.EqualTo(2), "test setup: both weapons declared");
                Assert.That(LimitedRules.IsSpent(attacker.GetValue(), rocket), Is.True,
                    "aiming a once-per-game weapon commits it - there is no un-declaring");
                Assert.That(offered[1], Does.Not.Contain("Rocket"),
                    "a declared weapon leaves the pool, so it cannot be aimed a second time this action");
            });
        }

        // The mode's bargain applied to its most expensive case: declare the rocket at a unit an earlier
        // weapon then wipes out and you have burned it for nothing. Deliberate, and worth pinning - it is
        // the one Declare First outcome a player would most likely report as a bug.
        [Test]
        public async Task DeclareFirst_ALimitedWeaponDeclaredAtADoomedTarget_IsStillSpent()
        {
            Weapon rocket = LimitedWeapon("Rocket");
            var (ctx, attacker, requester) = BuildDeclareFirstWorld(EShootingMode.DeclareFirst,
                attackerWeapons: new[] { Rifle(), rocket });
            // Rifle first, rocket second, both at Enemy0 - so the rocket is the declaration left holding
            // a corpse. (Reply order, not pool order: which weapon is offered first is not guaranteed.)
            int pass = 0;
            requester.Reply = req => FireNamedWeaponAt(req, pass++ == 0 ? "Rifle" : "Rocket", "Enemy0");

            var combatCtx = new CombatActionContext(ctx, attacker, isMelee: false);
            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            bool endedShoot = false;
            BindAllStageEvents(stage);
            stage.OnNoValidShots.OnWillActivate += _ => endedShoot = true;

            await stage.Enter(combatCtx);
            Assert.That(requester.Asked, Is.EqualTo(2), "test setup: rifle then rocket");

            combatCtx.ConsumeAttackIntoContext(ctx);            // the rifle fires...
            KillUnit(FindEnemy(ctx, "Enemy0"));                 // ...and kills the rocket's target

            await stage.Enter(combatCtx);

            Assert.Multiple(() =>
            {
                Assert.That(combatCtx.HasPendingAttack, Is.False, "the rocket's shot is lost");
                Assert.That(endedShoot, Is.True);
                Assert.That(LimitedRules.IsSpent(attacker.GetValue(), rocket), Is.True,
                    "spent for the game even though it never rolled - committing early is the whole bargain");
            });
        }

        // Reply helper: fire a NAMED weapon at a named target, so a test can fix the declaration order
        // (the available-weapon pool is a ConcurrentDictionary and does not promise one).
        private static CancellableResult<RangedAttackChoice> FireNamedWeaponAt(
            ChooseRangedAttackRequest req, string weaponName, string targetName)
        {
            var option = req.WeaponOptions.Single(o => o.Weapon.Name == weaponName);
            var target = option.WeaponTargetStats.Single(t => t.TargetUnit.GetValue().Name == targetName);
            Assert.That(target.UnselectableReason, Is.Null,
                $"test setup: {weaponName} must be able to shoot {targetName}");
            return new Selected<RangedAttackChoice>(new RangedAttackChoice(option.Weapon, target.TargetUnit));
        }

        /// <summary>Two weapons and two enemies, with a reply that aims each weapon at its own enemy
        /// (or all of them at <paramref name="aimEverythingAt"/> when named).</summary>
        private (TestGameContextWithRequester ctx, DataBinding<UnitData> attacker, CountingRangedRequester requester)
            BuildDeclareFirstWorld(EShootingMode mode, string? aimEverythingAt = null,
                Weapon[]? attackerWeapons = null)
        {
            var requester = new CountingRangedRequester();
            var (ctx, attacker) = BuildTwoTeamWorld(
                attackerPos: new Position(0, 0, 0),
                enemyPositions: new[] { new Position(5, 0, 0), new Position(6, 0, 0) },
                rifleRange: 24f,
                attackerWeapons: attackerWeapons ?? new[] { Rifle(), new Weapon("Pistol", 24f, 1, 0) },
                playerRequester: requester);
            ctx.Settings = ctx.Settings with { ShootingMode = mode };

            int pass = 0;
            requester.Reply = req => FireAtTargetNamed(req, aimEverythingAt ?? $"Enemy{pass++}");
            return (ctx, attacker, requester);
        }

        private static DataBinding<UnitData> FindEnemy(TestGameContextWithRequester ctx, string name) =>
            ctx.GameDataStore.GetAllDataBindings<UnitData>()
                .First(unit => unit.GetValue().Name == name);

        private static void KillUnit(DataBinding<UnitData> unit)
        {
            foreach (ModelData model in unit.GetValue().ModelBindings.Select(binding => binding.GetValue()))
                model.DealWounds(model.TotalWounds - model.WoundsDealt);
        }

        // Like CapturingRangedRequester, but counts the asks - "how many times was the player prompted"
        // IS the observable difference between the two shooting modes.
        internal class CountingRangedRequester : IPlayerRequestByID
        {
            public int Asked { get; private set; }
            public ChooseRangedAttackRequest? Captured { get; private set; }
            public Func<ChooseRangedAttackRequest, CancellableResult<RangedAttackChoice>> Reply { get; set; }
                = _ => new Cancelled<RangedAttackChoice>();

            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply>
            {
                if (request is ChooseRangedAttackRequest cr)
                {
                    Asked++;
                    Captured = cr;
                    object reply = Reply(cr)!;
                    return Task.FromResult((TReply)reply);
                }
                throw new InvalidOperationException("Unexpected request type: " + request.GetType());
            }
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
        // #319: Hold fire / Done shooting. Before this, a unit was FORCED to fire every weapon that had a
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

        // ──────────────────────────────────────────────────────────────────────
        // #325 pre-roll forecast: the effective hit/save numbers ride every
        // fireable row of the request, composed by the same code as the roll
        // beats' chips, so the targeting UI can show them before the commit.
        // ──────────────────────────────────────────────────────────────────────

        [Test]
        public async Task Enter_Forecast_RidesFireableRows_AndSkipsRowsWithNoShooters()
        {
            var requester = new CapturingRangedRequester { Reply = _ => new Cancelled<RangedAttackChoice>() };
            var (ctx, attackerBinding) = BuildTwoTeamWorld(
                attackerPos: new Position(0, 0, 0),
                enemyPositions: new[] { new Position(5, 0, 0), new Position(100, 0, 0) },
                rifleRange: 24f,
                playerRequester: requester);

            var combatCtx = new CombatActionContext(ctx, attackerBinding, isMelee: false);
            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            BindAllStageEvents(stage);
            await stage.Enter(combatCtx);

            var enemies = ctx.GameDataStore.GetAllDataBindings<ArmyData>()
                .First(a => a.GetValue().PlayerID != attackerBinding.GetValue().PlayerID)
                .GetValue().UnitBindings;
            var rows = requester.Captured!.WeaponOptions.Single().WeaponTargetStats;
            var inRange = rows.Single(t => t.TargetUnit.Reference.Equals(enemies[0].Reference));
            var outOfRange = rows.Single(t => t.TargetUnit.Reference.Equals(enemies[1].Reference));

            Assert.That(inRange.Forecast, Is.Not.Null, "a fireable row carries the numbers.");
            Assert.That(inRange.Forecast!.HitRollNeeded, Is.EqualTo(4), "attacker Quality 4, unmodified.");
            Assert.That(inRange.Forecast!.SaveRollNeeded, Is.EqualTo(4), "defender Defense 4, AP 0, no cover.");
            Assert.That(inRange.Forecast!.HitTags, Is.Null, "no chips when the number is just the stat.");
            Assert.That(inRange.Forecast!.SaveTags, Is.Null);
            Assert.That(outOfRange.Forecast, Is.Null, "no shooters - nothing to price.");
        }

        [Test]
        public void Compute_ApAndCover_FoldIntoTheSaveNumberAndChips()
        {
            var (ctx, attackerBinding) = BuildTwoTeamWorld(
                attackerPos: new Position(0, 0, 0),
                enemyPositions: new[] { new Position(5, 0, 0) },
                rifleRange: 24f,
                attackerWeapons: new[] { new Weapon("Cannon", 24f, 1, 2) });
            var enemy = FirstEnemy(ctx, attackerBinding);
            Weapon cannon = attackerBinding.GetValue().GetRangedWeapons().Single();

            var covered = ShootingForecast.Compute(ctx, attackerBinding, cannon, enemy,
                hasCover: true, ignoresCover: false, attackerMoved: false);
            Assert.That(covered.SaveRollNeeded, Is.EqualTo(5), "Defense 4 + AP 2 - Cover 1.");
            Assert.That(covered.SaveTags, Is.EqualTo(new[] { "Defense 4+", "AP 2", "Cover +1" }),
                "the same chips the save beat composes.");

            var coverIgnored = ShootingForecast.Compute(ctx, attackerBinding, cannon, enemy,
                hasCover: true, ignoresCover: true, attackerMoved: false);
            Assert.That(coverIgnored.SaveRollNeeded, Is.EqualTo(6), "a Blast-style weapon prices cover away.");
            Assert.That(coverIgnored.SaveTags, Does.Not.Contain("Cover +1"));
        }

        [Test]
        public void Compute_StealthDefender_RaisesTheHitThreshold_WithTheBeatChip()
        {
            var (ctx, attackerBinding) = BuildTwoTeamWorld(
                attackerPos: new Position(0, 0, 0),
                enemyPositions: new[] { new Position(12, 0, 0), new Position(5, 0, 0) },
                rifleRange: 24f);
            var enemies = ctx.GameDataStore.GetAllDataBindings<ArmyData>()
                .First(a => a.GetValue().PlayerID != attackerBinding.GetValue().PlayerID)
                .GetValue().UnitBindings;
            foreach (var enemyUnit in enemies)
            {
                enemyUnit.GetValue().AttachRuleDefinition(new ResolvedRule("Stealth", CoreRuleCatalog.Stealth));
            }
            Weapon rifle = attackerBinding.GetValue().GetRangedWeapons().First();

            var farShot = ShootingForecast.Compute(ctx, attackerBinding, rifle, enemies[0],
                hasCover: false, ignoresCover: false, attackerMoved: false);
            Assert.That(farShot.HitRollNeeded, Is.EqualTo(5), "Quality 4 shifted by Stealth's -1 beyond 9\".");
            Assert.That(farShot.HitTags, Is.EqualTo(new[] { "Quality 4+", "Stealth -1" }));

            var closeShot = ShootingForecast.Compute(ctx, attackerBinding, rifle, enemies[1],
                hasCover: false, ignoresCover: false, attackerMoved: false);
            Assert.That(closeShot.HitRollNeeded, Is.EqualTo(4), "within 9\" Stealth is silent.");
            Assert.That(closeShot.HitTags, Is.Null);
        }

        [Test]
        public void Compute_ShieldedDefender_LowersTheSaveThreshold_WithTheBeatChip()
        {
            var (ctx, attackerBinding) = BuildTwoTeamWorld(
                attackerPos: new Position(0, 0, 0),
                enemyPositions: new[] { new Position(5, 0, 0) },
                rifleRange: 24f);
            var enemy = FirstEnemy(ctx, attackerBinding);
            enemy.GetValue().AttachRuleDefinition(new ResolvedRule("Shielded", CoreRuleCatalog.Shielded));
            Weapon rifle = attackerBinding.GetValue().GetRangedWeapons().First();

            var forecast = ShootingForecast.Compute(ctx, attackerBinding, rifle, enemy,
                hasCover: false, ignoresCover: false, attackerMoved: false);

            Assert.That(forecast.SaveRollNeeded, Is.EqualTo(3), "Defense 4 improved by Shielded's +1.");
            Assert.That(forecast.SaveTags, Is.EqualTo(new[] { "Defense 4+", "Shielded +1" }));
        }

        [Test]
        public void Compute_ClampsThresholds_ToTheNaturalBand()
        {
            // A Quality 6 attacker into a Stealth target at range would need raw 7s; the forecast
            // clamps exactly as RollToHitStage does before rolling - a natural 6 always hits.
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var ctx = new TestGameContextWithRequester(store, new CapturingRangedRequester());
            var shooterModel = MakeModel(store, new Position(0, 0, 0), Rifle());
            var shooterUnit = new UnitData(new PlayerID(Guid.NewGuid()), "Militia", quality: 6, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { shooterModel });
            var shooterBinding = store.GetDataBinding<UnitData>(store.Create(shooterUnit));
            var targetModel = MakeModel(store, new Position(12, 0, 0));
            var targetUnit = new UnitData(new PlayerID(Guid.NewGuid()), "Sneaks", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { targetModel });
            targetUnit.AttachRuleDefinition(new ResolvedRule("Stealth", CoreRuleCatalog.Stealth));
            var targetBinding = store.GetDataBinding<UnitData>(store.Create(targetUnit));
            Weapon rifle = shooterUnit.GetRangedWeapons().Single();

            var forecast = ShootingForecast.Compute(ctx, shooterBinding, rifle, targetBinding,
                hasCover: false, ignoresCover: false, attackerMoved: false);

            Assert.That(forecast.HitRollNeeded, Is.EqualTo(6), "raw 7+ clamps to the 6s-always-hit band.");
            Assert.That(forecast.HitTags, Is.EqualTo(new[] { "Quality 6+", "Stealth -1" }),
                "the chips keep the honest arithmetic even when the number clamps.");
        }

        // The forecast crosses the network on a remote player's turn like the rest of the request.
        [Test]
        public void Forecast_RoundTripsThroughJson()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var playerID = new PlayerID(Guid.NewGuid());
            var attackerBinding = MakeUnit(store, playerID, "Attacker",
                new[] { MakeModel(store, new Position(0, 0, 0), Rifle()) });
            var targetBinding = MakeUnit(store, playerID, "Target",
                new[] { MakeModel(store, new Position(5, 0, 0)) });

            var forecast = new AttackForecast(5, 6,
                HitTags: new List<string> { "Quality 4+", "Stealth -1" },
                SaveTags: new List<string> { "Defense 4+", "AP 2" },
                Notes: new List<string> { "Mark on target: the first attack into it claims the marked bonus at roll time." });
            var stats = new WeaponTargetStats(targetBinding,
                new HashSet<DataBinding<ModelData>>(), new HashSet<DataBinding<ModelData>>(),
                Forecast: forecast);
            var request = new ChooseRangedAttackRequest(playerID, "ChooseRanged",
                attackerBinding, new List<WeaponOption>
                    { new WeaponOption(Rifle(), new List<WeaponTargetStats> { stats }) });

            string json = JsonConvert.SerializeObject(request, store.GetJsonSettings());
            var back = JsonConvert.DeserializeObject<ChooseRangedAttackRequest>(json, store.GetJsonSettings());

            var backForecast = back!.WeaponOptions[0].WeaponTargetStats[0].Forecast;
            Assert.That(backForecast, Is.Not.Null);
            Assert.That(backForecast!.HitRollNeeded, Is.EqualTo(5));
            Assert.That(backForecast!.SaveRollNeeded, Is.EqualTo(6));
            Assert.That(backForecast!.HitTags, Is.EqualTo(new[] { "Quality 4+", "Stealth -1" }));
            Assert.That(backForecast!.SaveTags, Is.EqualTo(new[] { "Defense 4+", "AP 2" }));
            Assert.That(backForecast!.Notes!.Single(), Does.Contain("Mark on target"));
        }

        private DataBinding<UnitData> FirstEnemy(TestGameContextWithRequester ctx,
            DataBinding<UnitData> attackerBinding)
        {
            return ctx.GameDataStore.GetAllDataBindings<ArmyData>()
                .First(a => a.GetValue().PlayerID != attackerBinding.GetValue().PlayerID)
                .GetValue().UnitBindings[0];
        }

        // #325: RuleDefinitions is [JsonIgnore], so a request that reached a remote player used to carry
        // rule-less weapons - the shoot panel showed "18\", A2 AP0" with Crack/Rending/etc. silently gone.
        // Weapon now rehydrates from its persisted blob on deserialization, so the receiving side reads
        // the same rules the host plays, descriptions included.
        [Test]
        public void RequestWeapon_RulesSurviveTheWire_ViaOnDeserializedRehydration()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var playerID = new PlayerID(Guid.NewGuid());
            var rocket = LimitedWeapon("Rocket");
            var attackerBinding = MakeUnit(store, playerID, "Attacker",
                new[] { MakeModel(store, new Position(0, 0, 0), rocket) });

            var request = new ChooseRangedAttackRequest(playerID, "ChooseRanged",
                attackerBinding, new List<WeaponOption>
                    { new WeaponOption(rocket, new List<WeaponTargetStats>()) });

            string json = JsonConvert.SerializeObject(request, store.GetJsonSettings());
            var back = JsonConvert.DeserializeObject<ChooseRangedAttackRequest>(json, store.GetJsonSettings());

            var backWeapon = back!.WeaponOptions[0].Weapon;
            Assert.That(backWeapon.RuleDefinitions, Has.Count.EqualTo(1),
                "the persisted blob must rehydrate on arrival - no consumer calls RehydrateRules by hand.");
            Assert.That(backWeapon.RuleDefinitions[0].RequestedName, Is.EqualTo("Limited"));
            Assert.That(backWeapon.RuleDefinitions[0].Definition.Description, Is.Not.Null.And.Not.Empty,
                "descriptions ride the blob, so rule tooltips work on the receiving side too.");
        }

        // ──────────────────────────────────────────────────────────────────────
        // #340: a Takedown weapon aims ONE COPY AT A TIME. Firing it commits a single rifle and hands the
        // rest back to the action's pool, so the picker offers the weapon again and every copy chooses its
        // own target unit - not just its own victim model inside the unit the first shot picked (#157).
        // ──────────────────────────────────────────────────────────────────────

        [Test]
        public async Task Enter_TakedownWeapon_FiresOneCopy_AndOffersTheRestAgain()
        {
            var requester = new CapturingRangedRequester { Reply = FireFirstFireable };
            var (ctx, attacker, _) = BuildSniperWorld(requester, snipers: 3, enemyUnits: 1);

            var combatCtx = new CombatActionContext(ctx, attacker, isMelee: false);
            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            BindAllStageEvents(stage);
            await stage.Enter(combatCtx);

            ICombatMetadata metadata = combatCtx.ConsumeAttackIntoContext(ctx);
            Assert.That(metadata.WeaponCount, Is.EqualTo(1),
                "one rifle fires per choice - the other two have not been aimed yet");

            Weapon rifle = combatCtx.AvailableWeapons.Keys.Single();
            Assert.That(combatCtx.AvailableWeapons[rifle], Is.EqualTo(2),
                "the unfired rifles go back into the pool, so the weapon is offered again");

            await stage.Enter(combatCtx);
            var option = requester.Captured!.WeaponOptions.Single();
            Assert.That(option.AimedIndividuallyRule, Is.EqualTo("Takedown"),
                "the request says WHY the weapon came back, so a resolver can label the row");
            Assert.That(option.CopiesRemaining, Is.EqualTo(2),
                "and how many rifles are still waiting to be aimed");
        }

        // #368: every weapon row now prints its copy count ("4x Rifle"), not just the Takedown ones, so
        // CopiesRemaining has to be right for an ORDINARY weapon too - it was previously documented as
        // "only meaningful to display when AimedIndividuallyRule is set", and nothing pinned the rest.
        [Test]
        public async Task Enter_OrdinaryWeapon_ReportsHowManyCopiesTheUnitIsFiring()
        {
            var requester = new CapturingRangedRequester { Reply = FireFirstFireable };
            var (ctx, attacker, _) = BuildSniperWorld(requester, snipers: 4, enemyUnits: 1,
                weaponTemplate: () => Rifle());

            var combatCtx = new CombatActionContext(ctx, attacker, isMelee: false);
            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            BindAllStageEvents(stage);
            await stage.Enter(combatCtx);

            var option = requester.Captured!.WeaponOptions.Single();
            Assert.That(option.AimedIndividuallyRule, Is.Null,
                "precondition: a plain rifle fires as one volley");
            Assert.That(option.CopiesRemaining, Is.EqualTo(4),
                "the row says how many rifles the volley is made of");
        }

        [Test]
        public async Task Enter_TakedownCopies_MayEachChooseADifferentTargetUnit()
        {
            // The bug this fixes: three snipers, one target unit for the lot of them.
            int pass = 0;
            var requester = new CapturingRangedRequester();
            requester.Reply = req => FireAtTargetNamed(req, pass++ == 0 ? "Enemy0" : "Enemy1");
            var (ctx, attacker, enemies) = BuildSniperWorld(requester, snipers: 3, enemyUnits: 2);

            var combatCtx = new CombatActionContext(ctx, attacker, isMelee: false);
            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            BindAllStageEvents(stage);

            await stage.Enter(combatCtx);
            ICombatMetadata first = combatCtx.ConsumeAttackIntoContext(ctx);

            await stage.Enter(combatCtx);
            ICombatMetadata second = combatCtx.ConsumeAttackIntoContext(ctx);

            Assert.That(first.DefendingUnit.Reference, Is.EqualTo(enemies[0].Reference));
            Assert.That(second.DefendingUnit.Reference, Is.EqualTo(enemies[1].Reference),
                "the second rifle aimed at a different unit entirely");
            Assert.That(combatCtx.AttackedDefenderRefs.Count, Is.EqualTo(2),
                "both units are on the hook for morale, each measured from its own starting wounds");
            Assert.That(new[] { first.BurstShotIndex, second.BurstShotIndex }, Is.EqualTo(new[] { 0, 1 }),
                "#276: consecutive copies are tagged in firing order so the attack beat rotates carriers");
        }

        [Test]
        public async Task Enter_TakedownCopies_StillBoundByTheTwoTargetCap()
        {
            // Owner ruling (#340): Takedown does not buy extra targets - the shoot action's 2-unit cap
            // binds sniper shots like everything else.
            int pass = 0;
            var requester = new CapturingRangedRequester();
            requester.Reply = req => FireAtTargetNamed(req, $"Enemy{pass++}");
            var (ctx, attacker, _) = BuildSniperWorld(requester, snipers: 3, enemyUnits: 3);

            var combatCtx = new CombatActionContext(ctx, attacker, isMelee: false);
            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            BindAllStageEvents(stage);

            await stage.Enter(combatCtx);                       // rifle 1 -> Enemy0
            combatCtx.ConsumeAttackIntoContext(ctx);
            await stage.Enter(combatCtx);                       // rifle 2 -> Enemy1
            combatCtx.ConsumeAttackIntoContext(ctx);

            // Third pass: the request must already have closed Enemy2 off, so the reply above never gets
            // the chance to break the cap.
            requester.Reply = req => FireAtTargetNamed(req, "Enemy0");
            await stage.Enter(combatCtx);

            var thirdUnit = requester.Captured!.WeaponOptions.Single()
                .WeaponTargetStats.Single(t => t.TargetUnit.GetValue().Name == "Enemy2");
            Assert.That(thirdUnit.UnselectableReason, Does.Contain("Already targeting 2 units"),
                "the third rifle may only add to a unit this action has already engaged");
        }

        [Test]
        public async Task Enter_LimitedTakedownWeapon_SpendsOneCarrierPerShot()
        {
            // A Limited + Takedown weapon (BlessedSisters' Crossbow-Mod) is the combination that would
            // break if firing one copy marked every carrier: rifle 1 would burn rifles 2 and 3.
            var requester = new CapturingRangedRequester { Reply = FireFirstFireable };
            Weapon crossbow = TakedownWeapon("Crossbow-Mod");
            crossbow.AttachRuleDefinition(new ResolvedRule("Limited", CoreRuleCatalog.Limited));
            var (ctx, attacker, _) = BuildSniperWorld(requester, snipers: 2, enemyUnits: 1,
                weaponTemplate: () =>
                {
                    var copy = TakedownWeapon("Crossbow-Mod");
                    copy.AttachRuleDefinition(new ResolvedRule("Limited", CoreRuleCatalog.Limited));
                    return copy;
                });

            var combatCtx = new CombatActionContext(ctx, attacker, isMelee: false);
            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            BindAllStageEvents(stage);

            await stage.Enter(combatCtx);
            combatCtx.ConsumeAttackIntoContext(ctx);

            Assert.That(LimitedRules.IsSpent(attacker.GetValue(), crossbow), Is.False,
                "only the carrier that fired is spent - the other crossbow still has its once-per-game shot");

            await stage.Enter(combatCtx);
            combatCtx.ConsumeAttackIntoContext(ctx);

            Assert.That(LimitedRules.IsSpent(attacker.GetValue(), crossbow), Is.True,
                "with both carriers fired the weapon is spent for the rest of the game");
        }

        // Reply helper: fire the first weapon at the named target unit (the row must be selectable).
        private static CancellableResult<RangedAttackChoice> FireAtTargetNamed(
            ChooseRangedAttackRequest req, string targetName)
        {
            var option = req.WeaponOptions.First(o =>
                o.WeaponTargetStats.Any(t => t.UnselectableReason == null && t.modelsThatCanShoot.Count > 0));
            var target = option.WeaponTargetStats.Single(t => t.TargetUnit.GetValue().Name == targetName);
            Assert.That(target.UnselectableReason, Is.Null, $"test setup: {targetName} must be selectable");
            return new Selected<RangedAttackChoice>(new RangedAttackChoice(option.Weapon, target.TargetUnit));
        }

        // A squad of Takedown-rifle carriers facing N single-model enemy units, all in range with clear
        // lines of sight (no terrain). One weapon PROFILE, so the copies pool into a single option.
        private static (TestGameContextWithRequester ctx, DataBinding<UnitData> attacker,
            List<DataBinding<UnitData>> enemies) BuildSniperWorld(
                IPlayerRequestByID requester, int snipers, int enemyUnits, Func<Weapon>? weaponTemplate = null)
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var attackerPlayer = new PlayerID(Guid.NewGuid());
            var enemyPlayer    = new PlayerID(Guid.NewGuid());
            var ctx = new TestGameContextWithRequester(store, requester);

            store.Create(new TeamData(0, new List<PlayerID> { attackerPlayer }));
            store.Create(new TeamData(1, new List<PlayerID> { enemyPlayer }));

            var attackerModels = new List<DataBinding<ModelData>>();
            for (int i = 0; i < snipers; i++)
            {
                Weapon weapon = weaponTemplate?.Invoke() ?? TakedownWeapon("Sniper Rifle");
                attackerModels.Add(MakeModel(store, new Position(0, i * 2), weapon));
            }
            var attackerUnit = MakeUnit(store, attackerPlayer, "Snipers", attackerModels);
            store.Create(new ArmyData(attackerPlayer, new List<DataBinding<UnitData>> { attackerUnit }));

            var enemies = new List<DataBinding<UnitData>>();
            for (int i = 0; i < enemyUnits; i++)
            {
                enemies.Add(MakeUnit(store, enemyPlayer, $"Enemy{i}",
                    new[] { MakeModel(store, new Position(10, i * 3)) }));
            }
            store.Create(new ArmyData(enemyPlayer, enemies));

            return (ctx, attackerUnit, enemies);
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

        // #345: an A2 rifle, so a trimmed volley's attack count is not merely its carrier count.
        private static Weapon TwoShotRifle(float range = 24f) =>
            new Weapon("Burst Rifle", range, 2, 0);

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
            // Settable so a test can flip a rule setting (#371 shooting mode) without a second context type.
            public GameSettings Settings { get; set; } = GameSettings.GetDefault();
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
