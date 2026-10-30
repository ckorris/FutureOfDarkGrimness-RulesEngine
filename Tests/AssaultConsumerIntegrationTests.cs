using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;
using static FDG.StageResolution.Requests.ChooseRangedAttackRequest;

namespace FDG.Tests
{
    // #424 Assault, the CONSUMERS: once the seam says a Heavy Pistol is both ranged and melee, every gate and
    // pool downstream must agree without being told. One Weapon object sits in both of the unit's attack pools;
    // an all-pistol unit can charge; the shooting stage offers the pistol at its 12"; the melee stage offers the
    // same pistol (without the range) with the same attacks, AP and rules; a Limited Assault weapon fired in the
    // shooting phase is spent for melee (once per game regardless of phase - the per-model token already keys on
    // the weapon, D4); and a phase-scoped weapon rule ("AP(+1) in melee") applies per USE, because the catalog's
    // melee/shooting gates are Condition.IsMelee() on the ATTACK, not on the weapon.
    [TestFixture]
    public class AssaultConsumerIntegrationTests
    {
        private GameDataStore _store = null!;
        private PlayerID _player;
        private PlayerID _foe;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _player = new PlayerID(Guid.NewGuid());
            _foe = new PlayerID(Guid.NewGuid());
        }

        // --- the pools ---------------------------------------------------------------------------------------

        [Test]
        public void TheUnitsTwoPools_HoldTheSameWeaponObject()
        {
            Weapon pistol = AssaultDefinitions.HeavyPistol();
            var sword = new Weapon("Sword", 0f, 2, 0);
            var rifle = new Weapon("Rifle", 24f, 1, 0);
            DataBinding<UnitData> unit = MakeUnit(_player, "Veterans", new Position(0f, 0f), pistol, sword, rifle);

            List<Weapon> ranged = unit.GetValue().GetRangedWeapons();
            List<Weapon> melee = unit.GetValue().GetMeleeWeapons();

            Assert.That(ranged.Select(w => w.Name), Is.EquivalentTo(new[] { "Heavy Pistol", "Rifle" }));
            Assert.That(melee.Select(w => w.Name), Is.EquivalentTo(new[] { "Heavy Pistol", "Sword" }));
            Assert.That(ranged.Single(w => w.Name == "Heavy Pistol"), Is.SameAs(melee.Single(w => w.Name == "Heavy Pistol")),
                "one weapon, one object, two pools - no split copy anywhere");
        }

        // "This weapon may only be used in this way" still wins: Strafing's exclusion is a different question
        // (may it be used in the ordinary pools at all?) and it keeps its answer whatever roles are granted.
        [Test]
        public void StrafeOnly_StillExcludesFromBothPools_EvenWithAssault()
        {
            Weapon bombs = AssaultDefinitions.HeavyPistol("Bomb Rack");
            bombs.AttachRuleDefinition(new ResolvedRule("Crossing Strike", WeaponRules36Definitions.CrossingStrike));
            DataBinding<UnitData> unit = MakeUnit(_player, "Bomber", new Position(0f, 0f), bombs);

            Assert.That(bombs.IsMelee(), Is.True, "the seam says both...");
            Assert.That(bombs.IsRanged(), Is.True);
            Assert.That(unit.GetValue().GetMeleeWeapons(), Is.Empty, "...and the pools still say neither");
            Assert.That(unit.GetValue().GetRangedWeapons(), Is.Empty);
        }

        [Test]
        public void MeleeRangeUtilities_PoolsThePistol_FromTheModelsInRange()
        {
            DataBinding<UnitData> unit = MakeUnit(_player, "Veterans", new Position(0f, 0f), AssaultDefinitions.HeavyPistol());

            List<Weapon> pooled = MeleeRangeUtilities.GetMeleeWeaponsFromModels(unit.GetValue().ModelBindings);

            Assert.That(pooled.Select(w => w.Name), Is.EqualTo(new[] { "Heavy Pistol" }));
        }

        // --- the gates ---------------------------------------------------------------------------------------

