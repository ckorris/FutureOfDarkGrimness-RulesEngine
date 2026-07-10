using FDG.Ai.Tactician;
using FDG.Ai.Tactician.Resolvers;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Utilities;
using NUnit.Framework;
using static FDG.StageResolution.Requests.ChooseRangedAttackRequest;

namespace FDG.Tests
{
    // #191 A4-3 — value-weighted target choice: shooting prefers removing worth (not raw shooter
    // counts), melee prefers the best exchange, both on authored states.
    [TestFixture]
    public class TacticianTargetChoiceTests
    {
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
        public async Task Shooting_PrefersFinishingAFragileValuableTarget_OverPlinkingATank()
        {
            var attacker = MakeUnit(_us, 5, Rifle(), atX: 20f, atZ: 20f);
            // Fragile but valuable: 1 remaining wound, lots of guns. Tank: 9 wounds, D2.
            var fragile = MakeUnit(_them, 3, Rifle(ap: 2), atX: 30f, atZ: 20f);
            KillAllBut(fragile, survivors: 1);
            var tank = MakeUnit(_them, 1, Rifle(ap: 1), atX: 30f, atZ: 28f, defense: 2, woundsPerModel: 9);

            var resolver = new TacticianRangedAttackResolver(_evaluator,
                new FDG.Ai.Resolvers.AiChooseRangedAttackResolver());
            var request = BuildRequest(attacker, fragile, tank);

            CancellableResult<DataBinding<UnitData>> _; // (satisfies style; reply below)
            var reply = await resolver.Resolve(request);
            var choice = ((Selected<RangedAttackChoice>)reply).Value;

            Assert.That(choice.TargetUnit.Reference, Is.EqualTo(fragile.Reference),
                "killing the last gunner outvalues bouncing shots off a tank");
        }

        [Test]
        public async Task Melee_PrefersTheProfitableExchange()
        {
            var planner = new TacticianPlanner(_tableState, _evaluator);
            var brawlers = MakeUnit(_us, 5, Blade(attacks: 3), atX: 20f, atZ: 20f);
            planner.BeginActivation(brawlers);

            // Squishy shooters vs a counter-attacking wall of blades: same distance, one is lunch.
            var squishy = MakeUnit(_them, 3, Rifle(), atX: 24f, atZ: 20f);
            var wall = MakeUnit(_them, 5, Blade(attacks: 3), atX: 20f, atZ: 24f, defense: 3, woundsPerModel: 2);

            var resolver = new TacticianMeleeDefenderResolver(_tableState, _evaluator, planner);
            var request = new ChooseMeleeDefenderRequest(_us, "Choose defending unit",
                new List<CancellableSelectionRequest<UnitData>.ValidOption>
                {
                    new(wall, "Wall"),
                    new(squishy, "Squishy"),
                },
                new List<CancellableSelectionRequest<UnitData>.InvalidOption>());

            var reply = await resolver.Resolve(request);
            var chosen = ((Selected<DataBinding<UnitData>>)reply).Value;

            Assert.That(chosen.Reference, Is.EqualTo(squishy.Reference),
                "charge the shooters, not the counter-blade wall");
        }

        [Test]
        public async Task Shooting_PrefersBreakingAMob_OverAnEqualFreshTarget()
        {
            // A5-4 mob breaking: two targets with IDENTICAL remaining wounds and living guns - the
            // only difference is that the weakened mob is one volley from HALF strength (where the
            // engine's morale test routs it). Without the break bonus the values tie and list
            // order decides; the bonus must make the breakable mob win outright.
            var attacker = MakeUnit(_us, 5, Rifle(), atX: 20f, atZ: 20f);
            var fresh = MakeUnit(_them, 6, Blade(), atX: 30f, atZ: 20f);
            var mob = MakeUnit(_them, 10, Blade(), atX: 30f, atZ: 28f);
            KillAllBut(mob, survivors: 6); // 6/10 living: ~1 more kill crosses half strength

            var resolver = new TacticianRangedAttackResolver(_evaluator,
                new FDG.Ai.Resolvers.AiChooseRangedAttackResolver());
            var reply = await resolver.Resolve(BuildRequest(attacker, fresh, mob));
            var choice = ((Selected<RangedAttackChoice>)reply).Value;

            Assert.That(choice.TargetUnit.Reference, Is.EqualTo(mob.Reference),
                "breaking the mob (half-strength morale) outvalues an equal volley into a fresh unit");
        }

        [Test]
        public async Task Shooting_PrefersTheChargerThatCanReachUs_OverAnEqualDistantTwin()
        {
            // A5-6: two identical melee units; one can charge us next activation, one cannot.
            // Kill the thing about to eat you. (The distant twin is listed first so a tie would
            // pick it - the threat factor must break the tie the right way.)
            var attacker = MakeUnit(_us, 5, Rifle(), atX: 20f, atZ: 20f);
            var far = MakeUnit(_them, 5, Blade(), atX: 20f, atZ: 42f);  // 22" - out of charge reach
            var near = MakeUnit(_them, 5, Blade(), atX: 20f, atZ: 30f); // 10" - charges next turn

            var resolver = new TacticianRangedAttackResolver(_evaluator,
                new FDG.Ai.Resolvers.AiChooseRangedAttackResolver(), _tableState);
            var reply = await resolver.Resolve(BuildRequest(attacker, far, near));
            var choice = ((Selected<RangedAttackChoice>)reply).Value;

            Assert.That(choice.TargetUnit.Reference, Is.EqualTo(near.Reference),
                "the unit that can charge us next activation dies first");
        }

