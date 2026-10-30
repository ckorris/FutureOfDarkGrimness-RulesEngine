using System.Linq;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using FDG.Tests.RulesHarness;
using NUnit.Framework;

namespace FDG.Tests
{
    // #432 - the OFFENSIVE half of the OPR 3.6 range-conditional family ("When this model shoots/charges enemies over
    // 9"/6" away, ..."), through the REAL stages: Devout's extra hit (RollToHitStage), Havocbound's AP(+1)
    // (RollToHitStage -> DetermineSaveRollsNeededStage), Infected's extra wound (AssignWoundsStage), the Versatile
    // Warrior activation pick (the real offer / accept, then the granted helper), and the charge distance's plumbing
    // from the activation-start snapshot into the hit context. Every case flips on distance: shooting at 10" vs 8",
    // charging from 7" vs 5". Units are Quality 4 / Defense 4, one model each, 0.5" bases; distances are base edge to
    // base edge, as every range gate measures them.
    [TestFixture]
    public class RangeConditionalOffensiveRuleIntegrationTests
    {
        private const float BaseRadius = 0.5f;

        private GameDataStore _store = null!;
        private WoundTestContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ctx = new WoundTestContext(_store, new CapturingWoundRequester(), new FixedDiceRoller(6));
        }

        // --- Devout (and Ferocious / Brutal / Lucky): an extra hit on each natural 6 ------------------------------

        [Test]
        public async Task Devout_ShootingAt10Inches_ScoresTheExtraHit()
        {
            DataBinding<UnitData> shooter = MakeUnit("Shooter", z: 0f, RangeConditional36Definitions.ExtraHit());

            Assert.That(TotalHits(await Hits(shooter, MakeTarget(), distance: 10f)), Is.EqualTo(2f),
                "a natural 6 at 10\": one hit plus Devout's extra");
        }

        [Test]
        public async Task Devout_ShootingAt8Inches_ScoresNoExtraHit()
        {
            DataBinding<UnitData> shooter = MakeUnit("Shooter", z: 0f, RangeConditional36Definitions.ExtraHit());

            Assert.That(TotalHits(await Hits(shooter, MakeTarget(), distance: 8f)), Is.EqualTo(1f),
                "8\" is not over 9\" - the 6 is just a hit");
        }

        [Test]
        public async Task Ferocious_ChargingFrom7Inches_ScoresTheExtraHit()
        {
            DataBinding<UnitData> charger = MakeUnit("Charger", z: 0f,
                RangeConditional36Definitions.ExtraHit("Ferocious"));

            Assert.That(TotalHits(await Hits(charger, MakeTarget(), distance: 0.5f, isMelee: true, isCharging: true,
                chargeOrigin: 7f)), Is.EqualTo(2f), "a charge from 7\" is over 6\" - the melee 6 scores again");
        }

        [Test]
        public async Task Ferocious_ChargingFrom5Inches_ScoresNoExtraHit()
        {
            DataBinding<UnitData> charger = MakeUnit("Charger", z: 0f,
                RangeConditional36Definitions.ExtraHit("Ferocious"));

            Assert.That(TotalHits(await Hits(charger, MakeTarget(), distance: 0.5f, isMelee: true, isCharging: true,
                chargeOrigin: 5f)), Is.EqualTo(1f));
        }

        [Test]
        public async Task Ferocious_StrikingBack_NeverScoresTheExtraHit()
        {
            DataBinding<UnitData> defender = MakeUnit("Defender", z: 0f,
                RangeConditional36Definitions.ExtraHit("Ferocious"));

            Assert.That(TotalHits(await Hits(defender, MakeTarget(), distance: 0.5f, isMelee: true,
                isCharging: false)), Is.EqualTo(1f), "a strike-back is not a charge");
        }

        [Test]
        public async Task Devout_HistogramMode_PricesTheExtraHitAtOneInSix()
        {
            // #100 histogram mode: 6 attack dice at Quality 4 hit 3.0 times on average, and Devout adds 1/6 per die
            // that shows a natural 6 - 1.0 extra over 9", nothing inside it.
            _ctx = new WoundTestContext(_store, new CapturingWoundRequester(), new ProbabilisticDiceRoller());
            DataBinding<UnitData> shooter = MakeUnit("Shooter", z: 0f, RangeConditional36Definitions.ExtraHit());

            Assert.That(TotalHits(await Hits(shooter, MakeTarget(), distance: 10f, attacks: 6)),
                Is.EqualTo(4f).Within(0.0001f));
            Assert.That(TotalHits(await Hits(shooter, MakeTarget(), distance: 8f, attacks: 6)),
                Is.EqualTo(3f).Within(0.0001f));
        }

