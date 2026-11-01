using FDG.Data;
using FDG.Presentation;
using FDG.Presentation.Beats;
using FDG.Rules.Dispatch;
using FDG.Stages;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #420 - Core Rules v3.6.0: "Units with models moving across Dangerous terrain, or that activate in it,
    // must roll as many dice as the number of wounds it would take to fully destroy them, and for each roll
    // of 1 the unit takes one wound." (Transport v3.6.0: destroyed-transport passengers "must take a
    // Dangerous terrain test".) 3.5 rolled one die per testing MODEL and wounded that model; these pin the
    // 3.6 shape: the dice are the UNIT's remaining wounds (Tough-aware), any one crossing model puts the
    // whole unit to the test, and a 1 is a wound to the unit, assigned the normal way.
    //
    // FixedOnesDiceRoller(n) = exactly n of the rolled dice are 1s; FixedFaceDiceRoller(f) = every die is f.
    // (FixedDiceRoller reports ONE die however many were rolled, which would hide the pool size.)
    [TestFixture]
    public class DangerousTerrainDiceRuleIntegrationTests
    {
        private static readonly RectangularZone DangerZone = new RectangularZone(3, 5, -2, 2);

        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        // ---- The shared helper: wounds it would take to fully destroy the unit ---------------------------

        [Test]
        public void WoundsToDestroy_FiveToughOneModels_IsFive()
        {
            Assert.That(MakeUnit(modelCount: 5).GetValue().GetWoundsToDestroy(), Is.EqualTo(5));
        }

        [Test]
        public void WoundsToDestroy_ToughSixHeroWithTwoWoundsTaken_IsFour()
        {
            DataBinding<UnitData> hero = MakeUnit(modelCount: 1, tough: 6);
            hero.GetValue().Models[0].DealWounds(2f);

            Assert.That(hero.GetValue().GetWoundsToDestroy(), Is.EqualTo(4));
        }

        [Test]
        public void WoundsToDestroy_ThreeToughThreeModels_IsNine()
        {
            Assert.That(MakeUnit(modelCount: 3, tough: 3).GetValue().GetWoundsToDestroy(), Is.EqualTo(9));
        }

        [Test]
        public void WoundsToDestroy_DeadModelsContributeNothing_EvenWithOverkillRecorded()
        {
            DataBinding<UnitData> unit = MakeUnit(modelCount: 3, tough: 2);
            unit.GetValue().Models[0].DealWounds(5f); // dead, with 3 overkill on it
            unit.GetValue().Models[1].DealWounds(1f); // one wound left

            Assert.That(unit.GetValue().GetWoundsToDestroy(), Is.EqualTo(3),
                "1 (wounded) + 2 (whole) - the corpse's negative balance must not eat into the live models.");
        }

        [Test]
        public void WoundsToDestroy_CountsAJoinedHeroesWounds()
        {
            DataBinding<UnitData> unit = MakeHeroJoinedUnit(gruntCount: 4, heroTough: 6);

            Assert.That(unit.GetValue().GetWoundsToDestroy(), Is.EqualTo(10),
                "four 1-wound grunts + a Tough(6) hero: the hero's model is part of the unit.");
        }

        [Test]
        public void WoundsToDestroy_FloorsAFractionalTail()
        {
            DataBinding<UnitData> unit = MakeUnit(modelCount: 3, tough: 1);
            unit.GetValue().Models[0].DealWounds(0.5f); // 2.5 wounds left under the probabilistic roller

            Assert.That(unit.GetValue().GetWoundsToDestroy(), Is.EqualTo(2),
                "a fractional tail cannot buy a whole die (the No Retreat / Reanimation precedent).");
        }

        // ---- Movement: any one crossing model puts the whole unit to the test ---------------------------

        [Test]
        public void MovementTest_FiveToughOneUnit_RollsFiveDice_EvenWhenOnlyOneModelCrosses()
        {
            DataBinding<UnitData> unit = MakeUnit(modelCount: 5);
            MovementExecutor.DangerousTerrainResult result = RollMove(unit, crossing: 1,
                new FixedFaceDiceRoller(4));

            Assert.That(result.DiceCount, Is.EqualTo(5), "one die per wound to destroy, not one per crossing model");
            Assert.That(result.Roll!.TotalRolls, Is.EqualTo(5f).Within(0.001f));
        }

        [Test]
        public void MovementTest_ToughSixHeroWithTwoWoundsTaken_RollsFourDice()
        {
            DataBinding<UnitData> hero = MakeUnit(modelCount: 1, tough: 6);
            hero.GetValue().Models[0].DealWounds(2f);

            Assert.That(RollMove(hero, crossing: 1, new FixedFaceDiceRoller(4)).DiceCount, Is.EqualTo(4));
        }

        [Test]
        public void MovementTest_ThreeToughThreeModels_RollsNineDice()
        {
            DataBinding<UnitData> unit = MakeUnit(modelCount: 3, tough: 3);

            Assert.That(RollMove(unit, crossing: 3, new FixedFaceDiceRoller(4)).DiceCount, Is.EqualTo(9));
        }

        [Test]
        public void MovementTest_NoModelCrosses_NoTest()
        {
            DataBinding<UnitData> unit = MakeUnit(modelCount: 5);

            Assert.That(RollMove(unit, crossing: 0, new FixedFaceDiceRoller(1)).AnyTested, Is.False,
                "the dangerous zone is on the table but nobody walks through it");
        }

        [Test]
        public void MovementTest_CountsAsDangerous_PutsTheWholeUnitToItsFullPool_WithNoDangerousTerrain()
        {
            DataBinding<UnitData> unit = MakeUnit(modelCount: 3, tough: 3);
            var paths = unit.GetValue().ModelBindings
                .Select(m => new ModelMoveEntry(m, new List<Position> { new Position(8f, 0f) })).ToList();
            var ctx = new TestGameContext(_store, new FixedFaceDiceRoller(4));

            MovementExecutor.DangerousTerrainResult result = MovementExecutor.RollDangerousTerrain(
                ctx, paths, new List<ITerrain>(), unit.GetValue(), countsAsInDangerousTerrain: true);

            Assert.That(result.DiceCount, Is.EqualTo(9), "the forced 'counts as Dangerous' test is the same test");
        }

        [Test]
        public void MovementTest_CountsAsDangerous_ButNobodyMoves_NoTest()
        {
            DataBinding<UnitData> unit = MakeUnit(modelCount: 3);
            var paths = unit.GetValue().ModelBindings
                .Select(m => new ModelMoveEntry(m, new List<Position>())).ToList();
            var ctx = new TestGameContext(_store, new FixedFaceDiceRoller(1));

            MovementExecutor.DangerousTerrainResult result = MovementExecutor.RollDangerousTerrain(
                ctx, paths, new List<ITerrain>(), unit.GetValue(), countsAsInDangerousTerrain: true);

            Assert.That(result.AnyTested, Is.False, "the grant rides a MOVE; a unit that stays put has no test");
        }

        // ---- A 1 is a wound to the UNIT, assigned the normal way ----------------------------------------

        [Test]
        public async Task EachOne_IsAWoundToTheUnit_FillingTheFirstModelBeforeTheNext()
        {
            // 3 x Tough(3): nine dice, four 1s. 3.5 would have given a wound to each of (at most) three
            // "testing models"; the unit now takes four wounds and the normal order fills model 1 first.
            DataBinding<UnitData> unit = MakeUnit(modelCount: 3, tough: 3);

            await RollAndLand(unit, crossing: 3, new FixedOnesDiceRoller(4));

            IReadOnlyList<IModel> models = unit.GetValue().Models;
            Assert.That(models[0].GetIsDead(), Is.True, "the first model takes its three wounds");
            Assert.That(models[1].WoundsDealt, Is.EqualTo(1f), "...and the fourth spills onto the next");
            Assert.That(models[2].WoundsDealt, Is.EqualTo(0f));
            Assert.That(unit.GetValue().RemainingWounds, Is.EqualTo(5f));
        }

        [Test]
        public async Task AnAlreadyWoundedModel_IsFinishedFirst()
        {
            // The mandatory order the normal assignment enforces (#023): a wound goes to the model that is
            // already hurt before a fresh one is started - even though it sits later in the unit.
            DataBinding<UnitData> unit = MakeUnit(modelCount: 3, tough: 3);
            unit.GetValue().Models[1].DealWounds(1f); // 8 wounds to destroy -> 8 dice

            MovementExecutor.DangerousTerrainResult rolled = RollMove(unit, crossing: 3,
                new FixedOnesDiceRoller(1));
            Assert.That(rolled.DiceCount, Is.EqualTo(8));
            await MovementExecutor.ResolveDangerousTerrain(NewContext(new FixedOnesDiceRoller(1)), rolled);

            Assert.That(unit.GetValue().Models[1].WoundsDealt, Is.EqualTo(2f), "the wounded model takes it");
            Assert.That(unit.GetValue().Models[0].WoundsDealt, Is.EqualTo(0f));
        }

        [Test]
        public async Task AJoinedHero_IsWoundedLast()
        {
            // Two 1-wound grunts and a Tough(3) hero: five dice. Two 1s kill the grunts and leave the hero
            // alone; a third reaches the hero.
            DataBinding<UnitData> twoOnes = MakeHeroJoinedUnit(gruntCount: 2, heroTough: 3);
            await RollAndLand(twoOnes, crossing: 3, new FixedOnesDiceRoller(2));
            Assert.That(twoOnes.GetValue().Models[2].WoundsDealt, Is.EqualTo(0f),
                "the hero is wounded last, so two wounds are absorbed by the rank and file");
            Assert.That(twoOnes.GetValue().Models[0].GetIsDead() && twoOnes.GetValue().Models[1].GetIsDead(), Is.True);

            DataBinding<UnitData> threeOnes = MakeHeroJoinedUnit(gruntCount: 2, heroTough: 3);
            await RollAndLand(threeOnes, crossing: 3, new FixedOnesDiceRoller(3));
            Assert.That(threeOnes.GetValue().Models[2].WoundsDealt, Is.EqualTo(1f),
                "once the rank and file are gone the next wound reaches the hero");
        }

        [Test]
        public async Task AllOnes_DestroyTheWholeUnit_ThroughTheDestructionSeam()
        {
            DataBinding<UnitData> unit = MakeUnit(modelCount: 3, tough: 3);

            await RollAndLand(unit, crossing: 1, new FixedFaceDiceRoller(1));

            Assert.That(unit.GetValue().GetIsDead(), Is.True, "nine 1s on nine wounds");
        }

        [Test]
        public async Task ASafeRoll_WoundsNothing()
        {
            DataBinding<UnitData> unit = MakeUnit(modelCount: 3, tough: 3);

            await RollAndLand(unit, crossing: 3, new FixedFaceDiceRoller(2));

            Assert.That(unit.GetValue().RemainingWounds, Is.EqualTo(9f));
        }

        // ---- Probabilistic: the expected wounds are still spread ---------------------------------------

        [Test]
        public async Task Probabilistic_ThreeToughThree_SpreadsTheExpectedOnePointFiveWounds()
        {
            // Nine dice -> 9/6 = 1.5 expected 1s, spread by each model's share of the unit's wounds (a third
            // each): 0.5 per model, nobody dies. Concentrating it on the first model would not be "spread".
            DataBinding<UnitData> unit = MakeUnit(modelCount: 3, tough: 3);
            GameSettings settings = GameSettings.GetDefault();
            settings.RandomnessType = ERandomnessType.Probabilistic;
            var ctx = new TestGameContext(_store, new ProbabilisticDiceRoller(), settings: settings);

            MovementExecutor.DangerousTerrainResult rolled = MovementExecutor.RollDangerousTerrain(ctx,
                CrossingPaths(unit, 3), new List<ITerrain> { new TerrainData(ETerrainType.Dangerous, DangerZone) },
                unit.GetValue());
            await MovementExecutor.ResolveDangerousTerrain(ctx, rolled);

            Assert.That(rolled.DiceCount, Is.EqualTo(9));
            Assert.That(rolled.Wounds, Is.EqualTo(1.5f).Within(0.001f));
            foreach (IModel model in unit.GetValue().Models)
                Assert.That(model.WoundsDealt, Is.EqualTo(0.5f).Within(0.001f), "an even third of the 1.5");
            Assert.That(unit.GetValue().Models.All(m => m.GetIsAlive()), Is.True);
        }

        [Test]
        public void Probabilistic_UnevenModels_SpreadInProportionToTheirWounds_NeverBeyondWhatTheyHold()
        {
            // A Tough(3) model and a 1-wound model: 4 dice, 4/6 expected 1s, shared 3:1.
            var heavy = MakeModel(tough: 3);
            var light = MakeModel(tough: 1);
            var unit = MakeUnitFrom(heavy, light);

            DangerousTerrainBatch.Outcome outcome = DangerousTerrainBatch.Roll(unit.GetValue(),
                new ProbabilisticDiceRoller(), ERandomnessType.Probabilistic);

            Assert.That(outcome.DiceCount, Is.EqualTo(4));
            Assert.That(outcome.Wounds, Is.EqualTo(4f / 6f).Within(0.001f));
            Assert.That(outcome.PendingWounds[0].Wounds, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(outcome.PendingWounds[1].Wounds, Is.EqualTo(1f / 6f).Within(0.001f));
        }

        // ---- The forced test (Dangerous Terrain Debuff) and the spillout use the same helper ------------

        [Test]
        public void ForcedTest_ThreeToughThreeVictim_RollsNineDice()
        {
            DataBinding<UnitData> victim = MakeUnit(modelCount: 3, tough: 3);
            var ctx = new TestGameContext(_store, new FixedFaceDiceRoller(4));

            Assert.That(MovementExecutor.RollForcedDangerousTerrain(ctx, victim.GetValue()).DiceCount,
                Is.EqualTo(9));
        }

        [Test]
        public void ForcedTest_ToughSixHeroWithTwoWoundsTaken_RollsFourDice()
        {
            DataBinding<UnitData> hero = MakeUnit(modelCount: 1, tough: 6);
            hero.GetValue().Models[0].DealWounds(2f);
            var ctx = new TestGameContext(_store, new FixedFaceDiceRoller(4));

            Assert.That(MovementExecutor.RollForcedDangerousTerrain(ctx, hero.GetValue()).DiceCount,
                Is.EqualTo(4));
        }

        [Test]
        public void Spillout_ThreeToughThreeOccupant_RollsNineDice_AndTwoOnesAreTwoWoundsToTheUnit()
        {
            UnitData occupant = MakeUnit(modelCount: 3, tough: 3).GetValue();

            TransportUtilities.SpilloutRollResult rolls =
                TransportUtilities.ApplySpilloutEffects(occupant, new FixedOnesDiceRoller(2));

            Assert.That(rolls.DiceCount, Is.EqualTo(9));
            Assert.That(rolls.Wounds, Is.EqualTo(2f));
            Assert.That(rolls.PendingWounds.Count, Is.EqualTo(1),
                "both wounds fill the first model - they are the unit's, not one apiece to two models");
            Assert.That(rolls.PendingWounds[0].Wounds, Is.EqualTo(2f));
            Assert.That(rolls.PendingWounds[0].Model, Is.SameAs(occupant.Models[0]));
        }

        [Test]
        public void Spillout_ToughSixHeroWithTwoWoundsTaken_RollsFourDice()
        {
            UnitData hero = MakeUnit(modelCount: 1, tough: 6).GetValue();
            hero.Models[0].DealWounds(2f);

            Assert.That(TransportUtilities.ApplySpilloutEffects(hero, new FixedFaceDiceRoller(4)).DiceCount,
                Is.EqualTo(4));
        }

        // ---- Presentation: the batched row shows the new count ------------------------------------------

        [Test]
        public async Task TheBatchedRow_ShowsOneDiePerWound_AndSaysWhy()
        {
            DataBinding<UnitData> unit = MakeUnit(modelCount: 3, tough: 3);
            var sink = new RecordingPresentationSink();
            var log = new RecordingTextOutput();
            var ctx = new TestGameContext(_store, new FixedOnesDiceRoller(0), textOutput: log,
                presenter: new LocalPresenter(sink, new Presentation.InstantPresentationClock()));

            MovementExecutor.DangerousTerrainResult rolled = MovementExecutor.RollForcedDangerousTerrain(ctx,
                unit.GetValue());
            await MovementExecutor.ResolveDangerousTerrain(ctx, rolled);

            DiceRolledBeat beat = sink.Beats.OfType<DiceRolledBeat>().Single();
            Assert.That(beat.FaceCounts.Sum(), Is.EqualTo(9f).Within(0.001f), "nine dice on the row");
            Assert.That(beat.Context, Does.Contain("9 dice (wounds to destroy)"));
            Assert.That(log.Entries.Select(e => e.Message),
                Has.Some.Contain("9 dice (wounds to destroy)"), "and the log line says the same");
        }

        [Test]
        public async Task TheBatchedRow_OneWoundLeft_SaysOneDie()
        {
            DataBinding<UnitData> unit = MakeUnit(modelCount: 1, tough: 3);
            unit.GetValue().Models[0].DealWounds(2f);
            var sink = new RecordingPresentationSink();
            var ctx = new TestGameContext(_store, new FixedOnesDiceRoller(0),
                presenter: new LocalPresenter(sink, new Presentation.InstantPresentationClock()));

            await MovementExecutor.ResolveDangerousTerrain(ctx,
                MovementExecutor.RollForcedDangerousTerrain(ctx, unit.GetValue()));

            Assert.That(sink.Beats.OfType<DiceRolledBeat>().Single().Context, Does.Contain("1 die (wounds to destroy)"));
        }

        // ---- Helpers ----------------------------------------------------------------------------------

        private TestGameContext NewContext(IDiceRoller roller) => new TestGameContext(_store, roller);

        // Paths for every model; the first `crossing` of them walk through the dangerous zone (z = 0), the
        // rest move along z = 10, clear of it.
        private static List<ModelMoveEntry> CrossingPaths(DataBinding<UnitData> unit, int crossing)
        {
            var paths = new List<ModelMoveEntry>();
            int i = 0;
            foreach (DataBinding<ModelData> model in unit.GetValue().ModelBindings)
            {
                float z = i < crossing ? 0f : 10f;
                model.GetValue().SetPosition(new Position(0f, z));
                paths.Add(new ModelMoveEntry(model, new List<Position> { new Position(8f, z) }));
                i++;
            }
            return paths;
        }

        private MovementExecutor.DangerousTerrainResult RollMove(DataBinding<UnitData> unit, int crossing,
            IDiceRoller roller)
        {
            var ctx = NewContext(roller);
            return MovementExecutor.RollDangerousTerrain(ctx, CrossingPaths(unit, crossing),
                new List<ITerrain> { new TerrainData(ETerrainType.Dangerous, DangerZone) }, unit.GetValue());
        }

        private async Task RollAndLand(DataBinding<UnitData> unit, int crossing, IDiceRoller roller)
        {
            var ctx = NewContext(roller);
            MovementExecutor.DangerousTerrainResult rolled = MovementExecutor.RollDangerousTerrain(ctx,
                CrossingPaths(unit, crossing),
                new List<ITerrain> { new TerrainData(ETerrainType.Dangerous, DangerZone) }, unit.GetValue());
            await MovementExecutor.ResolveDangerousTerrain(ctx, rolled);
        }

        private DataBinding<ModelData> MakeModel(int tough = 1)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), new Position(0, 0), _store);
            if (tough > 1) model.SetMaxWounds(tough);
            return _store.GetDataBinding<ModelData>(_store.Create(model));
        }

        private DataBinding<UnitData> MakeUnitFrom(params DataBinding<ModelData>[] models)
        {
            var player = new PlayerID(Guid.NewGuid());
            var unit = new UnitData(player, "TestUnit", quality: 4, defense: 4, modelBindings: models.ToList());
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        private DataBinding<UnitData> MakeUnit(int modelCount, int tough = 1) =>
            MakeUnitFrom(Enumerable.Range(0, modelCount).Select(_ => MakeModel(tough)).ToArray());

        // 1-wound grunts with a Tough(heroTough) Hero joined in (#006). AttachHero appends the hero's
        // binding, so the hero is last in Models.
        private DataBinding<UnitData> MakeHeroJoinedUnit(int gruntCount, int heroTough)
        {
            DataBinding<UnitData> unit = MakeUnit(gruntCount);
            DataBinding<ModelData> hero = MakeModel(heroTough);
            unit.GetValue().AttachHero(
                new HeroAttachment(hero.GetValue().ID, quality: 3, defense: 3, heroWounds: heroTough),
                new List<DataBinding<ModelData>> { hero });
            return unit;
        }
    }
}
