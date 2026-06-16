using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.SaveLoad;
using Newtonsoft.Json;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #006 sub-slice A: proves a Hero unit merges into its declared
    // host at army setup via the real applicator (HeroJoinResolver.Apply, the same call FDGServer makes),
    // that the host gains the hero's model and a HeroAttachment carrying the hero's own Quality/Defense,
    // that the hero's standalone unit drops out of the survivor set, that the hero keeps its OWN Tough
    // (via the hero-aware UnitCreationRules pass), and that every eligibility gate rejects gracefully
    // (the rejected hero survives as its own unit instead).
    [TestFixture]
    public class HeroRuleIntegrationTests
    {
        private GameDataStore _store = null!;
        private RuleEvaluator _evaluator = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _evaluator = new RuleEvaluator(new FixedDiceRoller(4)); // creation rules don't roll
        }

        [Test]
        public void Hero_MergesIntoDeclaredHost()
        {
            UnitData host = MakeUnit(modelCount: 3, quality: 4, defense: 4);
            UnitData hero = MakeUnit(modelCount: 1, quality: 2, defense: 2);
            AttachHero(hero);

            ModelID heroModelId = hero.ModelBindings[0].GetValue().ID;

            IReadOnlyList<UnitData> survivors = HeroJoinResolver.Apply(Pairs(
                ("host", null, host),
                (null, "host", hero)));

            Assert.That(survivors, Is.EqualTo(new[] { host }), "the hero is absorbed; only the host deploys.");
            Assert.That(host.HasHero, Is.True);
            Assert.That(host.Models.Count, Is.EqualTo(4), "the hero's model joined the host's three.");
            Assert.That(host.GetHeroModel()!.ID, Is.EqualTo(heroModelId));
            Assert.That(host.HeroAttachment!.Quality, Is.EqualTo(2), "the hero keeps its own Quality.");
            Assert.That(host.HeroAttachment!.Defense, Is.EqualTo(2), "the hero keeps its own Defense.");
        }

        [Test]
        public void MergedHero_KeepsItsOwnTough_NotTheHosts()
        {
            UnitData host = MakeUnit(modelCount: 3, quality: 4, defense: 4);
            AttachTough(host, x: 2);
            UnitData hero = MakeUnit(modelCount: 1, quality: 2, defense: 2);
            AttachHero(hero);
            AttachTough(hero, x: 3);

            ModelID heroModelId = hero.ModelBindings[0].GetValue().ID;

            HeroJoinResolver.Apply(Pairs(("host", null, host), (null, "host", hero)));

            // The creation-rules loop runs per surviving unit, exactly as FDGServer does at army-load.
            UnitCreationRules.Apply(host, _evaluator);

            foreach (IModel model in host.Models)
            {
                float expected = model.ID == heroModelId ? 3f : 2f;
                Assert.That(model.TotalWounds, Is.EqualTo(expected),
                    "host rank-and-file take the host's Tough(2); the hero keeps its own Tough(3).");
            }
        }

        [Test]
        public void Hero_WithoutTough_KeepsOneWound_UnderToughHost()
        {
            UnitData host = MakeUnit(modelCount: 3, quality: 4, defense: 4);
            AttachTough(host, x: 4);
            UnitData hero = MakeUnit(modelCount: 1, quality: 2, defense: 2);
            AttachHero(hero); // no Tough on the hero

            ModelID heroModelId = hero.ModelBindings[0].GetValue().ID;

            HeroJoinResolver.Apply(Pairs(("host", null, host), (null, "host", hero)));
            UnitCreationRules.Apply(host, _evaluator);

            Assert.That(host.GetHeroModel()!.TotalWounds, Is.EqualTo(1f),
                "a Tough-less hero stays at one wound even though the host is Tough(4).");
        }

        [Test]
        public void HeroOverToughSix_DoesNotJoin()
        {
            UnitData host = MakeUnit(modelCount: 3, quality: 4, defense: 4);
            UnitData hero = MakeUnit(modelCount: 1, quality: 2, defense: 2);
            AttachHero(hero);
            AttachTough(hero, x: 7);

            IReadOnlyList<UnitData> survivors = HeroJoinResolver.Apply(Pairs(
                ("host", null, host), (null, "host", hero)));

            Assert.That(survivors, Does.Contain(hero), "a Tough(7) hero deploys on its own.");
            Assert.That(host.HasHero, Is.False);
        }

        [Test]
        public void Hero_CannotJoinSingleModelUnit()
        {
            UnitData host = MakeUnit(modelCount: 1, quality: 4, defense: 4);
            UnitData hero = MakeUnit(modelCount: 1, quality: 2, defense: 2);
            AttachHero(hero);

            IReadOnlyList<UnitData> survivors = HeroJoinResolver.Apply(Pairs(
                ("host", null, host), (null, "host", hero)));

            Assert.That(survivors, Does.Contain(hero));
            Assert.That(host.HasHero, Is.False, "the target must be a multi-model unit.");
        }

        [Test]
        public void SecondHero_CannotJoinUnitThatAlreadyHasOne()
        {
            UnitData host = MakeUnit(modelCount: 3, quality: 4, defense: 4);
            UnitData heroA = MakeUnit(modelCount: 1, quality: 2, defense: 2);
            UnitData heroB = MakeUnit(modelCount: 1, quality: 3, defense: 3);
            AttachHero(heroA);
            AttachHero(heroB);

            IReadOnlyList<UnitData> survivors = HeroJoinResolver.Apply(Pairs(
                ("host", null, host), (null, "host", heroA), (null, "host", heroB)));

            Assert.That(host.Models.Count, Is.EqualTo(4), "only the first hero joins.");
            Assert.That(survivors, Does.Contain(heroB), "the second hero deploys on its own.");
            Assert.That(survivors, Does.Not.Contain(heroA));
        }

        [Test]
        public void Hero_WithUnknownTarget_DeploysOnItsOwn()
        {
            UnitData host = MakeUnit(modelCount: 3, quality: 4, defense: 4);
            UnitData hero = MakeUnit(modelCount: 1, quality: 2, defense: 2);
            AttachHero(hero);

            IReadOnlyList<UnitData> survivors = HeroJoinResolver.Apply(Pairs(
                ("host", null, host), (null, "nope", hero)));

            Assert.That(survivors, Is.EquivalentTo(new[] { host, hero }), "unknown target → no merge.");
            Assert.That(host.HasHero, Is.False);
        }

        [Test]
        public void Hero_WithNoJoinTarget_DeploysOnItsOwn()
        {
            UnitData host = MakeUnit(modelCount: 3, quality: 4, defense: 4);
            UnitData hero = MakeUnit(modelCount: 1, quality: 2, defense: 2);
            AttachHero(hero); // Hero rule, but JoinsUnitId left null

            IReadOnlyList<UnitData> survivors = HeroJoinResolver.Apply(Pairs(
                ("host", null, host), (null, null, hero)));

            Assert.That(survivors, Is.EquivalentTo(new[] { host, hero }));
            Assert.That(host.HasHero, Is.False);
        }

        [Test]
        public void GetHeroModel_SurvivesRankAndFileDeath()
        {
            UnitData host = MakeUnit(modelCount: 3, quality: 4, defense: 4);
            UnitData hero = MakeUnit(modelCount: 1, quality: 2, defense: 2);
            AttachHero(hero);
            ModelID heroId = hero.ModelBindings[0].GetValue().ID;

            HeroJoinResolver.Apply(Pairs(("host", null, host), (null, "host", hero)));

            host.Models.First(model => model.ID != heroId).DealWounds(1f); // kill a grunt

            Assert.That(host.GetHeroModel()!.ID, Is.EqualTo(heroId),
                "dead models are retained, so the hero stays identifiable by id after a grunt falls.");
        }

        [Test]
        public void HeroAttachment_RoundTripsThroughJson()
        {
            var original = new HeroAttachment(new ModelID(System.Guid.NewGuid()), quality: 3, defense: 2, heroWounds: 4);

            string json = JsonConvert.SerializeObject(original);
            HeroAttachment restored = JsonConvert.DeserializeObject<HeroAttachment>(json)!;

            Assert.That(restored.HeroModelId, Is.EqualTo(original.HeroModelId));
            Assert.That(restored.Quality, Is.EqualTo(3));
            Assert.That(restored.Defense, Is.EqualTo(2));
            Assert.That(restored.HeroWounds, Is.EqualTo(4));
        }

        // --- helpers ---

        private static IReadOnlyList<(UnitFileEntry Entry, UnitData Unit)> Pairs(
            params (string? Id, string? JoinsUnitId, UnitData Unit)[] specs) =>
            specs.Select(spec => (
                new UnitFileEntry { Id = spec.Id, JoinsUnitId = spec.JoinsUnitId, Name = spec.Unit.Name },
                spec.Unit)).ToList();

        private static void AttachHero(UnitData unit) =>
            unit.AttachRuleDefinition(new ResolvedRule("Hero", CoreRuleCatalog.Hero));

        private static void AttachTough(UnitData unit, int x) =>
            unit.AttachRuleDefinition(
                new ResolvedRule("Tough", CoreRuleCatalog.Tough, new RuleArgument[] { new RuleArgument.Int(x) }));

        private UnitData MakeUnit(int modelCount, int quality, int defense)
        {
            var modelBindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(
                    baseRadiusInches: 0.75f,
                    weapons: new List<Weapon>(),
                    initialPosition: new Position(0, 0),
                    gameDataStore: _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            return new UnitData(new PlayerID(System.Guid.NewGuid()), "TestUnit",
                quality: quality, defense: defense, modelBindings: modelBindings);
        }
    }
}