        // --- Havocbound (and Piercing Warrior): AP(+1) ------------------------------------------------------------

        [TestCase(10f, false, 0f, 5)]
        [TestCase(8f, false, 0f, 4)]
        [TestCase(0.5f, true, 7f, 5)]
        [TestCase(0.5f, true, 5f, 4)]
        public async Task Havocbound_GetsApPlusOne_OnlyOverRange(float distance, bool charging, float origin,
            int expectedSave)
        {
            DataBinding<UnitData> attacker = MakeUnit("Raider", z: 0f, RangeConditional36Definitions.ApPlusOne());

            Assert.That(await SaveNeeded(attacker, MakeTarget(), distance, charging, origin), Is.EqualTo(expectedSave),
                "Defense 4, plus AP(+1) only when shooting from over 9\" / charging from over 6\"");
        }

        // --- Infected (and Violent / Warbound / Destroyer): 1s to block deal an extra wound -----------------------

        [TestCase(10f, false, 0f, 2f)]
        [TestCase(8f, false, 0f, 1f)]
        [TestCase(0.5f, true, 7f, 2f)]
        [TestCase(0.5f, true, 5f, 1f)]
        public async Task Infected_OnesToBlockDealAnExtraWound_OnlyOverRange(float distance, bool melee, float origin,
            float expectedWounds)
        {
            Weapon claws = WeaponWith(RangeConditional36Definitions.ExtraWound());

            Assert.That(await WoundsFromOneFailedSave(claws, MakeTarget(), distance, melee, origin),
                Is.EqualTo(expectedWounds), "one natural 1 to block: one wound, two over range");
        }

        [Test]
        public async Task Infected_IgnoresSpellDamage_WhichIsNotShooting()
        {
            Weapon spell = WeaponWith(RangeConditional36Definitions.ExtraWound());

            Assert.That(await WoundsFromOneFailedSave(spell, MakeTarget(), 15f, melee: false, origin: 0f,
                isSpell: true), Is.EqualTo(1f), "\"hits from weapons\" - a spell 15\" away is not shooting");
        }

        // --- Versatile Warrior (and Watchborn / Vinci Tech): the activation pick ----------------------------------

        [TestCase("AP(+1)", 10f, 0, -1)]
        [TestCase("AP(+1)", 8f, 0, 0)]
        [TestCase("+1 to hit", 10f, 1, 0)]
        [TestCase("+1 to hit", 8f, 0, 0)]
        public void VersatileWarrior_ThePickApplies_OnlyOverRange(string label, float distance, int expectedHit,
            int expectedSave)
        {
            TestRuleHarness harness = PickHarness();
            IUnit warriors = harness.BuildUnit("P1", 3, "Versatile Warrior");
            IUnit enemy = harness.BuildUnit("P2", 1);

            AbilityOffer offer = harness.OfferAbilities(new ActivationStartContext(warriors))
                .Single(o => o.Ability.Label == label);
            OperationApplier.ApplyTokenOperations(harness.Accept(offer, warriors));

            var hit = new RollModifierSink();
            hit.ApplyFrom(harness.Evaluate(warriors, ERuleSeat.Actor,
                new HitRollModifierContext(warriors, enemy, distance)));
            var save = new RollModifierSink();
            save.ApplyFrom(harness.Evaluate(warriors, ERuleSeat.Actor,
                new HitRollCompleteContext(warriors, enemy, TestDice.Faces(4), distance)));

            Assert.That(hit.Net(ERollKind.Hit), Is.EqualTo(expectedHit), $"{label} at {distance}\"");
            Assert.That(save.Net(ERollKind.Save), Is.EqualTo(expectedSave), $"{label} at {distance}\"");
        }

