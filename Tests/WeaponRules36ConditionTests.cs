using System.Linq;
using System.Text.Json;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Serialization;
using FDG.Stages;
using FDG.Tests.RulesHarness;
using NUnit.Framework;

namespace FDG.Tests
{
    // #431 - the new condition/effect vocabulary on its own: each JSON kind round-trips, and the edges the
    // stage-level tests do not reach (a weaponless copy of a range gate, a joined hero in a Defense majority, a
    // wiped-out target, terrain absent).
    [TestFixture]
    public class WeaponRules36ConditionTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        // --- JSON kinds --------------------------------------------------------------------------------------

        private static IEnumerable<TestCaseData> NewConditions()
        {
            yield return new TestCaseData(new Condition.TargetOverHalfWeaponRange(), "targetOverHalfWeaponRange");
            yield return new TestCaseData(new Condition.TargetMostModelsDefenseInRange(5, 6), "targetMostModelsDefenseInRange");
            yield return new TestCaseData(new Condition.TargetAboveHalfStrength(), "targetAboveHalfStrength");
            yield return new TestCaseData(new Condition.TargetShakenOrFatigued(), "targetShakenOrFatigued");
            yield return new TestCaseData(new Condition.TargetMostModelsWithinInchesOfTerrain(1f),
                "targetMostModelsWithinInchesOfTerrain");
        }

        [TestCaseSource(nameof(NewConditions))]
        public void TheNewConditions_RoundTripUnderTheirKinds(Condition original, string kind)
        {
            string json = JsonSerializer.Serialize(original, RuleJson.Options);

            Assert.That(json, Does.Contain($"\"kind\": \"{kind}\""));
            Assert.That(JsonSerializer.Deserialize<Condition>(json, RuleJson.Options), Is.EqualTo(original));
        }

        private static IEnumerable<TestCaseData> NewEffects()
        {
            yield return new TestCaseData(new Effect.IgnorePositiveDefenseModifiers(), "ignorePositiveDefenseModifiers");
            yield return new TestCaseData(new Effect.CountsAsWoundsTaken(1, 2), "countsAsWoundsTaken");
        }

        [TestCaseSource(nameof(NewEffects))]
        public void TheNewEffects_RoundTripUnderTheirKinds(Effect original, string kind)
        {
            string json = JsonSerializer.Serialize(original, RuleJson.Options);

            Assert.That(json, Does.Contain($"\"kind\": \"{kind}\""));
            Assert.That(JsonSerializer.Deserialize<Effect>(json, RuleJson.Options), Is.EqualTo(original));
        }

        [Test]
        public void CountsAsWoundsTaken_DefaultsToScourgesNumbers()
        {
            Effect parsed = JsonSerializer.Deserialize<Effect>("{\"kind\": \"countsAsWoundsTaken\"}", RuleJson.Options)!;

            Assert.That(parsed, Is.EqualTo(new Effect.CountsAsWoundsTaken(OnRollValue: 1, WoundsPerDie: 2)));
        }

        // --- TargetOverHalfWeaponRange edges ------------------------------------------------------------------

        [Test]
        public void OverHalfRange_NeedsTheFiringWeapon()
        {
            UnitData shooter = MakeUnit(1);
            UnitData target = MakeUnit(1);
            var context = new SaveRollCompleteContext(shooter, target, TestDice.Faces(6), DistanceInches: 20f);
            var condition = new Condition.TargetOverHalfWeaponRange();

            Assert.That(condition.Evaluate(Invocation(context, shooter, new Weapon("Gun", 24f, 1, 0))), Is.True);
            Assert.That(condition.Evaluate(Invocation(context, shooter, weapon: null)), Is.False,
                "a unit-scoped (granted) copy has no weapon range to halve");
            Assert.That(condition.Evaluate(Invocation(context, shooter, new Weapon("Blade", 0f, 1, 0))), Is.False,
                "a melee weapon has no range");
        }

        [Test]
        public void OverHalfRange_IsStrict()
        {
            UnitData shooter = MakeUnit(1);
            UnitData target = MakeUnit(1);
            var condition = new Condition.TargetOverHalfWeaponRange();
            Weapon gun = new Weapon("Gun", 24f, 1, 0);

            Assert.That(condition.Evaluate(Invocation(
                new SaveRollCompleteContext(shooter, target, TestDice.Faces(6), DistanceInches: 12f), shooter, gun)),
                Is.False, "exactly half is not over half");
            Assert.That(condition.Evaluate(Invocation(
                new SaveRollCompleteContext(shooter, target, TestDice.Faces(6), DistanceInches: 12.1f), shooter, gun)),
                Is.True);
        }

