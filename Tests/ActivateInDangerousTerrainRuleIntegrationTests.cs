using System.Linq;
using FDG.Data;
using FDG.Players;
using FDG.Presentation;
using FDG.Presentation.Beats;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #446 - Core Rules v3.6.0, Dangerous Terrain: "Units with models moving across Dangerous terrain, OR THAT
    // ACTIVATE IN IT, must roll as many dice as the number of wounds it would take to fully destroy them, and for
    // each roll of 1 the unit takes one wound." #420 (DangerousTerrainDiceRuleIntegrationTests) pinned the pool;
    // the move half has always rolled. These pin the activation half, through the REAL MainUnitActionStage graph
    // where the wiring matters:
    //  - a unit that Holds standing in Dangerous terrain tests once, before its action menu, and the dice row and
    //    log line name the trigger; the activation can no longer be backed out of (dice were rolled);
    //  - one test per activation on either trigger (446 D2): moving out across the piece afterwards does not test
    //    again, nor does a "counts as Dangerous" move; the marker is swept at both ends of the activation;
    //  - "in it" is the move's own base-footprint test (D3): a base that stops short of the edge does not test;
    //  - Flying ignores it, Strider does not, an embarked unit does not, a unit placed there by deployment tests
    //    at its first activation;
    //  - a unit the test destroys ends its activation with no menu;
    //  - the test's wounds are "taken during this activation" for #419's melee result (a charge out of it).
    [TestFixture]
    public class ActivateInDangerousTerrainRuleIntegrationTests
    {
        // The piece the "inside" units stand in: x 0..10, z 0..10.
        private static readonly RectangularZone DangerZone = new RectangularZone(0f, 10f, 0f, 10f);
        private const float BaseRadius = 0.5f;

        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        // ---- Hold in Dangerous terrain: one test, before the action menu --------------------------------------

        [Test]
        public async Task Hold_InDangerousTerrain_TestsOnce_BeforeTheMenu_AndNamesTheTrigger()
        {
            AddDangerousTerrain();
            DataBinding<UnitData> unit = MakeUnit("Squad", models: 5, x: 5f);
            var run = await RunActivation(unit, new FixedOnesDiceRoller(2), ChooseActionStage.PASS_CHOICE_NAME);

            Assert.That(run.Requester.ActionMenuCount, Is.EqualTo(1), "the unit still gets its activation");
            Assert.That(run.Requester.FirstRequestWasMenuAfterDice, Is.True,
                "the test is rolled before the action menu is posed");
            DiceRolledBeat beat = run.DangerBeats.Single();
            Assert.That(beat.FaceCounts.Sum(), Is.EqualTo(5f).Within(0.001f), "five dice - the #420 pool");
            Assert.That(beat.Context, Is.EqualTo("Squad: activated in Dangerous terrain - 5 dice (wounds to destroy)"));
            Assert.That(run.Lines, Has.Member(
                "Squad: dangerous terrain test (activated in Dangerous terrain) - 5 dice (wounds to destroy), 2 wound(s) dealt."));
            Assert.That(LivingModels(unit), Is.EqualTo(3), "two 1s, two wounds to the unit");
            Assert.That(run.Reconciled, Is.True, "Pass ends the activation normally");
        }

        [Test]
        public async Task Hold_InDangerousTerrain_TheActivationCanNoLongerBeBackedOut()
        {
            // Re-picking the unit would re-roll the test (#248), so the menu after it is not cancellable.
            AddDangerousTerrain();
            DataBinding<UnitData> unit = MakeUnit("Squad", models: 5, x: 5f);

            var run = await RunActivation(unit, new FixedOnesDiceRoller(0), ChooseActionStage.PASS_CHOICE_NAME);

            Assert.That(run.DangerBeats.Count, Is.EqualTo(1), "premise: it tested (all safe)");
            Assert.That(run.Requester.MenuAllowCancel.Single(), Is.False);
        }

        [Test]
        public async Task Hold_OutsideDangerousTerrain_NoTest_AndTheActivationStaysPristine()
        {
            AddDangerousTerrain();
            DataBinding<UnitData> unit = MakeUnit("Squad", models: 5, x: 20f);

            var run = await RunActivation(unit, new FixedFaceDiceRoller(1), ChooseActionStage.PASS_CHOICE_NAME);

            Assert.That(run.DangerBeats, Is.Empty);
            Assert.That(LivingModels(unit), Is.EqualTo(5));
            Assert.That(run.Requester.MenuAllowCancel.Single(), Is.True, "nothing was rolled, so Back still works");
        }

        // ---- One test per activation (446 D2) -------------------------------------------------------------------

        [Test]
        public async Task MoveOutAcrossTheSamePiece_TestsOnce_NotTwice()
        {
            // The unit starts inside and walks 8in out the far side. Before #446 that move rolled (its first leg
            // starts overlapping the piece); now the activation test has already covered this activation.
            AddDangerousTerrain();
            DataBinding<UnitData> unit = MakeUnit("Squad", models: 5, x: 5f);

            var run = await RunActivation(unit, new FixedOnesDiceRoller(1),
                ChooseActionStage.MOVEMENT_CHOICE_NAME, ChooseActionStage.PASS_CHOICE_NAME);

            Assert.That(unit.GetValue().Models.Where(m => m.GetIsAlive()).All(m => m.Position.x > 12f), Is.True,
                "premise: the four survivors moved out");
            Assert.That(run.DangerBeats.Count, Is.EqualTo(1), "one test for the activation, not one more for the move");
            Assert.That(run.Lines.Count(l => l.Contains("dice (wounds to destroy)")), Is.EqualTo(1),
                "one rolled test in the log");
            Assert.That(run.Lines, Has.Member(
                "Squad: already took its dangerous terrain test this activation - no second test for this move."));
            Assert.That(WoundsTakenThisActivation.Peek(unit.GetValue()), Is.EqualTo(1f), "one 1, from the one test");
        }

        [Test]
        public async Task TheMoveTestAlone_StillRolls_ForAUnitThatActivatedOutside()
        {
            // Control for the above: the same walk through the piece from outside it is the move trigger, and
            // rolls exactly as it did before #446.
            AddDangerousTerrain();
            DataBinding<UnitData> unit = MakeUnit("Squad", models: 5, x: -3f);

            var run = await RunActivation(unit, new FixedOnesDiceRoller(1),
                ChooseActionStage.MOVEMENT_CHOICE_NAME, ChooseActionStage.PASS_CHOICE_NAME, moveInches: 6f);

            DiceRolledBeat beat = run.DangerBeats.Single();
            Assert.That(beat.Context, Is.EqualTo("Squad: 5 dice (wounds to destroy)"), "the move row is unchanged");
            Assert.That(DangerousTerrainTestedThisActivation.Has(unit.GetValue()), Is.True,
                "and a move test marks the activation too");
        }

        [Test]
        public void AfterTheActivationTest_ACountsAsDangerousMove_DoesNotTestAgain()
        {
            // "Counts as being in Dangerous terrain" is the same test, so it obeys the same marker (446 D4).
            AddDangerousTerrain();
            DataBinding<UnitData> unit = MakeUnit("Squad", models: 3, x: 20f);
            var ctx = NewContext(new FixedFaceDiceRoller(1));
            DangerousTerrainTestedThisActivation.Mark(unit.GetValue());

            var paths = unit.GetValue().ModelBindings
                .Select(m => new ModelMoveEntry(m, new List<Position> { new Position(25f, m.GetValue().Position.z) }))
                .ToList();
            MovementExecutor.DangerousTerrainResult result = MovementExecutor.RollDangerousTerrain(ctx, paths,
                new List<ITerrain>(), unit.GetValue(), countsAsInDangerousTerrain: true);

            Assert.That(result.AnyTested, Is.False);
        }

        [Test]
        public async Task AMarkerLeftOverFromAnEarlierActivation_IsSwept_SoTheUnitStillTests()
        {
            // The start of the activation clears the marker table-wide (MainUnitActionStage), so whatever an
            // earlier activation left behind - a Harassing move in the enemy's turn - never suppresses this test.
            AddDangerousTerrain();
            DataBinding<UnitData> unit = MakeUnit("Squad", models: 5, x: 5f);
            DangerousTerrainTestedThisActivation.Mark(unit.GetValue());

            var run = await RunActivation(unit, new FixedOnesDiceRoller(0), ChooseActionStage.PASS_CHOICE_NAME);

            Assert.That(run.DangerBeats.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task TheEndOfTheActivation_SweepsTheMarker_OnEveryUnit()
        {
            DataBinding<UnitData> activated = MakeUnit("Activated", models: 1, x: 20f);
            DataBinding<UnitData> harassed = MakeUnit("Harassed", models: 1, x: 30f);
            DangerousTerrainTestedThisActivation.Mark(activated.GetValue());
            DangerousTerrainTestedThisActivation.Mark(harassed.GetValue());
            var ctx = NewContext(new FixedFaceDiceRoller(4));

            var turn = new SingleTurnContext(ctx, activated.GetValue().PlayerID,
                new List<DataBinding<UnitData>> { activated });
            turn.ChooseUnitToActivate(activated);
            var reconcile = new ReconcileEndOfActivationStage(ctx, new NoOpLayer<ISingleTurnContext>());
            reconcile.OnFinished.Bind("done");
            await reconcile.Enter(turn);

            Assert.That(DangerousTerrainTestedThisActivation.Has(activated.GetValue()), Is.False);
            Assert.That(DangerousTerrainTestedThisActivation.Has(harassed.GetValue()), Is.False,
                "a unit tested during someone else's activation is cleared too");
        }

        // ---- "In it" = the move's base-footprint test (446 D3) --------------------------------------------------

        [Test]
        public async Task ABaseThatStopsShortOfTheEdge_IsNotInIt()
        {
            AddDangerousTerrain();
            // Centre 0.6in left of the piece's x = 0 edge, base radius 0.5in: a 0.1in gap.
            DataBinding<UnitData> unit = MakeUnit("Squad", models: 3, x: -0.6f);

            var (finished, destroyed, _) = await RunTestStage(unit, new FixedFaceDiceRoller(1));

            Assert.That(finished && !destroyed, Is.True);
            Assert.That(LivingModels(unit), Is.EqualTo(3), "adjacent is not inside - no test");
        }

        [Test]
        public async Task ABaseThatPokesIntoThePiece_IsInIt_EvenIfOnlyOneModelDoes()
        {
            AddDangerousTerrain();
            // Every model clear of the piece except the first, whose base overlaps the edge by 0.1in. As on a
            // move, one model in it puts the whole unit to the test, at the unit's full pool.
            DataBinding<UnitData> unit = MakeUnit("Squad", models: 3, x: -20f);
            unit.GetValue().Models[0].SetPosition(new Position(-0.4f, 5f));

            var (_, _, run) = await RunTestStage(unit, new FixedOnesDiceRoller(1));

            Assert.That(unit.GetValue().RemainingWounds, Is.EqualTo(2f), "three dice, one 1");
            Assert.That(DangerousTerrainTestedThisActivation.Has(unit.GetValue()), Is.True);
            Assert.That(run.Lines, Has.Some.Contain("3 dice (wounds to destroy)"));
        }

        // ---- Scopes ------------------------------------------------------------------------------------------

        [Test]
        public async Task Flying_IgnoresIt_APlainUnitDoesNot()
        {
            AddDangerousTerrain();
            DataBinding<UnitData> flyer = MakeUnit("Flyer", models: 3, x: 5f, rule: CoreRuleCatalog.Flying);
            DataBinding<UnitData> plain = MakeUnit("Plain", models: 3, x: 5f, z0: 6f);

            await RunTestStage(flyer, new FixedFaceDiceRoller(1));
            await RunTestStage(plain, new FixedOnesDiceRoller(1));

            Assert.That(LivingModels(flyer), Is.EqualTo(3), "Flying ignores the effects of Dangerous terrain");
            Assert.That(DangerousTerrainTestedThisActivation.Has(flyer.GetValue()), Is.False);
            Assert.That(LivingModels(plain), Is.EqualTo(2), "control");
        }

        [Test]
        public async Task Strider_DoesNotIgnoreIt()
        {
            // Strider "may ignore the effects of Difficult terrain" - Dangerous is not Difficult.
            AddDangerousTerrain();
            DataBinding<UnitData> strider = MakeUnit("Strider", models: 3, x: 5f, rule: CoreRuleCatalog.Strider);

            await RunTestStage(strider, new FixedOnesDiceRoller(1));

            Assert.That(LivingModels(strider), Is.EqualTo(2));
        }

        [Test]
        public async Task AnEmbarkedUnit_DoesNotTest_ItsModelsAreNotOnTheTable()
        {
            // Embarked models are parked at the table origin - which this piece covers. The transport tests for
            // itself; its cargo is inside it, not in the terrain.
            _store.Create(new TerrainData(ETerrainType.Dangerous, new RectangularZone(-2f, 2f, -2f, 2f)));
            DataBinding<UnitData> transport = MakeUnit("Transport", models: 1, x: 30f);
            DataBinding<UnitData> cargo = MakeUnit("Cargo", models: 3, x: 0f, z0: 0f);
            cargo.GetValue().Tokens.AddToken(new Token(TokenType.EmbarkedIn, 1, new TokenClearTrigger.ManualOnly(),
                OwnerUnitID: transport.GetValue().ID));

            await RunTestStage(cargo, new FixedFaceDiceRoller(1));

            Assert.That(LivingModels(cargo), Is.EqualTo(3));
        }

        [Test]
        public async Task AUnitDeployedIntoDangerousTerrain_TestsAtItsFirstActivation()
        {
            // Ambush / Scout / a teleport placement is a PLACEMENT, not a move across the piece, so it rolls
            // nothing when it lands - the unit "activates in it" next, and that is when it tests.
            AddDangerousTerrain();
            DataBinding<UnitData> unit = MakeUnit("Ambushers", models: 5, x: 5f);
            unit.GetValue().Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.ArrivedFromReserve));

            var run = await RunActivation(unit, new FixedOnesDiceRoller(1), ChooseActionStage.PASS_CHOICE_NAME);

            Assert.That(run.DangerBeats.Count, Is.EqualTo(1));
            Assert.That(LivingModels(unit), Is.EqualTo(4));
        }

        [Test]
        public async Task ACountsAsDangerousGrant_IsNotAnActivationTrigger()
        {
            // The deferred Dangerous Terrain Debuff rides the victim's next MOVE (446 D4): standing outside any
            // Dangerous piece with the grant, the unit takes no test at activation.
            AddDangerousTerrain();
            DataBinding<UnitData> unit = MakeUnit("Victim", models: 3, x: 20f, rule: CountsAsDangerous());
            var probe = NewContext(new FixedFaceDiceRoller(1));
            Assert.That(MovementRuleQueries.CountsAsInTerrain(unit.GetValue(), probe.RuleEvaluator,
                ECountAsTerrain.Dangerous), Is.True, "premise: the grant is live");

            await RunTestStage(unit, new FixedFaceDiceRoller(1));

            Assert.That(LivingModels(unit), Is.EqualTo(3));
        }

        // ---- A unit the test destroys ends its activation --------------------------------------------------------

        [Test]
        public async Task AUnitDestroyedByTheTest_EndsItsActivation_WithNoMenu()
        {
            AddDangerousTerrain();
            DataBinding<UnitData> unit = MakeUnit("Squad", models: 3, x: 5f);

            var run = await RunActivation(unit, new FixedFaceDiceRoller(1), ChooseActionStage.PASS_CHOICE_NAME);

            Assert.That(unit.GetValue().GetIsDead(), Is.True, "premise: three 1s on three wounds");
            Assert.That(run.Requester.ActionMenuCount, Is.EqualTo(0), "a dead unit is not offered actions");
            Assert.That(run.Reconciled, Is.True, "it leaves through the end-of-activation exit");
            Assert.That(run.BackedOut, Is.False);
            Assert.That(run.Lines, Has.Member("Squad was destroyed by Dangerous terrain - its activation ends."));
        }

        // ---- #419: the test's wounds count in a melee later in the same activation --------------------------------

        [Test]
        public async Task AChargeOutOfDangerousTerrain_TheTestsWoundsCountInTheMeleeResult()
        {
            // #419's acceptance case (Impact(2) kills 2, swings kill 2, the 2 survivors strike back for 2: the
            // Defender took more, 4 vs 2, and loses) - run from inside Dangerous terrain. The activation test
            // costs the Tough(9) charger 3 wounds first, which flips it: 5 vs 4, the CHARGER loses. Driven through
            // the real MainUnitActionStage, so this also pins that the test runs after the activation's
            // wound-ledger sweep.
            _store.Create(new TerrainData(ETerrainType.Dangerous, new RectangularZone(17f, 20.9f, 18f, 22f)));
            var log = new RecordingTextOutput();
            var (charger, defender) = BuildCharge();
            var requester = new ActivationRequester(ChooseActionStage.CHARGE_CHOICE_NAME, ChooseActionStage.PASS_CHOICE_NAME);
            var ctx = new TestGameContext(_store, new FirstRollOnesThenFaceDiceRoller(ones: 3, face: 4),
                textOutput: log, playerRequester: requester);

            await RunMain(ctx, charger);

            List<string> lines = log.Entries.Select(e => e.Message).ToList();
            Assert.That(lines, Has.Member(
                "Charger: dangerous terrain test (activated in Dangerous terrain) - 9 dice (wounds to destroy), 3 wound(s) dealt."));
            Assert.That(lines, Has.Member("Charger already took 3 wounds earlier this activation - they count."));
            Assert.That(lines, Has.Member(
                "Melee result: Charger took 5 wounds, Defender took 4 wounds - Charger took more and loses the melee."));
        }

        // ---- helpers --------------------------------------------------------------------------------------------

        private void AddDangerousTerrain() => _store.Create(new TerrainData(ETerrainType.Dangerous, DangerZone));

        private static int LivingModels(DataBinding<UnitData> unit) =>
            unit.GetValue().Models.Count(model => model.GetIsAlive());

        private sealed record ActivationRun(ActivationRequester Requester, List<DiceRolledBeat> DangerBeats,
            List<string> Lines, bool Reconciled, bool BackedOut);

        // The real MainUnitActionStage for one activation: the menu replies in order (Pass once they run out), a
        // straight move of moveInches in +x for every living model.
        private async Task<ActivationRun> RunActivation(DataBinding<UnitData> unit, IDiceRoller dice,
            string firstReply, string? secondReply = null, float moveInches = 8f)
        {
            var sink = new RecordingPresentationSink();
            var log = new RecordingTextOutput();
            var requester = secondReply == null
                ? new ActivationRequester(firstReply)
                : new ActivationRequester(firstReply, secondReply);
            requester.MoveInches = moveInches;
            requester.Sink = sink;
            var ctx = new TestGameContext(_store, dice, textOutput: log,
                presenter: new LocalPresenter(sink, new InstantPresentationClock()), playerRequester: requester);

            var (reconciled, backedOut) = await RunMain(ctx, unit);

            return new ActivationRun(requester,
                sink.Beats.OfType<DiceRolledBeat>().Where(b => b.Label == "Dangerous Terrain").ToList(),
                log.Entries.Select(e => e.Message).ToList(), reconciled, backedOut);
        }

        private static async Task<(bool Reconciled, bool BackedOut)> RunMain(IGameContext ctx, DataBinding<UnitData> unit)
        {
            var turn = new SingleTurnContext(ctx, unit.GetValue().PlayerID, new List<DataBinding<UnitData>> { unit });
            turn.ChooseUnitToActivate(unit);
            var main = new MainUnitActionStage(ctx, new NoOpLayer<ISingleTurnContext>());
            bool reconciled = false, backedOut = false;
            main.OnBackedOut.Bind("backedOut");
            main.ToReconcileEndOfActivation.Bind("reconcile");
            main.OnBackedOut.OnWillActivate += _ => backedOut = true;
            main.ToReconcileEndOfActivation.OnWillActivate += _ => reconciled = true;
            await main.Enter(turn);
            return (reconciled, backedOut);
        }

        private sealed class StageRun
        {
            public readonly List<string> Lines = new();
        }

        // Just the #446 stage, on a fresh activation context - for the scope questions, where the rest of the
        // activation adds nothing.
        private async Task<(bool Finished, bool Destroyed, StageRun Run)> RunTestStage(DataBinding<UnitData> unit,
            IDiceRoller dice)
        {
            var log = new RecordingTextOutput();
            var ctx = new TestGameContext(_store, dice, textOutput: log);
            var context = new UnitActionContext(ctx, unit);
            context.Reset(unit);

            var stage = new ActivatedInDangerousTerrainStage(ctx, new NoOpLayer<IUnitActionContext>());
            bool finished = false, destroyed = false;
            stage.OnFinished.Bind("finished");
            stage.OnUnitDestroyed.Bind("destroyed");
            stage.OnFinished.OnWillActivate += _ => finished = true;
            stage.OnUnitDestroyed.OnWillActivate += _ => destroyed = true;
            await stage.Enter(context);

            var run = new StageRun();
            run.Lines.AddRange(log.Entries.Select(e => e.Message));
            return (finished, destroyed, run);
        }

        private TestGameContext NewContext(IDiceRoller dice) => new TestGameContext(_store, dice);

        private int _nextTeam;

        // 1-wound models (base radius 0.5in) in a column along z from z0, 1.1in apart, at x; their own player,
        // team and army, so the action menu's gates can resolve them.
        private DataBinding<UnitData> MakeUnit(string name, int models, float x, float z0 = 1f,
            SpecialRuleDefinition? rule = null)
        {
            var player = new PlayerID(Guid.NewGuid());
            _store.Create(new TeamData(_nextTeam++, new List<PlayerID> { player }));
            var bindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < models; i++)
            {
                var model = new ModelData(BaseRadius, new List<Weapon>(), new Position(x, z0 + i * 1.1f), _store);
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(player, name, quality: 4, defense: 4, modelBindings: bindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            if (rule != null) binding.GetValue().AttachRuleDefinition(new ResolvedRule(rule.Name, rule));
            _store.Create(new ArmyData(player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        // The deferred Dangerous Terrain Debuff's granted rule (TerrainDebuffRuleIntegrationTests' shape),
        // attached outright.
        private static SpecialRuleDefinition CountsAsDangerous() => new("Dangerous Terrain Debuff Effect",
            new[]
            {
                new HookEntry(EHookID.Movement_OnMoveThroughTerrain, new Condition.Always(),
                    new Effect.CountAsInTerrain(ECountAsTerrain.Dangerous), ELifetime.ThisActivation),
            },
            System.Array.Empty<ActivatedAbility>());

        // MeleeResultRuleIntegrationTests' acceptance table: the Tough(9) Impact(2) charger at (20, 20) and six
        // 1-wound defenders in base reach in front of it, each side on its own team and army.
        private (DataBinding<UnitData> Charger, DataBinding<UnitData> Defender) BuildCharge()
        {
            var chargerPlayer = new PlayerID(Guid.NewGuid());
            var defenderPlayer = new PlayerID(Guid.NewGuid());
            _store.Create(new TeamData(0, new List<PlayerID> { chargerPlayer }));
            _store.Create(new TeamData(1, new List<PlayerID> { defenderPlayer }));

            var claws = new Weapon("Claws", rangeInches: 0f, attacks: 2, armorPenetration: 0);
            var chargerModel = new ModelData(0.75f, new List<Weapon> { claws }, new Position(20f, 20f), _store);
            chargerModel.SetMaxWounds(9);
            var chargerUnit = new UnitData(chargerPlayer, "Charger", quality: 4, defense: 5,
                modelBindings: new List<DataBinding<ModelData>>
                {
                    _store.GetDataBinding<ModelData>(_store.Create(chargerModel)),
                });
            DataBinding<UnitData> charger = _store.GetDataBinding<UnitData>(_store.Create(chargerUnit));
            charger.GetValue().AttachRuleDefinition(new ResolvedRule("Impact", CoreRuleCatalog.Impact,
                new RuleArgument[] { new RuleArgument.Int(2) }));

            var spear = new Weapon("Spear", rangeInches: 0f, attacks: 1, armorPenetration: 0);
            var defenderModels = new List<DataBinding<ModelData>>();
            foreach (float x in new[] { 21.5f, 22.6f })
            {
                foreach (float z in new[] { 18.9f, 20f, 21.1f })
                {
                    var model = new ModelData(0.5f, new List<Weapon> { spear }, new Position(x, z), _store);
                    defenderModels.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
                }
            }
            var defenderUnit = new UnitData(defenderPlayer, "Defender", quality: 4, defense: 5,
                modelBindings: defenderModels);
            DataBinding<UnitData> defender = _store.GetDataBinding<UnitData>(_store.Create(defenderUnit));

            _store.Create(new ArmyData(chargerPlayer, new List<DataBinding<UnitData>> { charger }));
            _store.Create(new ArmyData(defenderPlayer, new List<DataBinding<UnitData>> { defender }));
            return (charger, defender);
        }

        // The FIRST roll has exactly `ones` 1s (the rest 2s) - the activation test, which is the first thing an
        // activation rolls; every later roll lands every die on `face`.
        private sealed class FirstRollOnesThenFaceDiceRoller : IDiceRoller
        {
            private readonly FixedOnesDiceRoller _first;
            private readonly FixedFaceDiceRoller _rest;
            private bool _rolled;

            public FirstRollOnesThenFaceDiceRoller(int ones, int face)
            {
                _first = new FixedOnesDiceRoller(ones);
                _rest = new FixedFaceDiceRoller(face);
            }

            public IDiceResults Roll(int sideCount, float rollCount)
            {
                if (_rolled) return _rest.Roll(sideCount, rollCount);
                _rolled = true;
                return _first.Roll(sideCount, rollCount);
            }
        }

        // Answers one activation: action menus from the reply queue (Pass once it is empty, or the first option
        // when Pass is not offered), a straight move of MoveInches in +x for every living model, and every melee
        // prompt (the first defender, the first weapon, always strike back, auto-filled wounds, no consolidation).
        private sealed class ActivationRequester : IPlayerRequestByID
        {
            private readonly Queue<string> _replies;

            public ActivationRequester(params string[] replies) => _replies = new Queue<string>(replies);

            public float MoveInches { get; set; } = 8f;
            public RecordingPresentationSink? Sink { get; set; }
            public int ActionMenuCount { get; private set; }
            public List<bool> MenuAllowCancel { get; } = new List<bool>();

            /// <summary> Whether a Dangerous Terrain dice row had already been presented when the first menu came. </summary>
            public bool FirstRequestWasMenuAfterDice { get; private set; }

            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply>
            {
                switch (request)
                {
                    case ChooseActionRequest menu:
                        if (ActionMenuCount == 0)
                        {
                            FirstRequestWasMenuAfterDice = Sink != null
                                && Sink.Beats.OfType<DiceRolledBeat>().Any(b => b.Label == "Dangerous Terrain");
                        }
                        ActionMenuCount++;
                        MenuAllowCancel.Add(menu.AllowCancel);
                        string reply = _replies.Count > 0 && menu.ValidOptions.Contains(_replies.Peek())
                            ? _replies.Dequeue()
                            : menu.ValidOptions.Contains(ChooseActionStage.PASS_CHOICE_NAME)
                                ? ChooseActionStage.PASS_CHOICE_NAME
                                : menu.ValidOptions[0];
                        return Reply<TReply>(reply);

                    case DefineMovementPathRequest move:
                        List<ModelMoveEntry> entries = move.UnitDataBinding.GetValue().ModelBindings
                            .Where(m => m.GetValue().GetIsAlive())
                            .Select(m => new ModelMoveEntry(m, new List<Position>
                            {
                                new Position(m.GetValue().Position.x + MoveInches, m.GetValue().Position.z),
                            }))
                            .ToList();
                        return Reply<TReply>(new Selected<List<ModelMoveEntry>>(entries));

                    case CancellableSelectionRequest<UnitData> defenderPick:
                        return Reply<TReply>(new Selected<DataBinding<UnitData>>(defenderPick.ValidOptions[0].Option));

                    case StringSelectionRequest weaponPick:
                        return Reply<TReply>(weaponPick.ValidOptions[0]);

                    case YesNoRequest:
                        return Reply<TReply>(true);

                    case AssignWoundsRequest woundRequest:
                        var assigned = new AssignWoundsResults(woundRequest.UnitReceivingWounds, woundRequest.Packets);
                        assigned.AutoFill();
                        return Reply<TReply>(assigned);

                    case ConsolidationMoveRequest:
                        return Reply<TReply>(new List<ModelMoveEntry>());
                }

                throw new InvalidOperationException("Unexpected request type: " + request.GetType());
            }

            private static Task<TReply> Reply<TReply>(object reply) => Task.FromResult((TReply)reply);
        }
    }
}