        // #355's predicate is what the charge gate and every AI melee-capability check share: an all-pistol
        // unit used to be written off as harmless in melee, and could not charge at all.
        [Test]
        public void AnAssaultOnlyUnit_CanFightInMelee_APlainPistolUnit_CannotStill()
        {
            DataBinding<UnitData> pistoleers = MakeUnit(_player, "Pistoleers", new Position(0f, 0f),
                AssaultDefinitions.HeavyPistol());
            DataBinding<UnitData> riflemen = MakeUnit(_player, "Riflemen", new Position(0f, 0f),
                new Weapon("Pistol", 12f, 1, 0));

            Assert.That(ChargeContactRules.CanFightInMelee(pistoleers.GetValue()), Is.True);
            Assert.That(ChargeContactRules.CanFightInMelee(riflemen.GetValue()), Is.False,
                "control: the same unit with a plain pistol still has nothing to swing");
        }

        [Test]
        public void Shooting_OffersThePistol_AtItsPrintedRange()
        {
            var requester = new AssaultRequester();
            var (ctx, attacker) = BuildTwoTeamWorld(requester, new Position(0f, 0f),
                new[] { AssaultDefinitions.HeavyPistol() }, new Position(10f, 0f));

            Assert.That(ChooseRangedAttackStage.HasAnyFireableTarget(attacker, ctx), Is.True, "10\" is within 12\"");

            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            BindRanged(stage);
            var combat = new CombatActionContext(ctx, attacker, isMelee: false);
            requester.RangedReply = _ => new Cancelled<RangedAttackChoice>();
            stage.Enter(combat).GetAwaiter().GetResult();

            WeaponOption option = requester.RangedCaptured!.WeaponOptions.Single();
            Assert.That(option.Weapon.Name, Is.EqualTo("Heavy Pistol"));
            Assert.That(option.Weapon.RangeInches, Is.EqualTo(12f));
            Assert.That(option.WeaponTargetStats.Single().UnselectableReason, Is.Null, "the target at 10\" is shootable");
        }

        [Test]
        public void Shooting_DoesNotOfferThePistol_BeyondItsPrintedRange()
        {
            var (ctx, attacker) = BuildTwoTeamWorld(new AssaultRequester(), new Position(0f, 0f),
                new[] { AssaultDefinitions.HeavyPistol() }, new Position(14f, 0f));

            Assert.That(ChooseRangedAttackStage.HasAnyFireableTarget(attacker, ctx), Is.False,
                "14\" is beyond the pistol's 12\" - the melee role adds no reach to the shot");
        }

        [Test]
        public async Task Melee_OffersThePistol_WithoutItsRange_AndCarriesItsRules()
        {
            Weapon pistol = AssaultDefinitions.HeavyPistol();
            var requester = new AssaultRequester { MeleePick = options => options.First(o => o.Contains("Heavy Pistol")) };
            var (ctx, attacker, combat) = BuildMelee(requester, pistol);

            await EnterMelee(ctx, combat);

            string option = requester.MeleeCaptured!.ValidOptions.Single(o => o.Contains("Heavy Pistol"));
            Assert.That(option, Does.Not.Contain("12"), "the melee line carries no range - it is the swing, not the shot");
            Assert.That(option, Does.Contain("A1, AP1"), "the same attacks and AP the shot has");
            Assert.That(option, Does.Contain("Assault"), "and the same rules");
            Assert.That(requester.MeleeCaptured.OptionRules![option].Select(r => r.Name), Is.EqualTo(new[] { "Assault" }),
                "#336: the row's rules travel structured too, so the panel can underline them");
            Assert.That(combat.AlreadyUsedWeapons.Keys.Single(), Is.SameAs(pistol), "the swing consumed the pistol itself");
        }