        // --- TargetMostModelsDefenseInRange edges --------------------------------------------------------------

        [Test]
        public void DefenseMajority_CountsAJoinedHeroAsOneModelWithItsOwnDefense()
        {
            // 2 grunts at Defense 5 + a Defense 2 hero: 2 of 3 are in 5..6, so Fragment's gate holds; once a grunt
            // dies it is 1 of 2 - no longer most.
            (UnitData host, List<ModelData> grunts) = JoinedHeroUnit(gruntCount: 2, gruntDefense: 5, heroDefense: 2);
            var fragmentGate = new Condition.TargetMostModelsDefenseInRange(5, 6);
            var punctureGate = new Condition.TargetMostModelsDefenseInRange(2, 3);

            Assert.That(Evaluate(fragmentGate, host), Is.True);
            Assert.That(Evaluate(punctureGate, host), Is.False, "the hero alone is not most of the unit");

            grunts[0].DealWounds(grunts[0].TotalWounds);
            Assert.That(Evaluate(fragmentGate, host), Is.False, "1 of 2 living is not more than half");
        }

        [Test]
        public void DefenseMajority_TheBandsOrderDoesNotMatter()
        {
            UnitData target = MakeUnit(3, defense: 3);

            Assert.That(Evaluate(new Condition.TargetMostModelsDefenseInRange(3, 2), target), Is.True);
        }

        // --- TargetAboveHalfStrength / TargetShakenOrFatigued / terrain edges ------------------------------

        [Test]
        public void AboveHalfStrength_IsFalseForAWipedOutTarget()
        {
            UnitData target = MakeUnit(1);
            target.ModelBindings[0].GetValue().DealWounds(1);

            Assert.That(Evaluate(new Condition.TargetAboveHalfStrength(), target), Is.False);
        }

        [Test]
        public void ShakenOrFatigued_ReadsTheTargetNotTheBearer()
        {
            UnitData bearer = MakeUnit(1);
            UnitData target = MakeUnit(1);
            FatigueUtilities.ApplyFatigued(bearer);
            var context = new HitRollCompleteContext(bearer, target, TestDice.Faces(6));

            Assert.That(new Condition.TargetShakenOrFatigued().Evaluate(Invocation(context, bearer, null)), Is.False,
                "a Fatigued attacker does not make a fresh target Shaken or Fatigued");
        }

        [Test]
        public void TerrainMajority_WithNoTerrainInTheContext_StaysShut()
        {
            UnitData shooter = MakeUnit(1);
            UnitData target = MakeUnit(3);
            var context = new HitRollCompleteContext(shooter, target, TestDice.Faces(6), TerrainPieces: null);

            Assert.That(new Condition.TargetMostModelsWithinInchesOfTerrain(1f).Evaluate(
                Invocation(context, shooter, null)), Is.False);
        }

        // --- helpers --------------------------------------------------------------------------------------------

        private static RuleInvocation Invocation(IHookContext context, IUnit bearer, IWeapon? weapon) =>
            new(context, bearer, System.Array.Empty<RuleArgument>(), Weapon: weapon);

        private bool Evaluate(Condition condition, UnitData target)
        {
            UnitData shooter = MakeUnit(1);
            var context = new HitRollCompleteContext(shooter, target, TestDice.Faces(6), DistanceInches: 10f);
            return condition.Evaluate(Invocation(context, shooter, null));
        }

        private UnitData MakeUnit(int models, int defense = 4)
        {
            var bindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < models; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon>(), new Position(i, 0f), _store);
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            return new UnitData(new PlayerID(System.Guid.NewGuid()), "Unit", quality: 4, defense: defense,
                modelBindings: bindings);
        }

        // The HeroStatRulesTests join: a real HeroJoinResolver merge, so the HeroAttachment carries the hero's Defense.
        private (UnitData Host, List<ModelData> Grunts) JoinedHeroUnit(int gruntCount, int gruntDefense, int heroDefense)
        {
            UnitData host = MakeUnit(gruntCount, gruntDefense);
            UnitData hero = MakeUnit(1, heroDefense);
            hero.AttachRuleDefinition(new ResolvedRule("Hero", CoreRuleCatalog.Hero));

            HeroJoinResolver.Apply(new List<(FDG.SaveLoad.UnitFileEntry, UnitData)>
            {
                (new FDG.SaveLoad.UnitFileEntry { Id = "host" }, host),
                (new FDG.SaveLoad.UnitFileEntry { JoinsUnitId = "host" }, hero),
            });

            ModelID heroId = host.HeroAttachment!.HeroModelId;
            return (host, host.Models.Where(m => m.ID != heroId).Cast<ModelData>().ToList());
        }
    }
}