        [Test]
        public void VersatileWarrior_TheChargeArm_ReadsSixInches()
        {
            TestRuleHarness harness = PickHarness();
            IUnit warriors = harness.BuildUnit("P1", 3, "Versatile Warrior");
            IUnit enemy = harness.BuildUnit("P2", 1);
            AbilityOffer offer = harness.OfferAbilities(new ActivationStartContext(warriors))
                .Single(o => o.Ability.Label == "AP(+1)");
            OperationApplier.ApplyTokenOperations(harness.Accept(offer, warriors));

            int Save(float origin)
            {
                var sink = new RollModifierSink();
                sink.ApplyFrom(harness.Evaluate(warriors, ERuleSeat.Actor,
                    new HitRollCompleteContext(warriors, enemy, TestDice.Faces(4), 0.5f, IsMelee: true,
                        IsCharging: true, ChargeOriginDistanceInches: origin)));
                return sink.Net(ERollKind.Save);
            }

            Assert.That(Save(7f), Is.EqualTo(-1));
            Assert.That(Save(5f), Is.EqualTo(0));
        }

        [Test]
        public void VersatileWarrior_WithoutAPick_GrantsNothing()
        {
            TestRuleHarness harness = PickHarness();
            IUnit warriors = harness.BuildUnit("P1", 3, "Versatile Warrior");
            IUnit enemy = harness.BuildUnit("P2", 1);

            var hit = new RollModifierSink();
            hit.ApplyFrom(harness.Evaluate(warriors, ERuleSeat.Actor,
                new HitRollModifierContext(warriors, enemy, 10f)));

            Assert.That(hit.Net(ERollKind.Hit), Is.EqualTo(0), "the rule itself is only the pick");
            Assert.That(harness.OfferAbilities(new ActivationStartContext(warriors)).Select(o => o.Ability.Label),
                Is.EquivalentTo(new[] { "AP(+1)", "+1 to hit" }));
        }

        // --- the charge distance's plumbing ------------------------------------------------------------------------

        [TestCase(8f, 2f)]
        [TestCase(6f, 1f)]
        public async Task TheChargeDistance_IsTheActivationStartGap_ThroughTheRealChargePath(float gap,
            float expectedHits)
        {
            // Two armies on two teams so the activation-start snapshot sees the enemy. The charger starts `gap`
            // inches (base to base) from its target, moves into contact, and the charge's swing - built by the real
            // CombatActionContext from the real UnitActionContext - reads the pre-move gap.
            var p1 = new PlayerID(System.Guid.NewGuid());
            var p2 = new PlayerID(System.Guid.NewGuid());
            DataBinding<UnitData> charger = MakeUnit("Charger", z: 0f, RangeConditional36Definitions.ExtraHit("Ferocious"),
                player: p1, meleeWeapon: true);
            DataBinding<UnitData> target = MakeUnit("Target", z: gap + 2f * BaseRadius, null, player: p2);
            _store.Create(new ArmyData(p1, new List<DataBinding<UnitData>> { charger }));
            _store.Create(new ArmyData(p2, new List<DataBinding<UnitData>> { target }));
            _store.Create(new TeamData(0, new List<PlayerID> { p1 }));
            _store.Create(new TeamData(1, new List<PlayerID> { p2 }));

            var unitContext = new UnitActionContext(_ctx, charger);
            unitContext.Reset(charger);
            charger.GetValue().ModelBindings[0].GetValue().SetPosition(new Position(0f, gap));  // into contact

            var combat = new CombatActionContext(_ctx, charger, isMelee: true, attackerMoved: true, isCharging: true,
                activationContext: unitContext);
            combat.SetDefender(target);
            combat.SetAttackWeapon(charger.GetValue().GetMeleeWeapons().Single(), out _);
            ICombatMetadata metadata = combat.ConsumeAttackIntoContext(_ctx);
            Assert.That(metadata.ChargeOriginDistanceInches, Is.EqualTo(gap).Within(0.01f));

            var stage = new RollToHitStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");
            metadata.AddResult(new DetermineHitRollResults(4, 1));
            await stage.Enter(metadata);
            Assert.That(metadata.QueryForResult(out RollToHitResults result), Is.True);

            Assert.That(TotalHits(result), Is.EqualTo(expectedHits),
                $"a charge from {gap}\": Ferocious's extra hit {(expectedHits > 1f ? "applies" : "does not")}");
        }

        // --- helpers --------------------------------------------------------------------------------------------

