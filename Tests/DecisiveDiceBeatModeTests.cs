using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Players;
using FDG.Presentation;
using FDG.Presentation.Beats;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #289 — DiceRolledBeat.Mode describes the SHAPE of the histogram the front-end has to draw, not the
    // game's randomness setting. A roll made with IDiceRoller.RollDecisive commits to concrete faces in
    // BOTH roller modes (that is the whole point of the decisive path), so its beat must declare Realistic
    // even in a probabilistic game — otherwise the front-end draws an expected-value success bar for a die
    // that was genuinely rolled, and the player sees no dice at all.
    //
    // Every test here runs with Settings.RandomnessType = Probabilistic AND a ProbabilisticDiceRoller, so a
    // regression that goes back to reading the setting fails loudly.
    [TestFixture]
    public class DecisiveDiceBeatModeTests
    {
        [Test]
        public void FromDecisive_AlwaysDeclaresRealistic()
        {
            float[] perSide = new float[6];
            perSide[3] = 1f; // a 4 came up

            DiceRolledBeat beat = DiceRolledBeat.FromDecisive(new DiceResults(perSide), 4, "Roll to Cast");

            Assert.That(beat.Mode, Is.EqualTo(ERandomnessType.Realistic));
            Assert.That(beat.Total, Is.EqualTo(1f), "one die, one face - no expected-value spread.");
        }

        [Test]
        public async Task MoraleTest_InAProbabilisticGame_PresentsARealisticBeat()
        {
            var sink = new RecordingPresentationSink();
            (TestGameContext ctx, DataBinding<UnitData> unit) = Build(sink);

            await MoraleUtilities.TakeMoraleTest(ctx, unit.GetValue(), baseRollNeeded: 4);

            DiceRolledBeat beat = SingleDiceBeat(sink);
            Assert.That(beat.Mode, Is.EqualTo(ERandomnessType.Realistic),
                "a morale die is decisive, so it must draw as a die even in a probabilistic game.");
            AssertConcreteFaces(beat);
        }

        [Test]
        public async Task ShakenRecoveryRoll_InAProbabilisticGame_PresentsARealisticBeat()
        {
            var sink = new RecordingPresentationSink();
            (TestGameContext ctx, DataBinding<UnitData> unit) = Build(sink);
            unit.GetValue().Tokens.AddToken(new Token(TokenType.Shaken, 1, new TokenClearTrigger.ManualOnly()));

            await OperationExecutor.Execute(
                new[] { new RuleOperation.InvokeClearTokenOnRoll(unit.GetValue(), TokenType.Shaken, 4) },
                new GameOperationServices(ctx));

            DiceRolledBeat beat = SingleDiceBeat(sink);
            Assert.That(beat.Mode, Is.EqualTo(ERandomnessType.Realistic));
            AssertConcreteFaces(beat);
        }

        // The scope guard: a genuinely fractional roll must KEEP the probabilistic vocabulary. Roll (not
        // RollDecisive) spreads rollCount/sideCount across every face, and drawing 1.67 dice is meaningless
        // — so those sites still report the game's setting.
        [Test]
        public void AFractionalPoolRoll_StillDeclaresProbabilistic()
        {
            IDiceResults pool = new ProbabilisticDiceRoller().Roll(6, 10f);

            DiceRolledBeat beat = DiceRolledBeat.From(pool, 4, ERandomnessType.Probabilistic, "Roll to Hit");

            Assert.That(beat.Mode, Is.EqualTo(ERandomnessType.Probabilistic));
            Assert.That(beat.FaceCounts.Any(c => Math.Abs(c - MathF.Round(c)) > 0.001f), Is.True,
                "a probabilistic pool spreads fractionally - there are no dice to draw.");
        }

        private static DiceRolledBeat SingleDiceBeat(RecordingPresentationSink sink)
        {
            DiceRolledBeat? beat = sink.Beats.OfType<DiceRolledBeat>().FirstOrDefault();
            Assert.That(beat, Is.Not.Null, "the roll should have presented a dice beat.");
            return beat!;
        }

        // A decisive beat's histogram is a whole-number multiset of faces the front-end can expand into
        // individual dice; anything fractional would be drawn as round(count) dice and silently lie.
        private static void AssertConcreteFaces(DiceRolledBeat beat)
        {
            foreach (float count in beat.FaceCounts)
                Assert.That(count, Is.EqualTo(MathF.Round(count)).Within(0.0001f),
                    "a decisive roll's face counts are whole dice.");
            Assert.That(beat.Total, Is.GreaterThan(0f));
        }

        private static (TestGameContext Ctx, DataBinding<UnitData> Unit) Build(RecordingPresentationSink sink)
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            GameSettings settings = GameSettings.GetDefault();
            settings.RandomnessType = ERandomnessType.Probabilistic;

            var ctx = new TestGameContext(store, new ProbabilisticDiceRoller(seed: 20260726),
                presenter: new LocalPresenter(sink, new InstantPresentationClock()), settings: settings);

            var player = new PlayerID(Guid.NewGuid());
            var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
                initialPosition: new Position(0, 0), gameDataStore: store);
            var modelBindings = new List<DataBinding<ModelData>>
            {
                store.GetDataBinding<ModelData>(store.Create(model)),
            };
            var unit = new UnitData(player, "Tester", quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> unitBinding = store.GetDataBinding<UnitData>(store.Create(unit));

            // GameOperationServices resolves a unit through its army, so the token-shed path needs one.
            store.Create(new ArmyData(player, new List<DataBinding<UnitData>> { unitBinding }));

            return (ctx, unitBinding);
        }
    }
}
