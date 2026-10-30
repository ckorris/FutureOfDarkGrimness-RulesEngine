using FDG.Ai.Resolvers;
using FDG.Ai.Tactician;
using FDG.Ai.Tactician.Resolvers;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #442 goal item 5 (#434 Versatile Expert / Warrior): the Tactician answers the activation-start "pick one effect"
    // by pricing each pick's helper rule against the best target it can attack this activation. AP(+1) is worth more
    // into a good save, +1 to hit is worth more for a poor shooter into a poor save - the options are ordered so that
    // solo's first-option answer would FAIL each pick test. Plus the #066 legality pin: always an index in range,
    // never Cancel.
    [TestFixture]
    public class TacticianAbilityEffectResolverTests
    {
        private const string RuleName = "Versatile Expert";
        private const string PiercingLabel = "AP(+1)";
        private const string PrecisionLabel = "+1 to hit";

        private GameDataStore _store = null!;
        private TableState _tableState = null!;
        private RuleEvaluator _evaluator = null!;
        private PlayerID _us;
        private PlayerID _them;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _tableState = new TableState(_store);
            _evaluator = new RuleEvaluator(new ProbabilisticDiceRoller());
            _us = new PlayerID(Guid.NewGuid());
            _them = new PlayerID(Guid.NewGuid());
        }

        [Test]
        public async Task GoodSaveTarget_PicksAPPlusOne()
        {
            // Quality 3 hits on 4+ (+1 to hit: x1.25); Defense 2 fails 1 in 6 (AP(+1): x2).
            var (resolver, unit) = Scene(quality: 3, enemyDefense: 2);
            var request = Request(unit, PrecisionLabel, PiercingLabel);

            int choice = await resolver.Resolve(request);

            Assert.That(request.Options[choice].Label, Is.EqualTo(PiercingLabel));
        }

        [Test]
        public async Task PoorShooterIntoAPoorSave_PicksPlusOneToHit()
        {
            // Quality 5 hits on 5+ (+1 to hit: x1.5); Defense 6 fails 5 in 6 (AP(+1): x1.2).
            var (resolver, unit) = Scene(quality: 5, enemyDefense: 6);
            var request = Request(unit, PiercingLabel, PrecisionLabel);

            int choice = await resolver.Resolve(request);

            Assert.That(request.Options[choice].Label, Is.EqualTo(PrecisionLabel));
        }

        [Test]
        public async Task NoEnemyInReach_KeepsTheFirstOption()
        {
            var (resolver, unit) = Scene(quality: 3, enemyDefense: 2, enemyX: 100f);
            var request = Request(unit, PrecisionLabel, PiercingLabel);

            Assert.That(await resolver.Resolve(request), Is.EqualTo(0), "nothing to price: the authored default");
        }

        [Test]
        public async Task BearerIsNotTheActiveUnit_FallsBackToSolo()
        {
            var (resolver, unit) = Scene(quality: 3, enemyDefense: 2);
            var request = new ChooseAbilityEffectRequest(_us, "pick", RuleName, "Someone Else",
                new[] { Option(PrecisionLabel), Option(PiercingLabel) });

            Assert.That(await resolver.Resolve(request), Is.EqualTo(0));
        }

        [TestCase(2, 2)]
        [TestCase(5, 6)]
        [TestCase(4, 4)]
        public async Task Legality_AlwaysAnIndexInRange_NeverCancel(int quality, int enemyDefense)
        {
            var (resolver, unit) = Scene(quality, enemyDefense);
            var request = new ChooseAbilityEffectRequest(_us, "pick", RuleName, unit.GetValue().Name,
                new[] { Option(PiercingLabel), Option(PrecisionLabel) }, allowCancel: true);

            int choice = await resolver.Resolve(request);

            Assert.That(choice, Is.InRange(0, request.Options.Count - 1));
            Assert.That(choice, Is.Not.EqualTo(ChooseAbilityEffectRequest.CANCEL));
        }

        // --- scene ------------------------------------------------------------------------------------------------

        private (TacticianAbilityEffectResolver, DataBinding<UnitData>) Scene(int quality, int enemyDefense,
            float enemyX = 12f)
        {
            SpecialRuleDefinition piercing = Helper($"{RuleName} (Piercing)",
                new HookEntry(EHookID.Shooting_OnHitRollComplete, new Condition.Always(),
                    new Effect.RollModifier(ERollKind.Save, -1), ELifetime.ThisAttack));
            SpecialRuleDefinition precision = Helper($"{RuleName} (Precision)",
                new HookEntry(EHookID.Shooting_OnHitRollModifier, new Condition.Always(),
                    new Effect.RollModifier(ERollKind.Hit, +1), ELifetime.ThisAttack));
            var pick = new SpecialRuleDefinition(RuleName, Array.Empty<HookEntry>(),
                new[] { SelfPick(piercing.Name, PiercingLabel), SelfPick(precision.Name, PrecisionLabel) },
                ERuleScope.Unit);

            DataBinding<UnitData> unit = MakeUnit(_us, "Veterans", quality, defense: 4, x: 0f);
            unit.GetValue().AttachRuleDefinition(new ResolvedRule(pick.Name, pick));
            var ourArmy = new ArmyData(_us, new List<DataBinding<UnitData>> { unit });
            ourArmy.PersistRuleData(new[] { pick, piercing, precision }, Array.Empty<SpellDefinition>());
            _store.Create(ourArmy);

            DataBinding<UnitData> enemy = MakeUnit(_them, "Targets", quality: 4, enemyDefense, x: enemyX);
            _store.Create(new ArmyData(_them, new List<DataBinding<UnitData>> { enemy }));

            var planner = new TacticianPlanner(_tableState, _evaluator);
            planner.BeginActivation(unit);
            return (new TacticianAbilityEffectResolver(planner, _tableState, _evaluator,
                new AiChooseAbilityEffectResolver()), unit);
        }

        private ChooseAbilityEffectRequest Request(DataBinding<UnitData> unit, string first, string second) =>
            new(_us, "pick", RuleName, unit.GetValue().Name, new[] { Option(first), Option(second) });

        private static ChooseAbilityEffectRequest.EffectOption Option(string label) => new(label, label);

        private static SpecialRuleDefinition Helper(string name, HookEntry entry) =>
            new(name, new[] { entry }, Array.Empty<ActivatedAbility>(), ERuleScope.Unit);

        private static ActivatedAbility SelfPick(string granted, string label) =>
            new(EHookID.Activation_OnActivationStart, new Cost.OncePerActivation(),
                new TargetSelector(RangeInches: 0f, MinCount: 1, MaxCount: 1, ETargetAffinity.Self,
                    RequireLineOfSight: false),
                new Effect.AddRule(granted, ELifetime.ThisActivation), new Condition.Always(), Label: label);

        private DataBinding<UnitData> MakeUnit(PlayerID owner, string name, int quality, int defense, float x)
        {
            var models = new List<DataBinding<ModelData>>();
            for (int i = 0; i < 5; i++)
            {
                var rifle = new Weapon("Rifle", rangeInches: 24f, attacks: 2, armorPenetration: 0);
                var model = new ModelData(0.5f, new List<Weapon> { rifle }, new Position(x, 10f + i * 1.2f), _store);
                models.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            return _store.GetDataBinding<UnitData>(_store.Create(
                new UnitData(owner, name, quality: quality, defense: defense, modelBindings: models)));
        }
    }
}