        private static TestRuleHarness PickHarness()
        {
            var harness = new TestRuleHarness();
            harness.Register(RangeConditional36Definitions.Pick());
            harness.Register(RangeConditional36Definitions.PickPiercing());
            harness.Register(RangeConditional36Definitions.PickPrecision());
            return harness;
        }

        private async Task<RollToHitResults> Hits(DataBinding<UnitData> attacker, DataBinding<UnitData> defender,
            float distance, bool isMelee = false, bool isCharging = false, float chargeOrigin = 0f, int attacks = 1)
        {
            Place(defender, distance);
            var weapon = new Weapon("Gun", rangeInches: isMelee ? 0f : 24f, attacks: attacks, armorPenetration: 0);
            var stage = new RollToHitStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");
            var metadata = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1,
                attackerMoved: false, isMelee: isMelee, isCharging: isCharging, chargeOriginDistanceInches: chargeOrigin);
            metadata.AddResult(new DetermineHitRollResults(4, attacks));
            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out RollToHitResults result), Is.True);
            return result;
        }

        private async Task<int> SaveNeeded(DataBinding<UnitData> attacker, DataBinding<UnitData> defender,
            float distance, bool charging, float origin)
        {
            RollToHitResults hits = await Hits(attacker, defender, distance, isMelee: charging, isCharging: charging,
                chargeOrigin: origin);

            var stage = new DetermineSaveRollsNeededStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");
            var metadata = new CombatMetadata(_ctx, attacker, defender, new Weapon("Gun", 24f, 1, 0), weaponCount: 1);
            metadata.AddResult(hits);
            metadata.AddResult(new CoverCheckResults(0));
            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out DetermineSaveRollNeededResults result), Is.True);
            return result.PendingSaveRollsList.Max(p => p.SaveNeeded);
        }

        private async Task<float> WoundsFromOneFailedSave(Weapon weapon, DataBinding<UnitData> defender, float distance,
            bool melee, float origin, bool isSpell = false)
        {
            DataBinding<UnitData> attacker = MakeUnit("Attacker", z: 0f, null);
            Place(defender, distance);

            var stage = new AssignWoundsStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");
            var metadata = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1, attackerMoved: false,
                isMelee: melee, isCharging: melee, isSpell: isSpell, chargeOriginDistanceInches: origin);
            var failed = new List<FailedSaveInfo>
            {
                new FailedSaveInfo(TestDice.Faces(1), new PendingSaveRolls(TestDice.Faces(1), 4)),
            };
            metadata.AddResult(new RollToSaveResults(new List<SuccessfulSaveInfo>(), failed));
            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out AssignWoundsResults result), Is.True);
            return result.TotalWoundsToAssign;
        }

        private static float TotalHits(RollToHitResults results) => results.SuccessfulHitList.Sum(h => h.HitCount);

        private static Weapon WeaponWith(SpecialRuleDefinition rule)
        {
            var weapon = new Weapon("Claws", rangeInches: 24f, attacks: 1, armorPenetration: 0);
            weapon.AttachRuleDefinition(new ResolvedRule(rule.Name, rule));
            return weapon;
        }

        private DataBinding<UnitData> MakeTarget() => MakeUnit("Target", z: 10f, null);

        private DataBinding<UnitData> MakeUnit(string name, float z, SpecialRuleDefinition? rule,
            PlayerID? player = null, bool meleeWeapon = false)
        {
            var weapons = meleeWeapon
                ? new List<Weapon> { new Weapon("Blade", 0f, 1, 0) }
                : new List<Weapon>();
            var model = new ModelData(BaseRadius, weapons, new Position(0f, z), _store);
            var bindings = new List<DataBinding<ModelData>> { _store.GetDataBinding<ModelData>(_store.Create(model)) };
            var unit = new UnitData(player ?? new PlayerID(System.Guid.NewGuid()), name, quality: 4, defense: 4,
                modelBindings: bindings);
            if (rule != null) unit.AttachRuleDefinition(new ResolvedRule(rule.Name, rule));
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }

        // Base edge to base edge: the attacker sits at z = 0, so the target's model sits at the distance plus the two
        // base radii.
        private static void Place(DataBinding<UnitData> unit, float edgeDistance)
        {
            unit.GetValue().ModelBindings[0].GetValue().SetPosition(new Position(0f, edgeDistance + 2f * BaseRadius));
        }
    }
}
