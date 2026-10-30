using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Presentation.Beats;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Tests.RulesHarness;
using NUnit.Framework;

namespace FDG.Tests
{
    // #427 - OPR 3.6 Spell Amplifier (new in 3.6): "Gets +1 to casting rolls." Printed on a weapon (a Caster
    // Staff), so it is weapon-scoped; one passive at Casting_OnSpellCastAttempt, folded into the cast threshold by
    // CastRollRules from the REAL CastSpellStage (the SpellConduitRuleIntegrationTests setup: a Foe-affinity
    // debuff spell whose landing is the observable). #429 builds its other "+1 to casting rolls" rules on the
    // same hook.
    [TestFixture]
    public class SpellAmplifierRuleIntegrationTests
    {
        private static readonly Position CasterAt = new Position(10f, 10f);
        private static readonly Position EnemyAt = new Position(10f, 20f);

        private GameDataStore _store = null!;
        private PlayerID _player;
        private PlayerID _enemy;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _player = new PlayerID(System.Guid.NewGuid());
            _enemy = new PlayerID(System.Guid.NewGuid());
        }

        // --- the roll --------------------------------------------------------------------------------------

        [Test]
        public async Task ACasterWithAnAmplifiedStaff_CastsOnAThree()
        {
            // Every die comes up 3. Base 4+ fails; Spell Amplifier's +1 shifts the threshold to 3+.
            var requester = new RecordingCastRequester();
            var ctx = new TriggeredMoveTestContext(_store, requester, new FixedFaceDiceRoller(3));
            DataBinding<UnitData> caster = MakeCaster(withAmplifiedStaff: 1);
            DataBinding<UnitData> target = MakeEnemy();

            await RunCast(ctx, caster);

            Assert.That(target.GetValue().Tokens.GetAllTokens(TokenType.RuleGrant), Is.Not.Empty,
                "rolled 3, needed 3+ (base 4+, Spell Amplifier +1) - without it this cast fails.");
        }

        [Test]
        public async Task WithoutAnAmplifier_TheSameThreeFails()
        {
            var ctx = new TriggeredMoveTestContext(_store, new RecordingCastRequester(), new FixedFaceDiceRoller(3));
            DataBinding<UnitData> caster = MakeCaster(withAmplifiedStaff: 0);
            DataBinding<UnitData> target = MakeEnemy();

            await RunCast(ctx, caster);

            Assert.That(target.GetValue().Tokens.GetAllTokens(TokenType.RuleGrant), Is.Empty, "control: 4+ needed");
        }

        [Test]
        public async Task TheRuleCarriedOnTheUnitItself_AlsoCounts()
        {
            // "weapon-scoped AND unit-scoped": a unit-level copy (what a wargear bundle, an aura grant or the
            // army-load re-homing amounts to) reaches the same fold as a weapon-held one.
            var ctx = new TriggeredMoveTestContext(_store, new RecordingCastRequester(), new FixedFaceDiceRoller(3));
            DataBinding<UnitData> caster = MakeCaster(withAmplifiedStaff: 0);
            caster.GetValue().AttachRuleDefinition(new ResolvedRule("Spell Amplifier", CoreRuleCatalog.SpellAmplifier));
            DataBinding<UnitData> target = MakeEnemy();

            await RunCast(ctx, caster);

            Assert.That(target.GetValue().Tokens.GetAllTokens(TokenType.RuleGrant), Is.Not.Empty);
        }

        [Test]
        public async Task TwoAmplifiedStaffs_AreStillPlusOne()
        {
            // "Multiple instances of the same rule do not stack": rolled 2 would succeed at +2 (2+), and does not.
            var ctx = new TriggeredMoveTestContext(_store, new RecordingCastRequester(), new FixedFaceDiceRoller(2));
            DataBinding<UnitData> caster = MakeCaster(withAmplifiedStaff: 2);
            DataBinding<UnitData> target = MakeEnemy();

            await RunCast(ctx, caster);

            Assert.That(target.GetValue().Tokens.GetAllTokens(TokenType.RuleGrant), Is.Empty,
                "two staffs are one +1: 3+ needed, a 2 fails");
        }

