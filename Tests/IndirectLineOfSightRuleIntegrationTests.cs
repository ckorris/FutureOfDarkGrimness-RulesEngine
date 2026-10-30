using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for Indirect, re-authored for Core Rules v3.6.0 (#425): "When using Hold
    // actions and shooting, may target enemies that are not in line of sight as if in line of sight, and ignores
    // Cover from Sight Blockers." A Blocking terrain piece sits between attacker (0,5) and defender (20,5), so a
    // normal weapon is occluded; with Indirect on a Hold the REAL OcclusionCheckStage lets the shot through, after
    // an Advance it does not. The shooting target list (ChooseRangedAttackStage - what the CLI and GUI lists read)
    // and the REAL CoverCheckStage follow the same Hold gate: on a Hold the wall's own cover is ignored but Cover
    // terrain still counts (3.5 ignored all cover), after an Advance the shot is an ordinary one.
    //
    // #314 regression guard: Takedown carried the same LoS-/cover-ignore hooks from 2026-06-11 until
    // 2026-08-02 (copied off Indirect by the #042 checklist's mapping row), which let snipers shoot
    // through Blocking terrain. Its rule text grants neither, so the Takedown cases here assert the
    // OPPOSITE of Indirect's.
    [TestFixture]
    public class IndirectLineOfSightRuleIntegrationTests
    {
        private static readonly Position AttackerPos = new Position(0, 5);
        private static readonly Position BehindWallPos = new Position(20, 5);
        private static readonly RectangularZone WallRect = new RectangularZone(8, 12, 3, 7);
        // A clear line from AttackerPos: (0,5) -> (20,25) passes the wall's x-span at z 13..17, above it.
        private static readonly Position InSightPos = new Position(20, 25);

        private GameDataStore _store = null!;
        private TestGameContext _ctx = null!;
        private DataBinding<UnitData> _attacker = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ctx = new TestGameContext(_store, new FixedDiceRoller(4));
            _store.Create(new TerrainData(ETerrainType.Blocking, WallRect));
            _attacker = MakeUnit(AttackerPos);
        }

        [Test]
        public void Indirect_CarriesTheCore360Wording()
        {
            Assert.That(CoreRuleCatalog.Indirect.Description, Is.EqualTo(
                "When using Hold actions and shooting, may target enemies that are not in line of sight as if in " +
                "line of sight, and ignores Cover from Sight Blockers."));
        }

        // --- Line of sight: Hold only ----------------------------------------------------------------------

        [Test]
        public async Task NoIndirect_BehindWall_ShotIsOccluded()
        {
            DataBinding<UnitData> defender = MakeUnit(BehindWallPos);

            bool occluded = await RunOcclusionStage(_attacker, defender, IndirectWeapon(withRule: false), attackerMoved: false);

            Assert.That(occluded, Is.True, "a Blocking wall on the sight line occludes a normal shot.");
        }

        [Test]
        public async Task Indirect_OnAHold_BehindWall_ShotNotOccluded()
        {
            DataBinding<UnitData> defender = MakeUnit(BehindWallPos);

            bool occluded = await RunOcclusionStage(_attacker, defender, IndirectWeapon(), attackerMoved: false);

            Assert.That(occluded, Is.False, "on a Hold Indirect fires at the target as if in line of sight.");
        }

        [Test]
        public async Task Indirect_AfterAnAdvance_BehindWall_ShotIsOccluded()
        {
            DataBinding<UnitData> defender = MakeUnit(BehindWallPos);

            bool occluded = await RunOcclusionStage(_attacker, defender, IndirectWeapon(), attackerMoved: true);

            Assert.That(occluded, Is.True, "'when using Hold actions' - after moving, Indirect needs line of sight.");
        }

        // The target list the CLI and GUI resolvers render is the engine's eligibility: same Hold gate.
        [Test]
        public async Task TargetList_OnAHold_OffersTheTargetBehindTheWall_AfterAnAdvance_DoesNot()
        {
            (DataBinding<UnitData> shooter, DataBinding<UnitData> target) = MakeShootingWorld(IndirectWeapon());

            Assert.That(ChooseRangedAttackStage.HasAnyFireableTarget(shooter, _ctx, attackerMoved: false), Is.True,
                "Hold: the out-of-sight target is fireable.");
            Assert.That(ChooseRangedAttackStage.HasAnyFireableTarget(shooter, _ctx, attackerMoved: true), Is.False,
                "after an Advance the only target is out of sight - Shoot greys out.");

            // The action menu's gate asks with the unit's own HasMoved.
            var unitCtx = new UnitActionContext(_ctx, shooter);
            unitCtx.Reset(shooter);
            Assert.That(ChooseActionStage.GetCanShoot(_ctx, unitCtx, out _), Is.True, "Shoot offered on a Hold...");
            unitCtx.RegisterMoveFinished(distance: 3f, advanceAllowanceInches: GameWideConstants.MOVE_SHOOT_DISTANCE_INCHES);
            Assert.That(ChooseActionStage.GetCanShoot(_ctx, unitCtx, out string reason), Is.False, "...but not after an Advance.");
            Assert.That(reason, Is.EqualTo("No enemies in range or line of sight."));

            var requester = new CapturingRangedAttackRequester();
            var ctx = new TestGameContext(_store, new FixedDiceRoller(4), playerRequester: requester);
            await DriveChooseRangedAttack(ctx, shooter, attackerMoved: false);
            ChooseRangedAttackRequest.WeaponOption option = requester.Captured!.WeaponOptions.Single();
            Assert.That(option.LineOfSightIgnoreRule, Is.EqualTo("Indirect"), "and the row says why.");
            Assert.That(option.WeaponTargetStats.Single(s => s.TargetUnit.Reference.Equals(target.Reference))
                .modelsThatCanShoot.Count, Is.EqualTo(1));
        }

        [Test]
        public void SightRuleQueries_Indirect_AreHoldGated()
        {
            Weapon mortar = IndirectWeapon();
            RuleEvaluator ev = _ctx.RuleEvaluator;
            IUnit attacker = _attacker.GetValue();

            Assert.That(SightRuleQueries.LineOfSightIgnoreSource(attacker, mortar, ev, attackerMoved: false),
                Is.EqualTo("Indirect"), "Hold -> ignores line of sight (flags the targeting resolver requests).");
            Assert.That(SightRuleQueries.SightBlockerCoverFor(attacker, mortar, ev, attackerMoved: false),
                Is.EqualTo(ESightBlockerCover.Ignored), "Hold -> ignores cover from Sight Blockers.");
            Assert.That(SightRuleQueries.IgnoresCover(attacker, mortar, ev, attackerMoved: false), Is.False,
                "never ALL cover - Cover terrain still counts (3.6).");

            Assert.That(SightRuleQueries.IgnoresTerrain(attacker, mortar, ev, attackerMoved: true), Is.False,
                "after moving: needs line of sight.");
            Assert.That(SightRuleQueries.SightBlockerCoverFor(attacker, mortar, ev, attackerMoved: true),
                Is.EqualTo(ESightBlockerCover.BlocksSight), "after moving: an ordinary shot.");
        }

        // #314: a sniper shooting through a Blocking wall.
        [Test]
        public async Task Takedown_BehindWall_ShotIsOccluded()
        {
            AttachRule(_attacker, "Takedown", CoreRuleCatalog.Takedown);
            DataBinding<UnitData> defender = MakeUnit(BehindWallPos);

            bool occluded = await RunOcclusionStage(_attacker, defender, IndirectWeapon(withRule: false), attackerMoved: false);

            Assert.That(occluded, Is.True,
                "Takedown re-scopes the attack to one model - it does NOT see through a Blocking wall.");
        }

        // #314: the same wrong facet also reached the resolvers' per-weapon flags, so pin the query too.
        [Test]
        public void SightRuleQueries_Takedown_IgnoresNeitherLoSNorCover()
        {
            var weapon = new Weapon("Test", rangeInches: 48f, attacks: 1, armorPenetration: 0);
            RuleEvaluator ev = _ctx.RuleEvaluator;

            AttachRule(_attacker, "Takedown", CoreRuleCatalog.Takedown);

            Assert.That(SightRuleQueries.IgnoresTerrain(_attacker.GetValue(), weapon, ev, attackerMoved: false), Is.False,
                "Takedown -> does not ignore LoS.");
            Assert.That(SightRuleQueries.IgnoresCover(_attacker.GetValue(), weapon, ev, attackerMoved: false), Is.False,
                "Takedown -> does not ignore cover.");
        }

        // --- Cover: Sight Blockers' cover is ignored on a Hold, Cover terrain never ------------------------

        [Test]
        public async Task Hold_TargetInTheOpenBehindTheWall_GetsNoCover()
        {
            DataBinding<UnitData> defender = MakeUnit(BehindWallPos);

            int bonus = await RunCoverStage(defender, IndirectWeapon(), attackerMoved: false);

            Assert.That(bonus, Is.EqualTo(0), "'ignores Cover from Sight Blockers' - the wall grants nothing.");
        }

        [Test]
        public async Task Hold_TargetInCoverTerrainBehindTheWall_KeepsThatCover()
        {
            _store.Create(new TerrainData(ETerrainType.Cover, new RectangularZone(18, 22, 3, 7)));
            DataBinding<UnitData> defender = MakeUnit(BehindWallPos);

            int bonus = await RunCoverStage(defender, IndirectWeapon(), attackerMoved: false);

            Assert.That(bonus, Is.EqualTo(1),
                "the wood the target stands in is Cover terrain, not a Sight Blocker - and the wall in front no longer " +
                "masks it (an ordinary sight test would stop at the wall and see no cover at all).");
        }

        [Test]
        public async Task Hold_TargetInCoverTerrainInSight_KeepsThatCover()
        {
            _store.Create(new TerrainData(ETerrainType.Cover, new RectangularZone(18, 22, 23, 27)));
            DataBinding<UnitData> defender = MakeUnit(InSightPos);

            int bonus = await RunCoverStage(defender, IndirectWeapon(), attackerMoved: false);

            Assert.That(bonus, Is.EqualTo(1), "3.5 Indirect ignored all cover; 3.6 only cover from Sight Blockers.");
        }

        [Test]
        public async Task AfterAnAdvance_TargetInCoverTerrain_GetsCoverNormally()
        {
            _store.Create(new TerrainData(ETerrainType.Cover, new RectangularZone(18, 22, 23, 27)));
            DataBinding<UnitData> defender = MakeUnit(InSightPos);

            Assert.That(await RunCoverStage(defender, IndirectWeapon(), attackerMoved: true), Is.EqualTo(1),
                "an Indirect weapon that moved shoots like any other.");
            Assert.That(await RunCoverStage(defender, IndirectWeapon(withRule: false), attackerMoved: false), Is.EqualTo(1),
                "control: a plain weapon sees the same cover.");
        }

        // --- helpers ---------------------------------------------------------------------------------------

        private static Weapon IndirectWeapon(bool withRule = true)
        {
            var mortar = new Weapon("Mortar", rangeInches: 48f, attacks: 1, armorPenetration: 0);
            if (withRule) mortar.AttachRuleDefinition(new ResolvedRule("Indirect", CoreRuleCatalog.Indirect));
            return mortar;
        }

        // Returns true if the shot was occluded (OnOccluded fired, no result stored), false otherwise.
        private async Task<bool> RunOcclusionStage(DataBinding<UnitData> attacker, DataBinding<UnitData> defender,
            Weapon weapon, bool attackerMoved)
        {
            var stage = new OcclusionCheckStage(_ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");
            stage.OnOccluded.Bind("occluded");

            var metadata = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1,
                attackerMoved: attackerMoved);

            await stage.Enter(metadata);

            return !metadata.QueryForResult(out OcclusionCheckResults _);
        }

        private async Task<int> RunCoverStage(DataBinding<UnitData> defender, Weapon weapon, bool attackerMoved)
        {
            var stage = new CoverCheckStage(_ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");
            var metadata = new CombatMetadata(_ctx, _attacker, defender, weapon, weaponCount: 1,
                attackerMoved: attackerMoved);
            await stage.Enter(metadata);
            Assert.That(metadata.QueryForResult(out CoverCheckResults result), Is.True);
            return result.DefenseRollBonus;
        }

        // A shooter carrying the weapon on its model (so the target list enumerates it) and one enemy behind the
        // wall, with teams and armies registered.
        private (DataBinding<UnitData> Shooter, DataBinding<UnitData> Target) MakeShootingWorld(Weapon weapon)
        {
            var us = new PlayerID(Guid.NewGuid());
            var them = new PlayerID(Guid.NewGuid());
            _store.Create(new TeamData(0, new List<PlayerID> { us }));
            _store.Create(new TeamData(1, new List<PlayerID> { them }));
            // (0,4) -> (20,5) crosses the wall's x-span at z ~4.5: squarely through it, and clear of the fixture's
            // own _attacker base at (0,5).
            DataBinding<UnitData> shooter = MakeUnit(new Position(0, 4), us, weapon);
            DataBinding<UnitData> target = MakeUnit(BehindWallPos, them);
            _store.Create(new ArmyData(us, new List<DataBinding<UnitData>> { shooter }));
            _store.Create(new ArmyData(them, new List<DataBinding<UnitData>> { target }));
            return (shooter, target);
        }

        private static async Task DriveChooseRangedAttack(IGameContext ctx, DataBinding<UnitData> shooter, bool attackerMoved)
        {
            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            stage.OnChoseWeapon.Bind("Fire");
            stage.BackToChooseAction.Bind("Back");
            stage.OnNoValidShots.Bind("Done");
            await stage.Enter(new CombatActionContext(ctx, shooter, isMelee: false, attackerMoved: attackerMoved));
        }

        private static void AttachRule(DataBinding<UnitData> unit, string name, SpecialRuleDefinition def) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule(name, def));

        private DataBinding<UnitData> MakeUnit(Position pos, PlayerID? owner = null, Weapon? weapon = null)
        {
            var md = new ModelData(
                baseRadiusInches: 0.75f,
                weapons: weapon == null ? new List<Weapon>() : new List<Weapon> { weapon },
                initialPosition: pos,
                gameDataStore: _store);
            var modelBinding = _store.GetDataBinding<ModelData>(_store.Create(md));

            var unit = new UnitData(owner ?? new PlayerID(Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