        [Test]
        public async Task Shooting_PrefersTheLoadedTransport_OverAnEmptyTwin()
        {
            // A5-6: a loaded transport is worth boat + payload (killing it spills the cargo out
            // Shaken). Empty twin first, so a tie would pick it.
            var attacker = MakeUnit(_us, 5, Rifle(), atX: 20f, atZ: 20f);
            var empty = MakeUnit(_them, 1, Rifle(), atX: 30f, atZ: 20f);
            var loaded = MakeUnit(_them, 1, Rifle(), atX: 30f, atZ: 26f);
            var cargo = MakeUnit(_them, 4, Blade(), atX: 30f, atZ: 26f);
            foreach (var boat in new[] { empty, loaded })
                boat.GetValue().AttachRuleDefinition(new ResolvedRule(TransportUtilities.TransportRuleName,
                    CoreRuleCatalog.Transport, new Rules.Foundation.RuleArgument[]
                    { new Rules.Foundation.RuleArgument.Int(6) }));
            TransportUtilities.Embark(cargo.GetValue(), loaded.GetValue());

            var resolver = new TacticianRangedAttackResolver(_evaluator,
                new FDG.Ai.Resolvers.AiChooseRangedAttackResolver(), _tableState);
            var reply = await resolver.Resolve(BuildRequest(attacker, empty, loaded));
            var choice = ((Selected<RangedAttackChoice>)reply).Value;

            Assert.That(choice.TargetUnit.Reference, Is.EqualTo(loaded.Reference),
                "the boat with a payload inside is the more valuable kill");
        }

        [Test]
        public async Task ModelPick_SnipesTheOutputModel_NotModelOne()
        {
            // A5-6: Takedown/single-model spells took "Model 1"; the pick must go to the model
            // whose removal hurts - the heavy-weapon carrier.
            var grunt1 = MakeModel(_store, new Position(30, 20, 0), new Weapon("Rifle", 24f, 1, 0));
            var grunt2 = MakeModel(_store, new Position(31, 20, 0), new Weapon("Rifle", 24f, 1, 0));
            var heavy = MakeModel(_store, new Position(32, 20, 0), new Weapon("Melter", 12f, 3, 4));

            var resolver = new TacticianModelSelectionResolver(new FDG.Ai.Resolvers.AiSelectionResolver<ModelData>());
            var request = new SelectionRequest<ModelData>(_us, "Takedown: choose the target model",
                new List<SelectionRequest<ModelData>.ValidOption>
                {
                    new(grunt1, "Model 1"), new(grunt2, "Model 2"), new(heavy, "Model 3"),
                },
                Array.Empty<SelectionRequest<ModelData>.InvalidOption>(), allowCancel: false);

            var chosen = await resolver.Resolve(request);
            Assert.That(chosen.Reference, Is.EqualTo(heavy.Reference),
                "snipe the melter carrier, not whoever stands first in line");
        }

        // --- fixtures ---------------------------------------------------------------------------

        private ChooseRangedAttackRequest BuildRequest(DataBinding<UnitData> attacker,
            params DataBinding<UnitData>[] targets)
        {
            Weapon weapon = attacker.GetValue().GetRangedWeapons()[0];
            var shooters = attacker.GetValue().ModelBindings.ToHashSet();
            var stats = targets.Select(t => new WeaponTargetStats(t,
                shooters, new HashSet<DataBinding<ModelData>>(), false, null)).ToList();
            return new ChooseRangedAttackRequest(_us, "Choose Ranged Weapon", attacker,
                new List<WeaponOption> { new(weapon, stats, false, false, null, null) });
        }

        private static void KillAllBut(DataBinding<UnitData> unit, int survivors)
        {
            var models = unit.GetValue().ModelBindings;
            for (int i = 0; i < models.Count - survivors; i++)
                models[i].GetValue().DealWounds(models[i].GetValue().TotalWounds);
        }

        private static Weapon Rifle(int ap = 0) => new Weapon("Rifle", 24f, 1, ap);
        private static Weapon Blade(int attacks = 2) => new Weapon("Blade", 0f, attacks, 0);

        private static DataBinding<ModelData> MakeModel(GameDataStore store, Position position, Weapon weapon)
        {
            var model = new ModelData(0.5f, new List<Weapon> { weapon }, position, store);
            return store.GetDataBinding<ModelData>(store.Create(model));
        }

        private DataBinding<UnitData> MakeUnit(PlayerID owner, int modelCount, Weapon weapon,
            float atX, float atZ, int quality = 4, int defense = 4, int woundsPerModel = 1)
        {
            var modelBindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon> { weapon },
                    new Position(atX + (i % 2) * 1.1f, atZ + (i / 2) * 1.1f), _store);
                if (woundsPerModel > 1) model.SetMaxWounds(woundsPerModel);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(owner, $"U{atX},{atZ}", quality, defense, modelBindings);
            var binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(owner, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }
    }
}