        [Test]
        public async Task TheNaturalOneStillFails_HoweverManyBonusesStackOnTop()
        {
            // The cast roll's shared clamp ([2, 6]) holds with the new modifier in the sum: Amplifier +1 and a
            // granted Casting Buff +3 would be a 0+ unclamped; a natural 1 must still fail, a 2 must pass.
            foreach ((int face, bool lands) in new[] { (1, false), (2, true) })
            {
                SetUp();
                var ctx = new TriggeredMoveTestContext(_store, new RecordingCastRequester(), new FixedFaceDiceRoller(face));
                DataBinding<UnitData> caster = MakeCaster(withAmplifiedStaff: 1);
                caster.GetValue().Tokens.AddToken(new Token(RollModifierTokens.TypeFor(ERollKind.Cast), 1,
                    new TokenClearTrigger.ManualOnly(), Payload: new TokenPayload.StatModifier(3)));
                DataBinding<UnitData> target = MakeEnemy();

                await RunCast(ctx, caster);

                Assert.That(target.GetValue().Tokens.GetAllTokens(TokenType.RuleGrant).Any(), Is.EqualTo(lands),
                    $"face {face}");
            }
        }

        [Test]
        public async Task ADeadModelsStaff_NoLongerAmplifies()
        {
            var ctx = new TriggeredMoveTestContext(_store, new RecordingCastRequester(), new FixedFaceDiceRoller(3));
            DataBinding<UnitData> caster = MakeCaster(withAmplifiedStaff: 0, extraStaffBearer: true);
            ModelData staffBearer = caster.GetValue().ModelBindings[1].GetValue();
            staffBearer.DealWounds(staffBearer.TotalWounds);
            DataBinding<UnitData> target = MakeEnemy();

            await RunCast(ctx, caster);

            Assert.That(target.GetValue().Tokens.GetAllTokens(TokenType.RuleGrant), Is.Empty,
                "the staff-bearer is dead, so its Spell Amplifier is gone: base 4+ against a 3");
        }

        // --- #429: one sum, one clamp ----------------------------------------------------------------------
        // 3.6 patch notes: a Caster Staff's Spell Amplifier makes spells "go off on 3+, or on 2+ with one spell
        // token" - plain modifier arithmetic. Every cast-roll term (rules, relay, self-boost, boosters/hinderers,
        // granted tokens) is summed once and clamped once in CastSpellStage.

        [Test]
        public async Task AmplifierPlusOne_AndOneEnemyHinderToken_NetFourPlus()
        {
            // +1 (Spell Amplifier) - 1 (an enemy Caster's token) = 0: back to the plain 4+. A 3 fails, a 4 lands.
            foreach ((int face, bool lands) in new[] { (3, false), (4, true) })
            {
                SetUp();
                var requester = new CannedAssistRequester(tokensPerAssister: 1);
                var ctx = new TriggeredMoveTestContext(_store, requester, new FixedFaceDiceRoller(face));
                DataBinding<UnitData> caster = MakeCaster(withAmplifiedStaff: 1);
                (DataBinding<UnitData> target, DataBinding<UnitData> hinderer) = MakeEnemyWithHinderer(hinderTokens: 1);

                await RunCast(ctx, caster);

                Assert.That(requester.AssistPromptCount, Is.EqualTo(1), $"face {face}: the enemy Caster was asked");
                Assert.That(hinderer.GetValue().Tokens.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(0),
                    $"face {face}: and spent its one token");
                Assert.That(target.GetValue().Tokens.GetAllTokens(TokenType.RuleGrant).Any(), Is.EqualTo(lands),
                    $"face {face}: rules +1 and assists -1 net 4+");
            }
        }

