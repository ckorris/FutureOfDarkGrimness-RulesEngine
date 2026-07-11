using System.Linq;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #197 (P15): the Unpredictable family. "When attacking, roll one
    // die: 1-3 -> AP(+1), 4-6 -> +1 to hit." The die is rolled ONCE per attack action
    // (UnpredictableBranchResolver) and carried on the combat metadata as an EUnpredictableBranch, so both
    // arms read the same branch: the +1-to-hit arm folds at DetermineHitRollStage (HitBonus), and AP(+1) - a
    // -1 save modifier, same machinery as Thrust - folds at RollToHitStage (ApBonus). Because one branch
    // value drives both arms, exactly one ever fires. Fighter gates on melee, Shooter on shooting.
    [TestFixture]
    public class UnpredictableRuleIntegrationTests
    {
        private static readonly Position AttackerPos = new Position(0, 5);
        private static readonly Position DefenderPos = new Position(3, 5);

        private GameDataStore _store = null!;
        private TestGameContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ctx = new TestGameContext(_store, new FixedDiceRoller(4),
                ruleResolver: CoreRuleCatalog.CreateResolver());
        }

        // --- the +1-to-hit arm (HitBonus branch, DetermineHitRollStage) ---

        [Test]
        public async Task Unpredictable_HitBonusBranch_LowersHitThreshold()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            Attach(attacker, CoreRuleCatalog.Unpredictable);

            DetermineHitRollResults result = await RunHitStage(attacker, MakeUnit(DefenderPos),
                branch: EUnpredictableBranch.HitBonus);

            Assert.That(result.HitRollNeeded, Is.EqualTo(3),
                "base 4, lowered by 1 because the 4-6 branch gives +1 to hit.");
        }

        [Test]
        public async Task Unpredictable_ApBonusBranch_DoesNotTouchHit()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            Attach(attacker, CoreRuleCatalog.Unpredictable);

            DetermineHitRollResults result = await RunHitStage(attacker, MakeUnit(DefenderPos),
                branch: EUnpredictableBranch.ApBonus);

            Assert.That(result.HitRollNeeded, Is.EqualTo(4),
                "the 1-3 branch is AP, not +1 to hit; the hit threshold is unchanged.");
        }

        [Test]
        public async Task Unpredictable_NoBranch_NoChange()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            Attach(attacker, CoreRuleCatalog.Unpredictable);

            DetermineHitRollResults result = await RunHitStage(attacker, MakeUnit(DefenderPos),
                branch: EUnpredictableBranch.None);

            Assert.That(result.HitRollNeeded, Is.EqualTo(4),
                "no branch (no die rolled) means neither arm fires.");
        }

        // --- the AP arm (ApBonus branch, RollToHitStage -> SaveModifier) ---

        [Test]
        public async Task Unpredictable_ApBonusBranch_AppliesSaveModifier()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            Attach(attacker, CoreRuleCatalog.Unpredictable);

            RollToHitResults result = await RunRollToHit(attacker, MakeUnit(DefenderPos),
                branch: EUnpredictableBranch.ApBonus);

            Assert.That(result.SaveModifier, Is.EqualTo(-1),
                "the 1-3 branch is AP(+1), carried as a -1 save modifier.");
        }

        [Test]
        public async Task Unpredictable_HitBonusBranch_AppliesNoSaveModifier()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            Attach(attacker, CoreRuleCatalog.Unpredictable);

            RollToHitResults result = await RunRollToHit(attacker, MakeUnit(DefenderPos),
                branch: EUnpredictableBranch.HitBonus);

            Assert.That(result.SaveModifier, Is.EqualTo(0),
                "the 4-6 branch is +1 to hit, not AP; no save modifier - so exactly one arm ever fires.");
        }

        // --- Fighter: melee only ---

        [Test]
        public async Task UnpredictableFighter_HitBonus_Melee_LowersHitThreshold()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            Attach(attacker, CoreRuleCatalog.UnpredictableFighter);

            DetermineHitRollResults result = await RunHitStage(attacker, MakeUnit(DefenderPos),
                branch: EUnpredictableBranch.HitBonus, isMelee: true);

            Assert.That(result.HitRollNeeded, Is.EqualTo(3), "Fighter applies +1 to hit in melee.");
        }

        [Test]
        public async Task UnpredictableFighter_Shooting_NoChange()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            Attach(attacker, CoreRuleCatalog.UnpredictableFighter);

            DetermineHitRollResults result = await RunHitStage(attacker, MakeUnit(DefenderPos),
                branch: EUnpredictableBranch.HitBonus, isMelee: false);

            Assert.That(result.HitRollNeeded, Is.EqualTo(4), "Fighter is melee-only; a shooting attack is unaffected.");
        }

        // --- Shooter: shooting only ---

        [Test]
        public async Task UnpredictableShooter_HitBonus_Shooting_LowersHitThreshold()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            Attach(attacker, CoreRuleCatalog.UnpredictableShooter);

            DetermineHitRollResults result = await RunHitStage(attacker, MakeUnit(DefenderPos),
                branch: EUnpredictableBranch.HitBonus, isMelee: false);

            Assert.That(result.HitRollNeeded, Is.EqualTo(3), "Shooter applies +1 to hit when shooting.");
        }

        [Test]
        public async Task UnpredictableShooter_Melee_NoChange()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            Attach(attacker, CoreRuleCatalog.UnpredictableShooter);

            DetermineHitRollResults result = await RunHitStage(attacker, MakeUnit(DefenderPos),
                branch: EUnpredictableBranch.HitBonus, isMelee: true);

            Assert.That(result.HitRollNeeded, Is.EqualTo(4), "Shooter is shooting-only; a melee swing is unaffected.");
        }

        // --- UnpredictableBranchResolver: the once-per-action decisive roll ---

        [Test]
        public void Resolver_RollsHighToHitBonus()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            Attach(attacker, CoreRuleCatalog.Unpredictable);

            EUnpredictableBranch branch = UnpredictableBranchResolver.Resolve(
                attacker.GetValue(), isMelee: false, new FixedDiceRoller(5));

            Assert.That(branch, Is.EqualTo(EUnpredictableBranch.HitBonus), "a 5 is in the 4-6 (+1 to hit) band.");
        }

        [Test]
        public void Resolver_RollsLowToApBonus()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            Attach(attacker, CoreRuleCatalog.Unpredictable);

            EUnpredictableBranch branch = UnpredictableBranchResolver.Resolve(
                attacker.GetValue(), isMelee: false, new FixedDiceRoller(2));

            Assert.That(branch, Is.EqualTo(EUnpredictableBranch.ApBonus), "a 2 is in the 1-3 (AP) band.");
        }

        [Test]
        public void Resolver_NoRule_ReturnsNoneWithoutRolling()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            // A roller that throws if asked for a face - proves no die is consumed when the rule is absent.
            EUnpredictableBranch branch = UnpredictableBranchResolver.Resolve(
                attacker.GetValue(), isMelee: false, new ThrowingDiceRoller());

            Assert.That(branch, Is.EqualTo(EUnpredictableBranch.None),
                "no Unpredictable rule -> None, and no die is rolled (the seeded stream is untouched).");
        }

        [Test]
        public void Resolver_FighterInShooting_IsNotApplicable()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            Attach(attacker, CoreRuleCatalog.UnpredictableFighter);

            EUnpredictableBranch branch = UnpredictableBranchResolver.Resolve(
                attacker.GetValue(), isMelee: false, new ThrowingDiceRoller());

            Assert.That(branch, Is.EqualTo(EUnpredictableBranch.None),
                "Unpredictable Fighter does not apply to a shooting attack, so no die is rolled.");
        }

        [Test]
        public void Resolver_DetectsAuraGrantedRule()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            // "Unpredictable Fighter Aura" confers "Unpredictable Fighter" as a RuleGrant token, not a native
            // rule - the resolver must still see it, or aura units would silently never roll.
            attacker.GetValue().Tokens.AddToken(new Token(TokenType.RuleGrant, 1,
                new TokenClearTrigger.ManualOnly(),
                Payload: new TokenPayload.RuleGrant(CoreRuleCatalog.UnpredictableFighterRuleName, ELifetime.Aura)));

            EUnpredictableBranch branch = UnpredictableBranchResolver.Resolve(
                attacker.GetValue(), isMelee: true, new FixedDiceRoller(5));

            Assert.That(branch, Is.EqualTo(EUnpredictableBranch.HitBonus),
                "an aura-granted Unpredictable Fighter must trigger the roll in melee.");
        }

        // --- once per attack ACTION, shared across weapons (Option A), via the real CombatActionContext ---

        [Test]
        public void CombatActionContext_RollsOncePerAction_SharedAcrossWeapons()
        {
            // The roller's successive faces would give DIFFERENT branches (5 -> HitBonus, then 2 -> ApBonus).
            // If the die were re-rolled per weapon, the second weapon's metadata would read ApBonus; rolling
            // once per action and caching it means both weapons of the action share the first branch.
            var ctx = new TestGameContext(_store, new SequenceDiceRoller(5, 2),
                ruleResolver: CoreRuleCatalog.CreateResolver());
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos, MeleeWeapon("Axe"), MeleeWeapon("Sword"));
            Attach(attacker, CoreRuleCatalog.UnpredictableFighter);
            DataBinding<UnitData> defender = MakeUnit(DefenderPos);

            var combat = new CombatActionContext(ctx, attacker, isMelee: true);
            combat.SetDefender(defender);

            var weapons = attacker.GetValue().GetMeleeWeapons().ToList();
            combat.SetAttackWeapon(weapons[0], out _);
            ICombatMetadata first = combat.ConsumeAttackIntoContext(ctx);
            combat.SetAttackWeapon(weapons[1], out _);
            ICombatMetadata second = combat.ConsumeAttackIntoContext(ctx);

            Assert.That(first.UnpredictableBranch, Is.EqualTo(EUnpredictableBranch.HitBonus),
                "the action's die (a 5) picks the +1-to-hit branch and threads onto the first weapon's metadata.");
            Assert.That(second.UnpredictableBranch, Is.EqualTo(EUnpredictableBranch.HitBonus),
                "the branch is cached once per action - the second weapon shares it rather than re-rolling to ApBonus.");
        }

        // --- harness ---

        private async Task<DetermineHitRollResults> RunHitStage(
            DataBinding<UnitData> attacker, DataBinding<UnitData> defender,
            EUnpredictableBranch branch, bool isMelee = false)
        {
            var layer = new NoOpLayer<ICombatMetadata>();
            var stage = new DetermineHitRollStage<ICombatMetadata>(_ctx, layer);
            stage.NextStage.Bind("done");

            CombatMetadata metadata = MakeMetadata(attacker, defender, isMelee, branch);
            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out DetermineHitRollResults result), Is.True,
                "Stage must store a DetermineHitRollResults in metadata.");
            return result;
        }

        private async Task<RollToHitResults> RunRollToHit(
            DataBinding<UnitData> attacker, DataBinding<UnitData> defender,
            EUnpredictableBranch branch, bool isMelee = false)
        {
            var layer = new NoOpLayer<ICombatMetadata>();
            var stage = new RollToHitStage<ICombatMetadata>(_ctx, layer);
            stage.NextStage.Bind("done");

            CombatMetadata metadata = MakeMetadata(attacker, defender, isMelee, branch);
            metadata.AddResult(new DetermineHitRollResults(4, attackCount: 1));

            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out RollToHitResults result), Is.True,
                "Stage must store a RollToHitResults in metadata.");
            return result;
        }

        private CombatMetadata MakeMetadata(DataBinding<UnitData> attacker, DataBinding<UnitData> defender,
            bool isMelee, EUnpredictableBranch branch)
        {
            var weapon = new Weapon("Test", rangeInches: 48f, attacks: 1, armorPenetration: 0);
            return new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1,
                attackerMoved: false, isMelee: isMelee, isCharging: false, unpredictableBranch: branch);
        }

        private static void Attach(DataBinding<UnitData> unit, SpecialRuleDefinition rule) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule(rule.Name, rule));

        private static Weapon MeleeWeapon(string name) =>
            new Weapon(name, rangeInches: 0f, attacks: 1, armorPenetration: 0);

        private DataBinding<UnitData> MakeUnit(Position position, params Weapon[] weapons)
        {
            var model = new ModelData(
                baseRadiusInches: 0.75f,
                weapons: weapons.ToList(),
                initialPosition: position,
                gameDataStore: _store);
            DataBinding<ModelData> modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));

            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }

        // Fails loudly if a die is ever requested - used to prove the resolver rolls nothing when no
        // applicable rule is present.
        private sealed class ThrowingDiceRoller : IDiceRoller
        {
            public IDiceResults Roll(int sideCount, float rollCount) =>
                throw new System.InvalidOperationException("No die should be rolled when Unpredictable does not apply.");
        }

        // Yields its faces in order across successive rolls (clamping on the last), so a test can tell a
        // rolled-once-and-cached value apart from a re-rolled one.
        private sealed class SequenceDiceRoller : IDiceRoller
        {
            private readonly int[] _faces;
            private int _index;

            public SequenceDiceRoller(params int[] faces) => _faces = faces;

            public IDiceResults Roll(int sideCount, float rollCount) =>
                new FixedDiceResults(_faces[System.Math.Min(_index++, _faces.Length - 1)]);
        }
    }
}
