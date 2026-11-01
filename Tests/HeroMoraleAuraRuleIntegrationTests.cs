using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Players;
using FDG.Presentation;
using FDG.Presentation.Beats;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #421 - OPR 3.6 Hero: "The Hero's unit, and units within 6" of friendly Heroes with Tough(9) or higher, may use
    // the Hero's Quality for Morale Tests as long as the Hero isn't Shaken." (3.5.1 had no aura: only a joined hero
    // lent its Quality, and only to its own unit.)
    //
    // Every test drives a REAL morale dispatch site - the wound-driven test (MoraleUtilities.ResolveWoundDrivenMorale,
    // which ResolveRangedMoraleStage and ResolveStrafeMoraleStage call), the melee
    // loser's (DetermineMoraleSaveNeededStage -> RollForMoraleStage, #419), a rule-forced one (Spell Drain through
    // BeforeAttackActionStage -> GameOperationServices.MoraleTestThen, #429) - and all of them read the one seam,
    // MoraleUtilities.ResolveMoraleQuality. (CastSpellStage's conditional-spell test is pinned in
    // CasterRuleIntegrationTests.) "May use" is a benefit: the engine takes the best Quality with no prompt.
    //
    // The rig: the testing unit is Quality 5 and every die shows 3, so it fails on its own and passes only at a
    // Quality 3 hero's number. Distances are base edge to base edge (0.5" radius bases on a line).
    [TestFixture]
    public class HeroMoraleAuraRuleIntegrationTests
    {
        private const float BaseRadius = 0.5f;
        private const int Die = 3;
        private const float Origin = 10f;

        private GameDataStore _store = null!;
        private TestGameContext _ctx = null!;
        private RecordingTextOutput _log = null!;
        private PlayerID _p1;
        private PlayerID _p2;
        private readonly Dictionary<PlayerID, List<DataBinding<UnitData>>> _armies = new();

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _log = new RecordingTextOutput();
            _ctx = new TestGameContext(_store, new FixedDiceRoller(Die), textOutput: _log);
            _p1 = new PlayerID(System.Guid.NewGuid());
            _p2 = new PlayerID(System.Guid.NewGuid());
            _armies.Clear();
        }

        // --- the aura, through the wound-driven test --------------------------------------------------------------

        [Test]
        public async Task ToughNineHero_At5Inches_NeighbourTestsAtTheHerosQuality()
        {
            DataBinding<UnitData> warriors = Warriors();
            Hero("Captain", _p1, tough: 9, quality: 3, gap: 5f);

            await RunWoundDrivenMorale(warriors);

            Assert.That(IsShaken(warriors), Is.False, "Quality 3 from the Hero: the 3 passes");
            Assert.That(MoraleUtilities.ResolveMoraleQuality(_ctx, warriors.GetValue()),
                Is.EqualTo(new MoraleUtilities.MoraleQuality(3, MoraleUtilities.EMoraleQualitySource.HeroAura, "Captain")));
        }

        [Test]
        public async Task ToughNineHero_At7Inches_GivesNothing()
        {
            DataBinding<UnitData> warriors = Warriors();
            Hero("Captain", _p1, tough: 9, quality: 3, gap: 7f);

            await RunWoundDrivenMorale(warriors);

            Assert.That(IsShaken(warriors), Is.True, "out of 6\": the unit's own Quality 5, and a 3 fails");
        }

        [Test]
        public async Task ToughSixHero_At5Inches_GivesNothing()
        {
            DataBinding<UnitData> warriors = Warriors();
            Hero("Captain", _p1, tough: 6, quality: 3, gap: 5f);

            await RunWoundDrivenMorale(warriors);

            Assert.That(IsShaken(warriors), Is.True, "only Tough(9) or higher projects");
        }

        [Test]
        public async Task TwoHeroes_TheBestQualityWins_NotTheNearest()
        {
            DataBinding<UnitData> warriors = Warriors();
            Hero("Sergeant-Major", _p1, tough: 9, quality: 4, gap: 1f);
            Hero("Lord", _p1, tough: 12, quality: 3, gap: 5f, x: 1.5f);

            await RunWoundDrivenMorale(warriors);

            Assert.That(IsShaken(warriors), Is.False, "the Lord's Quality 3 is on offer, so a 3 passes");
            Assert.That(MoraleUtilities.ResolveMoraleQuality(_ctx, warriors.GetValue()).HeroName, Is.EqualTo("Lord"));
        }

        [Test]
        public async Task HerosUnitShaken_GivesNothing()
        {
            DataBinding<UnitData> warriors = Warriors();
            DataBinding<UnitData> hero = Hero("Captain", _p1, tough: 9, quality: 3, gap: 5f);
            hero.GetValue().Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.Shaken));

            await RunWoundDrivenMorale(warriors);

            Assert.That(IsShaken(warriors), Is.True, "'as long as the Hero isn't Shaken'");
        }

        [Test]
        public async Task HeroInReserve_GivesNothing()
        {
            DataBinding<UnitData> warriors = Warriors();
            DataBinding<UnitData> hero = Hero("Captain", _p1, tough: 9, quality: 3, gap: 5f);
            ReserveRules.PlaceInReserve(hero.GetValue());

            await RunWoundDrivenMorale(warriors);

            Assert.That(IsShaken(warriors), Is.True, "a Hero off the table is within 6\" of nothing (#421 D3)");
        }

        [Test]
        public async Task DeadHero_GivesNothing()
        {
            DataBinding<UnitData> warriors = Warriors();
            DataBinding<UnitData> hero = Hero("Captain", _p1, tough: 9, quality: 3, gap: 5f);
            ModelData model = hero.GetValue().ModelBindings[0].GetValue();
            model.DealWounds(model.TotalWounds);

            await RunWoundDrivenMorale(warriors);

            Assert.That(IsShaken(warriors), Is.True);
        }

        [Test]
        public async Task EnemyHero_At5Inches_GivesNothing()
        {
            DataBinding<UnitData> warriors = Warriors();
            Hero("Warlord", _p2, tough: 9, quality: 3, gap: 5f);

            await RunWoundDrivenMorale(warriors);

            Assert.That(IsShaken(warriors), Is.True, "only FRIENDLY Heroes lend their Quality");
        }

        [Test]
        public async Task TeammatesHero_IsFriendly_AndLendsItsQuality()
        {
            PlayerID ally = new PlayerID(System.Guid.NewGuid());
            _store.Create(new TeamData(0, new List<PlayerID> { _p1, ally }));
            _store.Create(new TeamData(1, new List<PlayerID> { _p2 }));
            DataBinding<UnitData> warriors = Warriors();
            Hero("Allied Captain", ally, tough: 9, quality: 3, gap: 5f);

            await RunWoundDrivenMorale(warriors);

            Assert.That(IsShaken(warriors), Is.False, "friendly is team-aware: an allied player's Hero counts");
        }

        [Test]
        public async Task WoundedToughNineHero_StillProjects_ToughIsTheRuleNotTheWoundsLeft()
        {
            DataBinding<UnitData> warriors = Warriors();
            DataBinding<UnitData> hero = Hero("Captain", _p1, tough: 9, quality: 3, gap: 5f);
            hero.GetValue().ModelBindings[0].GetValue().DealWounds(8f); // 1 wound left

            await RunWoundDrivenMorale(warriors);

            Assert.That(IsShaken(warriors), Is.False, "a Tough(9) hero with one wound left is still Tough(9)");
        }

        [Test]
        public async Task PerModelToughGrant_CountsAsTheHerosTough()
        {
            // #407: a Tough carried by the model itself (a per-model grant) is the model's Tough.
            DataBinding<UnitData> warriors = Warriors();
            DataBinding<UnitData> hero = Hero("Captain", _p1, tough: 0, quality: 3, gap: 5f);
            hero.GetValue().ModelBindings[0].GetValue().AttachRuleDefinition(Tough(9));

            await RunWoundDrivenMorale(warriors);

            Assert.That(IsShaken(warriors), Is.False);
        }

        [Test]
        public async Task MeasuredBaseToBase_NotCentreToCentre()
        {
            // Edges 5.5" apart, centres 6.5" apart: within 6" by the base-edge measure every range gate uses.
            DataBinding<UnitData> warriors = Warriors();
            Hero("Captain", _p1, tough: 9, quality: 3, gap: 5.5f);

            await RunWoundDrivenMorale(warriors);

            Assert.That(IsShaken(warriors), Is.False);
        }

        [Test]
        public void AHeroNoBetterThanTheUnit_IsNotNamed()
        {
            // "May use" - a Quality 5 Hero beside a Quality 5 unit changes nothing, so the unit's own is reported.
            DataBinding<UnitData> warriors = Warriors();
            Hero("Captain", _p1, tough: 9, quality: 5, gap: 2f);

            Assert.That(MoraleUtilities.ResolveMoraleQuality(_ctx, warriors.GetValue()),
                Is.EqualTo(MoraleUtilities.MoraleQuality.Own(5)));
        }

        [Test]
        public void TheHeroItself_TestsAtItsOwnQuality()
        {
            DataBinding<UnitData> hero = Hero("Captain", _p1, tough: 9, quality: 3, gap: 5f);

            Assert.That(MoraleUtilities.ResolveMoraleQuality(_ctx, hero.GetValue()),
                Is.EqualTo(MoraleUtilities.MoraleQuality.Own(3)), "'the Hero's unit' is its own Quality");
        }

        [Test]
        public void JoinedHero_BeatsAWorseAura_AndAnAuraBeatsAWorseJoinedHero()
        {
            DataBinding<UnitData> host = JoinedUnit(heroQuality: 4);
            Hero("Lord", _p1, tough: 12, quality: 3, gap: 4f, x: 30f);

            Assert.That(MoraleUtilities.ResolveMoraleQuality(_ctx, host.GetValue()),
                Is.EqualTo(new MoraleUtilities.MoraleQuality(3, MoraleUtilities.EMoraleQualitySource.HeroAura, "Lord")));

            DataBinding<UnitData> host2 = JoinedUnit(heroQuality: 2, x: 40f);
            Assert.That(MoraleUtilities.ResolveMoraleQuality(_ctx, host2.GetValue()),
                Is.EqualTo(new MoraleUtilities.MoraleQuality(2, MoraleUtilities.EMoraleQualitySource.JoinedHero, "Banner Captain")));
        }

        [Test]
        public void AJoinedHeroWorseThanItsUnit_IsNotUsed()
        {
            // 3.5.1 had the joined hero take the test "on behalf of the unit" whatever its Quality; 3.6's "may use"
            // makes it a choice, so a Quality 6 hero leaves its Quality 5 unit on its own number.
            DataBinding<UnitData> host = JoinedUnit(heroQuality: 6);

            Assert.That(MoraleUtilities.ResolveMoraleQuality(_ctx, host.GetValue()),
                Is.EqualTo(MoraleUtilities.MoraleQuality.Own(5)));
        }

        // --- presentation: the log line and the roll chip name the source ----------------------------------------

        [Test]
        public async Task TheLogLineAndTheRollChip_NameTheHeroWhoseAuraWasUsed()
        {
            var sink = new RecordingPresentationSink();
            _ctx = new TestGameContext(_store, new FixedDiceRoller(Die), textOutput: _log,
                presenter: new LocalPresenter(sink, new InstantPresentationClock()));
            DataBinding<UnitData> warriors = Warriors();
            Hero("Captain", _p1, tough: 9, quality: 3, gap: 5f);

            await RunWoundDrivenMorale(warriors);

            Assert.That(_log.Entries.Select(e => e.Message),
                Contains.Item("Warriors tests morale at Quality 3+ (Hero aura: Captain)."));
            DiceRolledBeat roll = sink.Beats.OfType<DiceRolledBeat>().Single(b => b.Label == "Warriors - Morale Test");
            Assert.That(roll.SuccessThreshold, Is.EqualTo(3));
            Assert.That(roll.ModifierTags, Is.EqualTo(new[] { "Quality 3+ (Hero aura: Captain)" }),
                "a Hero-sourced test always shows its source chip, even with no other modifier");
        }

        [Test]
        public async Task AJoinedHerosLogLine_NamesTheJoinedHero()
        {
            DataBinding<UnitData> host = JoinedUnit(heroQuality: 3);

            await MoraleUtilities.TakeMoraleTest(_ctx, host.GetValue(),
                MoraleUtilities.ResolveMoraleQuality(_ctx, host.GetValue()));

            Assert.That(_log.Entries.Select(e => e.Message),
                Contains.Item("Squad tests morale at Quality 3+ (joined Hero: Banner Captain)."));
        }

        [Test]
        public async Task AUnitOnItsOwnQuality_LogsNoSourceLine()
        {
            DataBinding<UnitData> warriors = Warriors();

            await RunWoundDrivenMorale(warriors);

            Assert.That(_log.Entries.Any(e => e.Message.Contains("tests morale at")), Is.False);
        }

        [Test]
        public void HeroDescription_IsTheCore360Wording()
        {
            Assert.That(CoreRuleCatalog.Hero.Description, Is.EqualTo(
                "Heroes with up to Tough(6) may deploy as part of one multi-model unit without another Hero, but must " +
                "use the unit's Defense until all other models have been killed. The Hero's unit, and units within 6\" " +
                "of friendly Heroes with Tough(9) or higher, may use the Hero's Quality for Morale Tests as long as the " +
                "Hero isn't Shaken."));
            Assert.That(CoreRuleCatalog.Hero.Description.All(c => c < 128), Is.True, "game text is ASCII-only");
        }

        // --- the melee loser's test (#419) -------------------------------------------------------------------------

        [TestCase(5f, true)]
        [TestCase(7f, false)]
        public async Task MeleeLoser_UsesTheAura(float gap, bool expectedPass)
        {
            DataBinding<UnitData> warriors = Warriors();
            Hero("Captain", _p1, tough: 9, quality: 3, gap: gap);
            DataBinding<UnitData> chargers = MakeUnit(_p2, "Chargers", models: 2, quality: 4, x: -20f, z: 0f);

            var melee = new CombatActionContext(_ctx, chargers, isMelee: true);
            melee.SetDefender(warriors);
            melee.AddResult(new DetermineMeleeWinnerResults(DetermineMeleeWinnerResults.EMeleeWinnerResult.AttackerWon));

            var determine = new DetermineMoraleSaveNeededStage(_ctx, new NoOpLayer<ICombatActionContext>());
            determine.ToRollForMorale.Bind(DetermineMoraleSaveNeededStage.DETERMINE_MORALE_SAVE_NEEDED_FINISHED_TRANSITION);
            await determine.Enter(melee);

            var roll = new RollForMoraleStage(_ctx, new NoOpLayer<ICombatActionContext>());
            bool passed = false;
            roll.OnMoralePassed.Bind(RollForMoraleStage.ROLL_FOR_MORALE_PASSED_TRANSITION);
            roll.OnMoraleFailed.Bind(RollForMoraleStage.ROLL_FOR_MORALE_FAILED_TRANSITION);
            roll.OnMoralePassed.OnWillActivate += _ => passed = true;
            await roll.Enter(melee);

            Assert.That(passed, Is.EqualTo(expectedPass), $"the losing unit's Hero is {gap}\" away");
        }

        // --- a rule-forced test: Spell Drain (#429) ----------------------------------------------------------------

        [TestCase(5f, 3)]
        [TestCase(7f, 1)]
        public async Task SpellDrainVictim_UsesItsFriendlyHerosAura(float gap, int tokensLeft)
        {
            // The victim is the Quality 5 caster; its own side's Tough(9) Hero stands beside it.
            DataBinding<UnitData> victim = MakeUnit(_p1, "Hex-Witch", models: 1, quality: 5, x: 0f, z: 0f);
            victim.GetValue().AttachRuleDefinition(new ResolvedRule("Caster", CoreRuleCatalog.Caster,
                new RuleArgument[] { new RuleArgument.Int(2) }));
            victim.GetValue().Tokens.AddToken(new Token(TokenType.SpellTokens, 3, new TokenClearTrigger.ManualOnly()));
            Hero("Captain", _p1, tough: 9, quality: 3, gap: gap);

            DataBinding<UnitData> drainer = MakeUnit(_p2, "Inquisitor", models: 1, quality: 4, x: -10f, z: 0f);
            drainer.GetValue().AttachRuleDefinition(
                new ResolvedRule("Spell Drain", SpellDrainRuleIntegrationTests.SpellDrain()));
            var ctx = new TestGameContext(_store, new FixedDiceRoller(Die), playerRequester: new PickUnit(victim));

            var unitCtx = new UnitActionContext(ctx, drainer);
            unitCtx.Reset(drainer);
            unitCtx.SetPendingCustomAction(ctx.RuleEvaluator.GatherOffers(new BeforeAttackActionContext(drainer.GetValue()))
                .Single(o => o.RuleName == "Spell Drain"));
            var stage = new BeforeAttackActionStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("OnFinished");
            await stage.Enter(unitCtx);

            Assert.That(victim.GetValue().Tokens.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(tokensLeft),
                gap <= 6f ? "passed at the Hero's Quality 3 - nothing drained" : "failed at its own Quality 5 - 2 drained");
        }

        // --- helpers -----------------------------------------------------------------------------------------------

        // The Quality 5 unit under test: four models on the z = 0 line, x = 0, 1.5, 3, 4.5.
        private DataBinding<UnitData> Warriors() => MakeUnit(_p1, "Warriors", models: 4, quality: 5, x: 0f, z: 0f);

        // A single-model Hero whose base edge is `gap` inches from the Warriors' line (straight up the z axis).
        private DataBinding<UnitData> Hero(string name, PlayerID player, int tough, int quality, float gap, float x = 0f)
        {
            DataBinding<UnitData> hero = MakeUnit(player, name, models: 1, quality: quality, x: x,
                z: gap + 2f * BaseRadius);
            hero.GetValue().AttachRuleDefinition(new ResolvedRule("Hero", CoreRuleCatalog.Hero));
            if (tough > 0)
            {
                hero.GetValue().AttachRuleDefinition(Tough(tough));
                hero.GetValue().ModelBindings[0].GetValue().SetMaxWounds(tough);
            }
            return hero;
        }

        // A Quality 5 host with a Tough(3) hero of the given Quality joined to it, 30" up the x axis.
        private DataBinding<UnitData> JoinedUnit(int heroQuality, float x = 30f)
        {
            UnitData host = BuildUnit(_p1, "Squad", models: 3, quality: 5, x: x, z: 0f);
            UnitData hero = BuildUnit(_p1, "Banner Captain", models: 1, quality: heroQuality, x: x, z: 1.5f);
            hero.AttachRuleDefinition(new ResolvedRule("Hero", CoreRuleCatalog.Hero));
            hero.AttachRuleDefinition(Tough(3));
            HeroJoinResolver.Apply(new List<(SaveLoad.UnitFileEntry, UnitData)>
            {
                (new SaveLoad.UnitFileEntry { Id = "host" + x }, host),
                (new SaveLoad.UnitFileEntry { JoinsUnitId = "host" + x }, hero),
            });
            return Register(host);
        }

        private static ResolvedRule Tough(int value) =>
            new ResolvedRule("Tough", CoreRuleCatalog.Tough, new RuleArgument[] { new RuleArgument.Int(value) });

        private DataBinding<UnitData> MakeUnit(PlayerID player, string name, int models, int quality, float x, float z)
            => Register(BuildUnit(player, name, models, quality, x, z));

        private UnitData BuildUnit(PlayerID player, string name, int models, int quality, float x, float z)
        {
            var bindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < models; i++)
            {
                // Offset off the origin: a model at exactly (0, 0) reads as not yet placed (GetIsOnBattlefield).
                var model = new ModelData(BaseRadius, new List<Weapon> { new Weapon("Blade", 0f, 1, 0) },
                    new Position(Origin + x + i * 1.5f, Origin + z), _store);
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            return new UnitData(player, name, quality: quality, defense: 4, modelBindings: bindings);
        }

        private DataBinding<UnitData> Register(UnitData unit)
        {
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(unit.PlayerID, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        // The two far Warriors (x = 3, 4.5) fall to a volley, leaving the unit at half strength: the test is due,
        // and the survivors at x = 0 and 1.5 are the ones a Hero's 6" is measured to.
        private async Task RunWoundDrivenMorale(DataBinding<UnitData> unit)
        {
            float before = unit.GetValue().RemainingWounds;
            for (int i = 2; i < 4; i++)
            {
                ModelData model = unit.GetValue().ModelBindings[i].GetValue();
                model.DealWounds(model.TotalWounds);
            }
            await MoraleUtilities.ResolveWoundDrivenMorale(_ctx, unit, before);
        }

        private static bool IsShaken(DataBinding<UnitData> unit) => unit.GetValue().Tokens.HasToken(TokenType.Shaken);

        // Answers the before-attack target pick with the given unit; nothing else is expected.
        private sealed class PickUnit : IPlayerRequestByID
        {
            private readonly DataBinding<UnitData> _target;

            public PickUnit(DataBinding<UnitData> target) => _target = target;

            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply>
            {
                if (request is CancellableSelectionRequest<UnitData>)
                {
                    return Task.FromResult((TReply)(object)new Selected<DataBinding<UnitData>>(_target));
                }

                throw new System.InvalidOperationException("Unexpected request: " + request.GetType());
            }
        }
    }
}