        [Test]
        public async Task AmplifierAndHinder_TheBannerShowsBothTerms()
        {
            var sink = new RecordingPresentationSink();
            var ctx = new TriggeredMoveTestContext(_store, new CannedAssistRequester(tokensPerAssister: 1),
                new FixedFaceDiceRoller(4), presentationSink: sink);
            DataBinding<UnitData> caster = MakeCaster(withAmplifiedStaff: 1);
            MakeEnemyWithHinderer(hinderTokens: 1);

            await RunCast(ctx, caster);

            Assert.That(sink.Beats.OfType<BannerBeat>().Select(b => b.BannerText),
                Has.Some.Contains("rolled 4, needed 4+ (base 4+, rules +1, assists -1)"),
                "the log line shows every term that moved the threshold, not just the result");
        }

        [Test]
        public async Task AmplifierAndOneSelfBoostToken_CastOnATwo()
        {
            // "on 2+ with one spell token": 4 - 1 (Amplifier) - 1 (self-boost) = 2+.
            var ctx = new TriggeredMoveTestContext(_store, new CannedAssistRequester(tokensPerAssister: 0, boostTokens: 1),
                new FixedFaceDiceRoller(2));
            DataBinding<UnitData> caster = MakeCaster(withAmplifiedStaff: 1);
            DataBinding<UnitData> target = MakeEnemy();

            await RunCast(ctx, caster);

            Assert.That(target.GetValue().Tokens.GetAllTokens(TokenType.RuleGrant), Is.Not.Empty);
        }

        [Test]
        public async Task ThePickersBaseThreshold_CarriesTheCastersGrantedTokensToo_WithoutSpendingThem()
        {
            // Spell Amplifier +1 and a Casting Debuff "-1 to casting rolls once" on the caster: its own threshold is
            // back to 4+, and the picker must say so (GUI "Roll needed", CLI boost cap). Cancelling the pick proves
            // the read is a peek: the one-shot debuff is still there for the real roll.
            var requester = new CancellingSpellRequester();
            var ctx = new TriggeredMoveTestContext(_store, requester, new FixedFaceDiceRoller(3));
            DataBinding<UnitData> caster = MakeCaster(withAmplifiedStaff: 1);
            caster.GetValue().Tokens.AddToken(new Token(RollModifierTokens.TypeFor(ERollKind.Cast), 1,
                new TokenClearTrigger.FirstTrigger(), Payload: new TokenPayload.StatModifier(-1)));
            MakeEnemy();

            await RunCast(ctx, caster);

            Assert.That(requester.SpellRequest!.BaseThreshold, Is.EqualTo(4), "4+ less +1 (rules) less -1 (granted)");
            Assert.That(requester.SpellRequest.BaseThresholdNote, Is.EqualTo("base 4+, rules +1, granted -1"));
            Assert.That(caster.GetValue().Tokens.HasToken(RollModifierTokens.TypeFor(ERollKind.Cast)), Is.True,
                "the picker only peeks - the debuff is spent by the roll, never by browsing the menu");
        }

        [Test]
        public async Task AnUnmodifiedCaster_HasNoBaseThresholdNote()
        {
            var requester = new RecordingCastRequester();
            DataBinding<UnitData> caster = MakeCaster(withAmplifiedStaff: 0);
            MakeEnemy();

            await RunCast(new TriggeredMoveTestContext(_store, requester, new FixedFaceDiceRoller(3)), caster);

            Assert.That(requester.SpellRequest!.BaseThreshold, Is.EqualTo(4));
            Assert.That(requester.SpellRequest.BaseThresholdNote, Is.Empty, "a plain 4+ needs no explanation");
        }

        // --- the player is told ----------------------------------------------------------------------------

        [Test]
        public async Task TheSpellPicker_ShowsTheAmplifiedThreshold()
        {
            var amplified = new RecordingCastRequester();
            DataBinding<UnitData> amplifiedCaster = MakeCaster(withAmplifiedStaff: 1);
            MakeEnemy();
            await RunCast(new TriggeredMoveTestContext(_store, amplified, new FixedFaceDiceRoller(3)), amplifiedCaster);
            Assert.That(amplified.SpellRequest!.BaseThreshold, Is.EqualTo(3),
                "the picker's 'roll needed' starts from the caster's own threshold: 4+ less the +1");

            SetUp();
            var plain = new RecordingCastRequester();
            DataBinding<UnitData> plainCaster = MakeCaster(withAmplifiedStaff: 0);
            MakeEnemy();
            await RunCast(new TriggeredMoveTestContext(_store, plain, new FixedFaceDiceRoller(3)), plainCaster);
            Assert.That(plain.SpellRequest!.BaseThreshold, Is.EqualTo(4));
        }

