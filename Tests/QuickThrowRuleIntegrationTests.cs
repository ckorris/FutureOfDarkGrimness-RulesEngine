using System;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #425 - Quick Throw (book rule, 3.6.0: Human Inquisition, Ratmen Clans, and the AoF Dragon Empire, Ratmen,
    // Duchies of Vinci and Dwarves books): "This model may always target enemies that are not in line of sight when
    // shooting with Indirect weapons (instead of only when using Hold actions)."
    //
    // The shipped shape (FdgRaylib/Assets/Books/GdfRuleSupplement.json, inherited by AoF - hand-built here because
    // the engine suite cannot read the app's supplement; QuickThrowShippedDataTests asserts the JSON matches): a
    // weapon-scoped rule routed onto the model's weapons, queuing IgnoreLineOfSight at Shooting_OnSaveRollModifier
    // on the weapon that also carries Indirect, with no move condition. The text lifts the Hold gate for TARGETING
    // only, so Indirect's Sight-Blocker-cover bypass stays Hold-only: after an Advance a Quick Throw shot can be
    // aimed past the wall, but the wall's cover counts (ESightBlockerCover.GrantsCover).
    [TestFixture]
    public class QuickThrowRuleIntegrationTests
    {
        public static SpecialRuleDefinition QuickThrow { get; } = new("Quick Throw",
            new[]
            {
                new HookEntry(EHookID.Shooting_OnSaveRollModifier,
                    new Condition.WeaponHasRule("Indirect"),
                    new Effect.IgnoreLineOfSight(),
                    ELifetime.ThisAttack),
            },
            Array.Empty<ActivatedAbility>(),
            ERuleScope.Weapon,
            Valence: EValence.Positive,
            Description: "This model may always target enemies that are not in line of sight when shooting with " +
                "Indirect weapons (instead of only when using Hold actions).");

        private static readonly Position AttackerPos = new Position(0, 5);
        private static readonly Position BehindWallPos = new Position(20, 5);

        private GameDataStore _store = null!;
        private TestGameContext _ctx = null!;
        private DataBinding<UnitData> _attacker = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ctx = new TestGameContext(_store, new FixedDiceRoller(4));
            _store.Create(new TerrainData(ETerrainType.Blocking, new RectangularZone(8, 12, 3, 7)));
            _attacker = MakeUnit(AttackerPos);
        }

        [Test]
        public void QuickThrow_LiftsTheHoldGate_ForLineOfSight()
        {
            IUnit attacker = _attacker.GetValue();
            Weapon bombs = Weapon(indirect: true, quickThrow: true);

            Assert.That(SightRuleQueries.IgnoresTerrain(attacker, bombs, _ctx.RuleEvaluator, attackerMoved: true), Is.True,
                "'may always target enemies that are not in line of sight' - after moving too.");
            Assert.That(SightRuleQueries.LineOfSightIgnoreSource(attacker, bombs, _ctx.RuleEvaluator, attackerMoved: true),
                Is.EqualTo("Quick Throw"), "attributed to the rule that lifted the gate.");
            Assert.That(SightRuleQueries.IgnoresTerrain(attacker, Weapon(indirect: true, quickThrow: false),
                _ctx.RuleEvaluator, attackerMoved: true), Is.False, "control: plain Indirect after moving needs LoS.");
        }

        [Test]
        public void QuickThrow_DoesNotLiftTheHoldGate_ForTheSightBlockerCoverBypass()
        {
            IUnit attacker = _attacker.GetValue();
            Weapon bombs = Weapon(indirect: true, quickThrow: true);

            Assert.That(SightRuleQueries.SightBlockerCoverFor(attacker, bombs, _ctx.RuleEvaluator, attackerMoved: true),
                Is.EqualTo(ESightBlockerCover.GrantsCover),
                "after moving: may aim past the wall, but the text says nothing about cover - the wall's counts.");
            Assert.That(SightRuleQueries.SightBlockerCoverFor(attacker, bombs, _ctx.RuleEvaluator, attackerMoved: false),
                Is.EqualTo(ESightBlockerCover.Ignored), "on a Hold Indirect's own bypass applies as usual.");
        }

        [Test]
        public void QuickThrow_OnANonIndirectWeapon_DoesNothing()
        {
            Assert.That(SightRuleQueries.IgnoresTerrain(_attacker.GetValue(), Weapon(indirect: false, quickThrow: true),
                    _ctx.RuleEvaluator, attackerMoved: false), Is.False,
                "'when shooting with Indirect weapons' - a plain weapon routed the rule gains nothing.");
        }

        // Through the real stages: after an Advance the shot behind the wall is not occluded, and the target
        // keeps the cover the wall gives it.
        [Test]
        public async Task AfterAnAdvance_ShotBehindTheWall_IsNotOccluded_AndTheWallGivesCover()
        {
            DataBinding<UnitData> defender = MakeUnit(BehindWallPos);
            Weapon bombs = Weapon(indirect: true, quickThrow: true);

            var occlusion = new OcclusionCheckStage(_ctx, new NoOpLayer<ICombatMetadata>());
            occlusion.NextStage.Bind("done");
            occlusion.OnOccluded.Bind("occluded");
            var occlusionMeta = new CombatMetadata(_ctx, _attacker, defender, bombs, weaponCount: 1, attackerMoved: true);
            await occlusion.Enter(occlusionMeta);
            Assert.That(occlusionMeta.QueryForResult(out OcclusionCheckResults _), Is.True, "not occluded.");

            Assert.That(await CoverBonus(defender, bombs, attackerMoved: true), Is.EqualTo(1),
                "'behind Sight Blockers' - Quick Throw does not ignore that cover.");
            Assert.That(await CoverBonus(defender, bombs, attackerMoved: false), Is.EqualTo(0),
                "control: on a Hold, Indirect ignores Cover from Sight Blockers.");
        }

        private async Task<int> CoverBonus(DataBinding<UnitData> defender, Weapon weapon, bool attackerMoved)
        {
            var stage = new CoverCheckStage(_ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");
            var metadata = new CombatMetadata(_ctx, _attacker, defender, weapon, weaponCount: 1, attackerMoved: attackerMoved);
            await stage.Enter(metadata);
            Assert.That(metadata.QueryForResult(out CoverCheckResults result), Is.True);
            return result.DefenseRollBonus;
        }

        private static Weapon Weapon(bool indirect, bool quickThrow)
        {
            var weapon = new Weapon("Juggler Bombs", rangeInches: 24f, attacks: 1, armorPenetration: 2);
            if (indirect) weapon.AttachRuleDefinition(new ResolvedRule("Indirect", CoreRuleCatalog.Indirect));
            if (quickThrow) weapon.AttachRuleDefinition(new ResolvedRule("Quick Throw", QuickThrow));
            return weapon;
        }

        private DataBinding<UnitData> MakeUnit(Position pos)
        {
            var model = new ModelData(0.75f, new List<Weapon>(), pos, _store);
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "TestUnit", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { _store.GetDataBinding<ModelData>(_store.Create(model)) });
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
