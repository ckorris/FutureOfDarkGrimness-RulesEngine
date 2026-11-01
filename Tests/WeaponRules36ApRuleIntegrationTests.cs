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
    // #431 - the hit/AP half of the OPR 3.6 weapon-rule family, through the REAL RollToHitStage and
    // DetermineSaveRollsNeededStage (the RendingRuleIntegrationTests setup): Pierce (both facets), Fracture (with
    // #418's Fatigue view), Puncture, Disintegrate, Destructive, Demolish and Purge - each with its gate met and a
    // control - plus the cover-ignoring rules (Anti-Cover, Demolish, Pierce) through the REAL CoverCheckStage, and
    // Imprecise through the REAL DetermineHitRollStage. Units are Quality 4 / Defense 4, the weapon AP(0): the
    // base save threshold is 4, and every AP(+X) shows as 4 + X.
    [TestFixture]
    public class WeaponRules36ApRuleIntegrationTests
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

        // --- Pierce ---------------------------------------------------------------------------------------------

        [Test]
        public async Task Pierce_OverHalfRange_GetsApPlusOne()
        {
            int needed = await SaveNeeded(WeaponWith(WeaponRules36Definitions.Pierce, range: 24f), MakeDefender(),
                distance: 13f);

            Assert.That(needed, Is.EqualTo(5), "13\" of a 24\" gun is over half range: AP(+1)");
        }

        [Test]
        public async Task Pierce_InsideHalfRange_NoExtraAp()
        {
            int needed = await SaveNeeded(WeaponWith(WeaponRules36Definitions.Pierce, range: 24f), MakeDefender(),
                distance: 11f);

            Assert.That(needed, Is.EqualTo(4));
        }

        [Test]
        public async Task Pierce_IgnoresTheTargetsOwnDefenseBonus()
        {
            DataBinding<UnitData> shielded = MakeDefender();
            shielded.GetValue().AttachRuleDefinition(new ResolvedRule("Shielded", CoreRuleCatalog.Shielded));

            Assert.That(await SaveNeeded(PlainWeapon(), shielded, distance: 11f), Is.EqualTo(3),
                "control: Shielded's +1 lowers the threshold to 3");

            RollToHitResults hits = await Hits(WeaponWith(WeaponRules36Definitions.Pierce, range: 24f), shielded, 11f);
            Assert.That(hits.SaveModifier, Is.EqualTo(0), "Pierce drops the +1 from the whole-attack save modifier");
            Assert.That(hits.IgnoresPositiveDefenseModifiers, Is.True);
            Assert.That(hits.SaveModifierTags, Has.None.Contains("Shielded"), "an ignored bonus earns no chip");
            Assert.That(hits.SaveModifierTags, Has.Some.Contains("Pierce"), "the Pierce chip says why");
            Assert.That(await SaveNeeded(WeaponWith(WeaponRules36Definitions.Pierce, range: 24f), shielded, 11f),
                Is.EqualTo(4));
        }

        [Test]
        public async Task Pierce_IgnoresCoverAndGrantedDefenseBuffs()
        {
            DataBinding<UnitData> buffed = MakeDefender();
            buffed.GetValue().Tokens.AddToken(new Token(RollModifierTokens.TypeFor(ERollKind.Save), 1,
                new TokenClearTrigger.RoundEnd(), Payload: new TokenPayload.StatModifier(1)));

            Assert.That(await SaveNeeded(PlainWeapon(), buffed, distance: 11f, cover: 1), Is.EqualTo(2),
                "control: cover +1 and a granted +1 make it 2+");
            Assert.That(await SaveNeeded(WeaponWith(WeaponRules36Definitions.Pierce, range: 24f), buffed, 11f, cover: 1),
                Is.EqualTo(4), "Pierce ignores both positive Defense modifiers");
        }

        [Test]
        public async Task Pierce_KeepsNegativeModifiers()
        {
            // "positive" only: a -1 to the target's Defense rolls still applies.
            DataBinding<UnitData> debuffed = MakeDefender();
            debuffed.GetValue().Tokens.AddToken(new Token(RollModifierTokens.TypeFor(ERollKind.Save), 1,
                new TokenClearTrigger.RoundEnd(), Payload: new TokenPayload.StatModifier(-1)));

            Assert.That(await SaveNeeded(WeaponWith(WeaponRules36Definitions.Pierce, range: 24f), debuffed, 13f),
                Is.EqualTo(6), "4 + AP(+1) over half range + the -1 debuff");
        }

        // --- Fracture -------------------------------------------------------------------------------------------

        [Test]
        public async Task Fracture_OnANaturalSix_HitsGetApPlusTwo()
        {
            RollToHitResults hits = await Hits(WeaponWith(WeaponRules36Definitions.Fracture), MakeDefender(), 10f);

            Assert.That(hits.SuccessfulHitList.Single().SaveModifier, Is.EqualTo(-2), "the natural-6 hit gets AP(+2)");
            Assert.That(await SaveNeeded(WeaponWith(WeaponRules36Definitions.Fracture), MakeDefender(), 10f),
                Is.EqualTo(6));
        }

        [Test]
        public async Task Fracture_FromAFatiguedStriker_AddsNothing()
        {
            // #418: a Fatigued unit's melee 6s "don't count as rolls of 6 for special rules".
            DataBinding<UnitData> attacker = MakeUnit("Striker", models: 1, z: 0f);
            FatigueUtilities.ApplyFatigued(attacker.GetValue());

            RollToHitResults hits = await Hits(WeaponWith(WeaponRules36Definitions.Fracture), MakeDefender(), 1f,
                attacker: attacker, isMelee: true);

            Assert.That(hits.SuccessfulHitList.All(g => g.SaveModifier == 0), Is.True, "no AP(+2) group under Fatigue");
        }

        [Test]
        public async Task Fracture_OffASix_AddsNothing()
        {
            _ctx = new WoundTestContext(_store, new CapturingWoundRequester(), new FixedDiceRoller(5));

            RollToHitResults hits = await Hits(WeaponWith(WeaponRules36Definitions.Fracture), MakeDefender(), 10f);

            Assert.That(hits.SuccessfulHitList.All(g => g.SaveModifier == 0), Is.True, "a 5 is a hit, not a 6");
        }

        // --- Puncture / Disintegrate / Destructive / Demolish / Purge --------------------------------------------

        [Test]
        public async Task Puncture_VersusDefense3_GetsApPlusTwo()
        {
            Assert.That(await SaveNeeded(WeaponWith(WeaponRules36Definitions.Puncture), MakeDefender(defense: 3), 10f),
                Is.EqualTo(5), "Defense 3+ is in the 2+..3+ band: 3 + AP(+2)");
        }

        [Test]
        public async Task Puncture_VersusDefense4_NoExtraAp()
        {
            Assert.That(await SaveNeeded(WeaponWith(WeaponRules36Definitions.Puncture), MakeDefender(defense: 4), 10f),
                Is.EqualTo(4));
        }

        [Test]
        public async Task Disintegrate_VersusToughThree_GetsApPlusTwo()
        {
            Assert.That(await SaveNeeded(WeaponWith(WeaponRules36Definitions.Disintegrate),
                MakeDefender(models: 3, tough: 3), 10f), Is.EqualTo(6));
        }

        [Test]
        public async Task Disintegrate_VersusToughOne_NoExtraAp()
        {
            Assert.That(await SaveNeeded(WeaponWith(WeaponRules36Definitions.Disintegrate),
                MakeDefender(models: 3, tough: 1), 10f), Is.EqualTo(4),
                "the 3.5 'Defense 2+' gate is gone - Disintegrate now keys on Tough(3)");
        }

        [Test]
        public async Task Destructive_AboveHalfStrength_GetsApPlusOne()
        {
            Assert.That(await SaveNeeded(WeaponWith(WeaponRules36Definitions.Destructive), MakeDefender(models: 4), 10f),
                Is.EqualTo(5));
        }

        [Test]
        public async Task Destructive_AtHalfStrength_NoExtraAp()
        {
            DataBinding<UnitData> defender = MakeDefender(models: 4);
            Kill(defender, 2);

            Assert.That(await SaveNeeded(WeaponWith(WeaponRules36Definitions.Destructive), defender, 10f),
                Is.EqualTo(4), "2 of 4 is half, not more than half");
        }

        [Test]
        public async Task Destructive_NoLongerRendsOnSixes()
        {
            // 3.5 Destructive was AP(+4) on natural 6s; 3.6 replaced it outright.
            DataBinding<UnitData> defender = MakeDefender(models: 4);
            Kill(defender, 2);

            RollToHitResults hits = await Hits(WeaponWith(WeaponRules36Definitions.Destructive), defender, 10f);

            Assert.That(hits.SuccessfulHitList.All(g => g.SaveModifier == 0), Is.True);
        }

        [Test]
        public async Task Demolish_VersusMostModelsNearTerrain_GetsApPlusOne()
        {
            DataBinding<UnitData> defender = MakeDefender(models: 3);
            Place(defender, 10f);
            // A piece just behind the target's models (centres at x 0 / 1.5 / 3, z 11): every base edge is
            // within 1" of it (the outer two at about 0.6").
            _store.Create(new TerrainData(ETerrainType.Cover, new CircularZone(new Float2(1.5f, 12.5f), 1f)));

            Assert.That(await SaveNeeded(WeaponWith(WeaponRules36Definitions.Demolish), defender, 10f, place: false),
                Is.EqualTo(5));
        }

        [Test]
        public async Task Demolish_VersusATargetInTheOpen_NoExtraAp()
        {
            DataBinding<UnitData> defender = MakeDefender(models: 3);
            Place(defender, 10f);
            _store.Create(new TerrainData(ETerrainType.Cover, new CircularZone(new Float2(30f, 30f), 1f)));

            Assert.That(await SaveNeeded(WeaponWith(WeaponRules36Definitions.Demolish), defender, 10f, place: false),
                Is.EqualTo(4));
        }

        [TestCase("Shaken")]
        [TestCase("Fatigued")]
        public async Task Purge_VersusAShakenOrFatiguedUnit_GetsApPlusTwo(string status)
        {
            DataBinding<UnitData> defender = MakeDefender();
            defender.GetValue().Tokens.AddToken(TokenDefinitionCatalog.Create(new TokenType(status)));

            Assert.That(await SaveNeeded(WeaponWith(WeaponRules36Definitions.Purge), defender, 10f), Is.EqualTo(6));
        }

        [Test]
        public async Task Purge_VersusAFreshUnit_NoExtraAp()
        {
            Assert.That(await SaveNeeded(WeaponWith(WeaponRules36Definitions.Purge), MakeDefender(), 10f), Is.EqualTo(4));
        }

        // --- Cover: Anti-Cover, Demolish, Pierce ----------------------------------------------------------------

        private static IEnumerable<TestCaseData> CoverIgnorers()
        {
            yield return new TestCaseData("Anti-Cover");
            yield return new TestCaseData("Demolish");
            yield return new TestCaseData("Pierce");
        }

        private static SpecialRuleDefinition CoverRule(string name) => name switch
        {
            "Anti-Cover" => WeaponRules36Definitions.AntiCover,
            "Demolish" => WeaponRules36Definitions.Demolish,
            _ => WeaponRules36Definitions.Pierce,
        };

        [TestCaseSource(nameof(CoverIgnorers))]
        public async Task TheCoverIgnorers_DropTheCoverBonus(string rule)
        {
            (DataBinding<UnitData> attacker, DataBinding<UnitData> defender) = BehindCover();

            Assert.That(await CoverBonus(attacker, defender, PlainWeapon()), Is.EqualTo(1), "control: the target is in cover");
            Assert.That(await CoverBonus(attacker, defender, WeaponWith(CoverRule(rule))), Is.EqualTo(0),
                $"{rule} ignores it");
            Assert.That(SightRuleQueries.CoverIgnoreSource(attacker.GetValue(), WeaponWith(CoverRule(rule)),
                _ctx.RuleEvaluator, attackerMoved: false), Is.EqualTo(rule), "and the targeting overlays name the rule");
        }

        // --- Imprecise ------------------------------------------------------------------------------------------

        [Test]
        public async Task Imprecise_IsMinusOneToHit()
        {
            Weapon sickles = new Weapon("Sickles", rangeInches: 0f, attacks: 2, armorPenetration: 0);
            sickles.AttachRuleDefinition(new ResolvedRule("Imprecise", WeaponRules36Definitions.Imprecise));

            Assert.That((await HitRoll(sickles)).HitRollNeeded, Is.EqualTo(5), "Quality 4 at -1 to hit");
            Assert.That((await HitRoll(new Weapon("Blade", 0f, 2, 0))).HitRollNeeded, Is.EqualTo(4), "control");
        }

        // --- helpers --------------------------------------------------------------------------------------------

        private async Task<int> SaveNeeded(Weapon weapon, DataBinding<UnitData> defender, float distance,
            int cover = 0, bool place = true)
        {
            RollToHitResults hits = await Hits(weapon, defender, distance, place: place);

            DataBinding<UnitData> attacker = MakeUnit("Shooter", models: 1, z: 0f);
            var stage = new DetermineSaveRollsNeededStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");
            var metadata = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1);
            metadata.AddResult(hits);
            metadata.AddResult(new CoverCheckResults(cover));
            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out DetermineSaveRollNeededResults result), Is.True);
            return result.PendingSaveRollsList.Max(p => p.SaveNeeded);
        }

        private async Task<RollToHitResults> Hits(Weapon weapon, DataBinding<UnitData> defender, float distance,
            DataBinding<UnitData>? attacker = null, bool isMelee = false, bool place = true)
        {
            attacker ??= MakeUnit("Shooter", models: 1, z: 0f);
            if (place) Place(defender, distance);

            var stage = new RollToHitStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");
            var metadata = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1,
                attackerMoved: false, isMelee: isMelee);
            metadata.AddResult(new DetermineHitRollResults(4, 1));
            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out RollToHitResults result), Is.True);
            return result;
        }

        private async Task<DetermineHitRollResults> HitRoll(Weapon weapon)
        {
            DataBinding<UnitData> attacker = MakeUnit("Striker", models: 1, z: 0f);
            DataBinding<UnitData> defender = MakeDefender();
            Place(defender, 0.5f);

            var stage = new DetermineHitRollStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");
            var metadata = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1,
                attackerMoved: false, isMelee: true);
            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out DetermineHitRollResults result), Is.True);
            return result;
        }

        // The CoverMajorityTests geometry: shooter at (0,5), a cover block across the line of fire, target at (20,5).
        private (DataBinding<UnitData> Attacker, DataBinding<UnitData> Defender) BehindCover()
        {
            _store.Create(new TerrainData(ETerrainType.Cover, new RectangularZone(8, 12, 3, 7)));
            DataBinding<UnitData> attacker = MakeUnitAt("Shooter", new Position(0, 5));
            DataBinding<UnitData> defender = MakeUnitAt("Target", new Position(20, 5));
            return (attacker, defender);
        }

        private async Task<int> CoverBonus(DataBinding<UnitData> attacker, DataBinding<UnitData> defender, Weapon weapon)
        {
            var stage = new CoverCheckStage(_ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");
            var metadata = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1);
            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out CoverCheckResults result), Is.True);
            return result.DefenseRollBonus;
        }

        private static Weapon PlainWeapon() => new Weapon("Plain gun", rangeInches: 24f, attacks: 1, armorPenetration: 0);

        private static Weapon WeaponWith(SpecialRuleDefinition rule, float range = 24f)
        {
            var weapon = new Weapon("Test gun", rangeInches: range, attacks: 1, armorPenetration: 0);
            weapon.AttachRuleDefinition(new ResolvedRule(rule.Name, rule));
            return weapon;
        }

        private DataBinding<UnitData> MakeDefender(int defense = 4, int models = 1, int tough = 1) =>
            MakeUnit("Target", models, z: 10f, defense, tough);

        private DataBinding<UnitData> MakeUnit(string name, int models, float z, int defense = 4, int tough = 1)
        {
            var bindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < models; i++)
            {
                var model = new ModelData(BaseRadius, new List<Weapon>(), new Position(i * 1.5f, z), _store);
                if (tough > 1) model.SetMaxWounds(tough);
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), name, quality: 4, defense: defense,
                modelBindings: bindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }

        private DataBinding<UnitData> MakeUnitAt(string name, Position position)
        {
            var model = new ModelData(BaseRadius, new List<Weapon>(), position, _store);
            var bindings = new List<DataBinding<ModelData>> { _store.GetDataBinding<ModelData>(_store.Create(model)) };
            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), name, quality: 4, defense: 4,
                modelBindings: bindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }

        private static void Place(DataBinding<UnitData> unit, float edgeDistance)
        {
            List<DataBinding<ModelData>> models = unit.GetValue().ModelBindings;
            for (int i = 0; i < models.Count; i++)
            {
                models[i].GetValue().SetPosition(new Position(i * 1.5f, edgeDistance + 2f * BaseRadius));
            }
        }

        private static void Kill(DataBinding<UnitData> unit, int count)
        {
            for (int i = 0; i < count; i++)
            {
                ModelData model = unit.GetValue().ModelBindings[i].GetValue();
                model.DealWounds(model.TotalWounds);
            }
        }
    }
}