        // The headline: one unit, one pistol, one round - shoot at 12", then charge and strike with the same
        // weapon at the same attacks and AP. The shooting flow spends the ranged action's copy of the pool and
        // the melee flow builds its own from the same models, so both reach the same Weapon.
        [Test]
        public async Task OneRound_ShootsThenStrikes_WithTheSamePistol()
        {
            Weapon pistol = AssaultDefinitions.HeavyPistol();
            var requester = new AssaultRequester();
            var (ctx, attacker) = BuildTwoTeamWorld(requester, new Position(0f, 0f), new[] { pistol }, new Position(10f, 0f));
            DataBinding<UnitData> enemy = FirstEnemy(ctx, attacker);

            // Shooting phase of the activation: the ranged stage offers the pistol, the player aims it.
            var shoot = new CombatActionContext(ctx, attacker, isMelee: false);
            requester.RangedReply = request => new Selected<RangedAttackChoice>(
                new RangedAttackChoice(request.WeaponOptions.Single().Weapon, enemy));
            var rangedStage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            BindRanged(rangedStage);
            await rangedStage.Enter(shoot);

            Assert.That(shoot.AlreadyUsedWeapons.Keys.Single(), Is.SameAs(pistol), "fired");

            // A later activation of the same round: it charges and the melee stage offers the SAME pistol.
            var melee = new CombatActionContext(ctx, attacker, isMelee: true, isCharging: true);
            melee.SetInRangeAttackers(attacker.GetValue().ModelBindings.ToList());
            requester.MeleePick = options => options.Single(o => o.Contains("Heavy Pistol"));
            await EnterMelee(ctx, melee);

            Weapon swung = melee.AlreadyUsedWeapons.Keys.Single();
            Assert.That(swung, Is.SameAs(pistol), "struck with the very weapon that fired");
            Assert.That((swung.Attacks, swung.ArmorPenetration), Is.EqualTo((1, 1)), "same attacks, same AP");
        }

        // D4: Limited is once per game regardless of phase. The per-model LimitedSpent token keys on the weapon,
        // not on the flow that spent it, so the melee stage reads the shooting stage's spend without a bridge.
        [Test]
        public async Task ALimitedAssaultWeapon_FiredInShooting_IsSpentForMelee()
        {
            Weapon rocketPistol = AssaultDefinitions.HeavyPistol("Rocket Pistol");
            rocketPistol.AttachRuleDefinition(new ResolvedRule("Limited", CoreRuleCatalog.Limited));
            var sword = new Weapon("Sword", 0f, 1, 0);
            var requester = new AssaultRequester();
            var (ctx, attacker) = BuildTwoTeamWorld(requester, new Position(0f, 0f), new[] { rocketPistol, sword },
                new Position(10f, 0f));
            DataBinding<UnitData> enemy = FirstEnemy(ctx, attacker);

            var shoot = new CombatActionContext(ctx, attacker, isMelee: false);
            requester.RangedReply = request => new Selected<RangedAttackChoice>(
                new RangedAttackChoice(request.WeaponOptions.Single(o => o.Weapon.Name == "Rocket Pistol").Weapon, enemy));
            var rangedStage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            BindRanged(rangedStage);
            await rangedStage.Enter(shoot);
            Assume.That(LimitedRules.IsSpent(attacker.GetValue(), rocketPistol), Is.True, "the shooting stage spent it");

            var melee = new CombatActionContext(ctx, attacker, isMelee: true, isCharging: true);
            melee.SetInRangeAttackers(attacker.GetValue().ModelBindings.ToList());
            requester.MeleePick = options => options.First(o => o.Contains("Sword"));
            await EnterMelee(ctx, melee);

            Assert.That(requester.MeleeCaptured!.ValidOptions.Any(o => o.Contains("Rocket Pistol")), Is.False,
                "once per game: the pistol that fired this round cannot be swung");
            Assert.That(requester.MeleeCaptured.InvalidOptions.Any(o => o.Option.Contains("Rocket Pistol")
                    && o.Reason.Contains("Limited")), Is.True, "and the menu says why");
            Assert.That(requester.MeleeCaptured.ValidOptions.Any(o => o.Contains("Sword")), Is.True);
        }

        // --- per-use rules -------------------------------------------------------------------------------------

