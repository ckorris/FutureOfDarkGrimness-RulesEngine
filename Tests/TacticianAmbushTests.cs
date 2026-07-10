using System;
using FDG.Ai.Tactician;
using FDG.Ai.Tactician.Resolvers;
using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #191 A5-2, revised A5-8 (Chris): EVERYTHING with Ambush holds, always - round 1 is
    // positioning, and Ambush is free movement (the old melee-only + half-army-cap heuristic
    // left long-range Ambushers walking on at round 1). An arriving unit drops onto the most
    // winnable objective. Scout placement reuses the deployment aim.
    [TestFixture]
    public class TacticianAmbushTests
    {
        private GameDataStore _store = null!;
        private TableState _tableState = null!;
        private PlayerID _us;
        private PlayerID _them;
        private Dictionary<PlayerID, List<DataBinding<UnitData>>> _units = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _tableState = new TableState(_store);
            _us = new PlayerID(Guid.NewGuid());
            _them = new PlayerID(Guid.NewGuid());
            _units = new Dictionary<PlayerID, List<DataBinding<UnitData>>>();
        }

        [Test]
        public void EverythingWithAmbush_Holds_MeleeAndShooterAlike()
        {
            MakeUnit(_us, "Brawlers", 4, Blade());
            MakeUnit(_us, "Riflemen", 4, Rifle());
            BuildArmies();

            Assert.That(AmbushPolicy.ShouldHold(_tableState, _us, "Brawlers"), Is.True);
            Assert.That(AmbushPolicy.ShouldHold(_tableState, _us, "Riflemen"), Is.True,
                "A5-8 (Chris): Ambush is free positioning - a shooter teleporting to its own "
                + "range band beats a round of marching too");
        }

        [Test]
        public void TheWholeArmyMayHold()
        {
            var first = MakeUnit(_us, "Brawlers A", 4, Blade());
            MakeUnit(_us, "Brawlers B", 4, Blade());
            BuildArmies();

            Assert.That(AmbushPolicy.ShouldHold(_tableState, _us, "Brawlers A"), Is.True);
            ReserveRules.PlaceInReserve(first.GetValue()); // the engine holds it on that answer

            Assert.That(AmbushPolicy.ShouldHold(_tableState, _us, "Brawlers B"), Is.True,
                "A5-8: no half-army cap - if the whole army can ambush, it does");
        }

        [Test]
        public async Task HoldPrompt_IsAnsweredHold_ForEveryProfile()
        {
            MakeUnit(_us, "Brawlers", 4, Blade());
            MakeUnit(_us, "Riflemen", 4, Rifle());
            BuildArmies();
            var resolver = new TacticianActionResolver(
                new TacticianPlanner(_tableState, new RuleEvaluator(new ProbabilisticDiceRoller())),
                _tableState, new FDG.Ai.Resolvers.AiStringSelectionResolver(_tableState, _us));

            string melee = await resolver.Resolve(HoldPrompt(_us, "Brawlers"));
            string shooter = await resolver.Resolve(HoldPrompt(_us, "Riflemen"));

            Assert.That(melee, Is.EqualTo("Hold in Ambush"));
            Assert.That(shooter, Is.EqualTo("Hold in Ambush"));
        }

        [Test]
        public async Task AmbushArrival_DropsOnTheMostWinnableObjective()
        {
            // Objective A is crawling with enemies; objective B is free. The arrival aims at B.
            _store.Create(new ObjectiveData(new Position(12f, 24f), _store));
            _store.Create(new ObjectiveData(new Position(40f, 24f), _store));
            MakeUnit(_them, "Campers", 5, Rifle(), atX: 12f, atZ: 24f);
            var arrivers = MakeUnit(_us, "Strikers", 4, Blade(), atX: 0f, atZ: 0f);
            BuildArmies();
            var resolver = new TacticianPlaceObjectsResolver<ModelData>(_tableState);

            var reply = await resolver.Resolve(new PlaceObjectsRequest<ModelData>(
                _us, TacticianPlaceObjectsResolver<ModelData>.AmbushTaskName,
                new RectangularZone(0f, 48f, 0f, 48f), arrivers.GetValue().ModelBindings,
                minDistanceFromEnemiesInches: 9f));

            var placed = ((Selected<List<PlacedObjectEntry<ModelData>>>)reply).Value;
            float cx = placed.Average(p => p.Position.x);
            float cz = placed.Average(p => p.Position.z);
            float distToFree = MathF.Sqrt((cx - 40f) * (cx - 40f) + (cz - 24f) * (cz - 24f));
            Assert.That(distToFree, Is.LessThanOrEqualTo(4f),
                "the arrival lands on the uncontested objective, not the camped one");
        }

        // --- fixtures ---

        private static StringSelectionRequest HoldPrompt(PlayerID player, string unitName) =>
            new StringSelectionRequest(player,
                $"Deploy {unitName} now, or hold it in Ambush?",
                new List<string>
                {
                    "Hold in Ambush",
                    ChooseUnitToDeployStage.DEPLOY_NORMALLY_CHOICE,
                    ChooseUnitToDeployStage.BACK_TO_LIST_CHOICE,
                },
                new List<StringSelectionRequest.InvalidOption>());

        private static Weapon Rifle() => new Weapon("Rifle", 24f, 1, 0);
        private static Weapon Blade() => new Weapon("Blade", 0f, 2, 0);

        private DataBinding<UnitData> MakeUnit(PlayerID owner, string name, int modelCount,
            Weapon weapon, float atX = 5f, float atZ = 5f)
        {
            var modelBindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon> { weapon },
                    new Position(atX + (i % 2) * 1.1f, atZ + (i / 2) * 1.1f), _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(owner, name, quality: 4, defense: 4, modelBindings: modelBindings);
            var binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            if (!_units.TryGetValue(owner, out List<DataBinding<UnitData>>? list))
                _units[owner] = list = new List<DataBinding<UnitData>>();
            list.Add(binding);
            return binding;
        }

        private void BuildArmies()
        {
            foreach ((PlayerID owner, List<DataBinding<UnitData>> list) in _units)
                _store.Create(new ArmyData(owner, list));
        }
    }
}
