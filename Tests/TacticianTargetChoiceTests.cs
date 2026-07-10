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