        // "X in melee" / "X when shooting" are Condition.IsMelee() gates on the ATTACK (the catalog's convention),
        // so one weapon reaching both pipelines gets each arm exactly where it belongs: the pistol's +1 AP in
        // melee pushes the save from 4+ to 5+ on the swing and leaves the shot alone.
        [Test]
        public async Task APhaseScopedWeaponRule_AppliesPerUse()
        {
            var meleeAp = new SpecialRuleDefinition("Bayonet",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnHitRollComplete, new Condition.IsMelee(),
                        new Effect.RollModifier(ERollKind.Save, -1), ELifetime.ThisAttack),
                },
                Array.Empty<ActivatedAbility>(), Scope: ERuleScope.Weapon);
            Weapon pistol = AssaultDefinitions.HeavyPistol(attacks: 1, ap: 0);
            pistol.AttachRuleDefinition(new ResolvedRule("Bayonet", meleeAp));

            var requester = new CapturingWoundRequester();
            var ctx = new WoundTestContext(_store, requester, new ProbabilisticDiceRoller());
            DataBinding<UnitData> attacker = MakeUnit(_player, "Pistoleers", new Position(0f, 0f), pistol, pistol, pistol);
            DataBinding<UnitData> defender = MakeUnit(_foe, "Grunts", new Position(1f, 0f),
                new Weapon("Fists", 0f, 1, 0), new Weapon("Fists", 0f, 1, 0), new Weapon("Fists", 0f, 1, 0),
                new Weapon("Fists", 0f, 1, 0), new Weapon("Fists", 0f, 1, 0));

            float shot = await Volley(ctx, attacker, defender, pistol, 3, isMelee: false);
            float swing = await Volley(ctx, attacker, defender, pistol, 3, isMelee: true);

            Assert.That(shot, Is.EqualTo(0.75f).Within(0.0001f), "3 x 0.5 hits x 0.5 fail a 4+ save - no AP when shooting");
            Assert.That(swing, Is.EqualTo(1f).Within(0.0001f), "3 x 0.5 hits x 4/6 fail a 5+ save - AP(+1) in melee");
        }

        // --- helpers ----------------------------------------------------------------------------------------

        private DataBinding<UnitData> MakeUnit(PlayerID owner, string name, Position at, params Weapon[] weaponsOnePerModel)
        {
            var models = new List<DataBinding<ModelData>>();
            for (int i = 0; i < weaponsOnePerModel.Length; i++)
            {
                var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon> { weaponsOnePerModel[i] },
                    initialPosition: new Position(at.x, at.z + i * 1.2f), gameDataStore: _store);
                models.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(owner, name, quality: 4, defense: 4, modelBindings: models);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }

        private (ChooseRangedAttackStageTests.TestGameContextWithRequester ctx, DataBinding<UnitData> attacker)
            BuildTwoTeamWorld(IPlayerRequestByID requester, Position attackerAt, Weapon[] attackerWeapons, Position enemyAt)
        {
            var ctx = new ChooseRangedAttackStageTests.TestGameContextWithRequester(_store, requester);
            _store.Create(new TeamData(0, new List<PlayerID> { _player }));
            _store.Create(new TeamData(1, new List<PlayerID> { _foe }));

            var attackerModel = new ModelData(0.5f, attackerWeapons.ToList(), attackerAt, _store);
            var attackerBinding = _store.GetDataBinding<ModelData>(_store.Create(attackerModel));
            var attackerUnit = new UnitData(_player, "Attacker", 4, 4, new List<DataBinding<ModelData>> { attackerBinding });
            var attacker = _store.GetDataBinding<UnitData>(_store.Create(attackerUnit));
            _store.Create(new ArmyData(_player, new List<DataBinding<UnitData>> { attacker }));

            var enemyModel = new ModelData(0.5f, new List<Weapon>(), enemyAt, _store);
            var enemyBinding = _store.GetDataBinding<ModelData>(_store.Create(enemyModel));
            var enemyUnit = new UnitData(_foe, "Enemy", 4, 4, new List<DataBinding<ModelData>> { enemyBinding });
            var enemy = _store.GetDataBinding<UnitData>(_store.Create(enemyUnit));
            _store.Create(new ArmyData(_foe, new List<DataBinding<UnitData>> { enemy }));

            return (ctx, attacker);
        }

        private static DataBinding<UnitData> FirstEnemy(IGameContext ctx, DataBinding<UnitData> attacker) =>
            ctx.GameDataStore.GetAllDataBindings<ArmyData>()
                .First(a => a.GetValue().PlayerID != attacker.GetValue().PlayerID)
                .GetValue().UnitBindings[0];

        private (ChooseRangedAttackStageTests.TestGameContextWithRequester ctx, DataBinding<UnitData> attacker,
            CombatActionContext combat) BuildMelee(AssaultRequester requester, params Weapon[] weapons)
        {
            var model = new ModelData(0.5f, weapons.ToList(), new Position(0f, 0f), _store);
            var modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));
            var unit = new UnitData(_player, "Attacker", 4, 4, new List<DataBinding<ModelData>> { modelBinding });
            var attacker = _store.GetDataBinding<UnitData>(_store.Create(unit));
            var ctx = new ChooseRangedAttackStageTests.TestGameContextWithRequester(_store, requester);
            var combat = new CombatActionContext(ctx, attacker, isMelee: true);
            combat.SetInRangeAttackers(new List<DataBinding<ModelData>> { modelBinding });
            return (ctx, attacker, combat);
        }

        private static void BindRanged(ChooseRangedAttackStage stage)
        {
            stage.OnChoseWeapon.Bind("test-on-chose-weapon");
            stage.BackToChooseAction.Bind("test-back-to-choose-action");
            stage.OnNoValidShots.Bind("test-on-no-valid-shots");
        }

        private static async Task EnterMelee(IGameContext ctx, ICombatActionContext combat)
        {
            var stage = new ChooseMeleeWeaponStage(ctx, new NoOpLayer<ICombatActionContext>());
            stage.OnChosen.Bind("test-on-chosen");
            stage.OnNoWeaponsLeftToSwing.Bind("test-no-weapons-left");
            await stage.Enter(combat);
        }

        // The real hit -> save -> wounds chain for one volley (the CombatMathPinTests shape); returns the wounds landed.
        private static async Task<float> Volley(WoundTestContext ctx, DataBinding<UnitData> attacker,
            DataBinding<UnitData> defender, Weapon weapon, int weaponCount, bool isMelee)
        {
            var metadata = new CombatMetadata(ctx, attacker, defender, weapon, weaponCount, attackerMoved: false,
                isMelee: isMelee, isCharging: isMelee);
            metadata.AddResult(new CoverCheckResults(0));
            await RunStage(new DetermineHitRollStage<ICombatMetadata>(ctx, new NoOpLayer<ICombatMetadata>()), metadata);
            await RunStage(new RollToHitStage<ICombatMetadata>(ctx, new NoOpLayer<ICombatMetadata>()), metadata);
            await RunStage(new DetermineSaveRollsNeededStage<ICombatMetadata>(ctx, new NoOpLayer<ICombatMetadata>()), metadata);
            await RunStage(new RollToSaveStage<ICombatMetadata>(ctx, new NoOpLayer<ICombatMetadata>()), metadata);
            await RunStage(new AssignWoundsStage<ICombatMetadata>(ctx, new NoOpLayer<ICombatMetadata>()), metadata);
            Assert.That(metadata.QueryForResult(out AssignWoundsResults results), Is.True);
            return results.TotalAssignedWounds;
        }

        private static async Task RunStage<TResult, TSelf>(CombatStage<TResult, TSelf, ICombatMetadata> stage,
            CombatMetadata metadata) where TSelf : CombatStage<TResult, TSelf, ICombatMetadata>
        {
            stage.NextStage.Bind("done");
            await stage.Enter(metadata);
        }

        // Answers the three request types the shoot-then-charge round raises.
        private sealed class AssaultRequester : IPlayerRequestByID
        {
            public ChooseRangedAttackRequest? RangedCaptured { get; private set; }
            public StringSelectionRequest? MeleeCaptured { get; private set; }
            public Func<ChooseRangedAttackRequest, CancellableResult<RangedAttackChoice>> RangedReply { get; set; }
                = _ => new Cancelled<RangedAttackChoice>();
            public Func<IReadOnlyList<string>, string> MeleePick { get; set; } = options => options.First();

            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply>
            {
                switch (request)
                {
                    case ChooseRangedAttackRequest ranged:
                        RangedCaptured = ranged;
                        return Task.FromResult((TReply)(object)RangedReply(ranged));
                    case StringSelectionRequest melee:
                        MeleeCaptured = melee;
                        return Task.FromResult((TReply)(object)MeleePick(melee.ValidOptions));
                    case YesNoRequest:
                        return Task.FromResult((TReply)(object)true);
                }
                throw new InvalidOperationException("Unexpected request type: " + request.GetType());
            }
        }
    }
}