        [Test]
        public async Task TheCastBanner_NamesTheRulesBonus()
        {
            var sink = new RecordingPresentationSink();
            var ctx = new TriggeredMoveTestContext(_store, new RecordingCastRequester(), new FixedFaceDiceRoller(3),
                presentationSink: sink);
            DataBinding<UnitData> caster = MakeCaster(withAmplifiedStaff: 1);
            MakeEnemy();

            await RunCast(ctx, caster);

            Assert.That(sink.Beats.OfType<BannerBeat>().Select(b => b.BannerText),
                Has.Some.Contains("needed 3+ (base 4+, rules +1)"),
                "the roll breakdown says where the 3+ came from");
        }

        [Test]
        public void CastRollRules_NarratesALiveFold_AndStaysSilentForAPreview()
        {
            var log = new CapturingLog();
            var harness = new TestRuleHarness(log: log);
            UnitData caster = BuildCasterWithStaff();

            Assert.That(CastRollRules.Net(caster, harness.Evaluator, live: false), Is.EqualTo(1));
            Assert.That(log.Lines, Is.Empty, "a preview is a read-only query");

            Assert.That(CastRollRules.Net(caster, harness.Evaluator, live: true), Is.EqualTo(1));
            Assert.That(log.Lines, Has.Some.Contains("Spell Amplifier").And.Contains("Cast"),
                "the live fold logs 'X's Staff's Spell Amplifier added +1 to Cast rolls'");
        }

        [Test]
        public void SpellAmplifier_IsCatalogued_WeaponScoped_AndCarriesTheCore360Wording()
        {
            Assert.That(CoreRuleCatalog.All.Any(r => r.Name == "Spell Amplifier"), Is.True);
            Assert.That(CoreRuleCatalog.CreateResolver().TryResolve("Spell Amplifier", out _), Is.True);
            Assert.That(CoreRuleCatalog.SpellAmplifier.Scope, Is.EqualTo(ERuleScope.Weapon));
            Assert.That(CoreRuleCatalog.SpellAmplifier.Description, Is.EqualTo("Gets +1 to casting rolls."));
        }

        // --- helpers ---------------------------------------------------------------------------------------

        private async Task RunCast(TriggeredMoveTestContext ctx, DataBinding<UnitData> caster)
        {
            var unitCtx = new UnitActionContext(ctx, caster);
            unitCtx.Reset(caster);
            var stage = new CastSpellStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("OnFinished");
            await stage.Enter(unitCtx);
        }

        private static Weapon Staff()
        {
            var staff = new Weapon("Caster Staff", rangeInches: 0f, attacks: 1, armorPenetration: 0);
            staff.AttachRuleDefinition(new ResolvedRule("Spell Amplifier", CoreRuleCatalog.SpellAmplifier));
            return staff;
        }

        private UnitData BuildCasterWithStaff()
        {
            var model = new ModelData(0.5f, new List<Weapon> { Staff() }, CasterAt, _store);
            var unit = new UnitData(_player, "Psy-Seer", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { _store.GetDataBinding<ModelData>(_store.Create(model)) });
            _store.Create(unit);
            return unit;
        }

        // One Caster(2) unit with the spell army wiring; `withAmplifiedStaff` models carry a Caster Staff.
        // `extraStaffBearer` adds a second model (index 1) holding the only staff, to be killed by a test.
        private DataBinding<UnitData> MakeCaster(int withAmplifiedStaff, bool extraStaffBearer = false)
        {
            var bindings = new List<DataBinding<ModelData>>();
            int staffs = withAmplifiedStaff;
            do
            {
                var weapons = new List<Weapon>();
                if (staffs > 0) { weapons.Add(Staff()); staffs--; }
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(
                    new ModelData(0.5f, weapons, CasterAt, _store))));
            }
            while (staffs > 0);

            if (extraStaffBearer)
            {
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(
                    new ModelData(0.5f, new List<Weapon> { Staff() }, CasterAt, _store))));
            }

            var unit = new UnitData(_player, "Psy-Seer", quality: 4, defense: 4, modelBindings: bindings);
            unit.AttachRuleDefinition(new ResolvedRule("Caster", CoreRuleCatalog.Caster,
                new RuleArgument[] { new RuleArgument.Int(2) }));
            unit.Tokens.AddToken(new Token(TokenType.SpellTokens, 3, new TokenClearTrigger.ManualOnly()));
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));

            var army = new ArmyData(_player, new List<DataBinding<UnitData>> { binding });
            army.SetSpells(new[] { DebuffSpell("Hex", threshold: 1) });
            _store.Create(army);
            return binding;
        }

        private DataBinding<UnitData> MakeEnemy()
        {
            var model = new ModelData(0.5f, new List<Weapon>(), EnemyAt, _store);
            var unit = new UnitData(_enemy, "Grunts", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { _store.GetDataBinding<ModelData>(_store.Create(model)) });
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(_enemy, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        // The usual target plus an enemy Caster(1) beside it holding `hinderTokens`, both in one enemy army,
        // within 18" of the caster.
        private (DataBinding<UnitData> target, DataBinding<UnitData> hinderer) MakeEnemyWithHinderer(int hinderTokens)
        {
            DataBinding<UnitData> Unit(string name, Position at)
            {
                var model = new ModelData(0.5f, new List<Weapon>(), at, _store);
                var unit = new UnitData(_enemy, name, quality: 4, defense: 4,
                    modelBindings: new List<DataBinding<ModelData>> { _store.GetDataBinding<ModelData>(_store.Create(model)) });
                return _store.GetDataBinding<UnitData>(_store.Create(unit));
            }

            DataBinding<UnitData> target = Unit("Grunts", EnemyAt);
            DataBinding<UnitData> hinderer = Unit("Hex-Witch", new Position(14f, 20f));
            hinderer.GetValue().AttachRuleDefinition(new ResolvedRule("Caster", CoreRuleCatalog.Caster,
                new RuleArgument[] { new RuleArgument.Int(1) }));
            hinderer.GetValue().Tokens.AddToken(new Token(TokenType.SpellTokens, hinderTokens,
                new TokenClearTrigger.ManualOnly()));
            _store.Create(new ArmyData(_enemy, new List<DataBinding<UnitData>> { target, hinderer }));
            return (target, hinderer);
        }

        // Records the spell picker's request and cancels it - nothing is spent and no roll is made.
        private sealed class CancellingSpellRequester : IPlayerRequestByID
        {
            public ChooseSpellRequest? SpellRequest { get; private set; }

            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply>
            {
                if (request is ChooseSpellRequest pick)
                {
                    SpellRequest = pick;
                    return Task.FromResult((TReply)(object)ChooseSpellReply.Cancel);
                }

                throw new System.InvalidOperationException("Unexpected request: " + request.GetType());
            }
        }

        // Foe-affinity, non-damage: the debuff lands as a RuleGrant token on the target, so "did the cast
        // succeed" is one assertion with no wound pipeline in the way.
        private static RuntimeSpell DebuffSpell(string name, int threshold) =>
            new RuntimeSpell(
                new SpellDefinition(name, threshold,
                    new TargetSelector(18f, 1, 1, ETargetAffinity.Foe, RequireLineOfSight: false),
                    new Effect.AddRule("Slow", ELifetime.NextTrigger)),
                System.Array.Empty<ResolvedRule>());

        private sealed class CapturingLog : ITextOutput
        {
            public List<string> Lines { get; } = new();

            public void Log(string message, TextColor? color = null) => Lines.Add(message);

            public void LogDebug(string message, TextColor? color = null) { }
        }
    }
}
