using System.Collections.Generic;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #197 P23's Spell Accumulator (7 refs):
    //
    //   "Gets X accumulator tokens at the start of each round, but can't hold more than 6 tokens at once.
    //    Casters from other friendly units within 12" may spend this model's accumulator tokens as if they
    //    were their own spell tokens. Friendly casters may only use this rule if this unit isn't Shaken."
    //
    // Three claims, and the tests below are grouped by them: the pool fills and caps, WHO may draw on it,
    // and what "as if they were their own spell tokens" means at the moment of spending.
    //
    // The pool is its own token type rather than plain SpellTokens, and that is load-bearing rather than
    // tidy-minded: the corpus puts the accumulator upgrade on units that are themselves casters, and the
    // rule says OTHER friendly units - one shared type would let the holder spend its own pool.
    //
    // Who may lend is asked at Lifecycle_OnCapabilityQuery, so "only if this unit isn't Shaken" is a plain
    // Condition on that entry (TheCapabilityIsLive below) rather than a special case inside the cast stage.
    [TestFixture]
    public class SpellAccumulatorRuleIntegrationTests
    {
        private const float LendRangeInches = 12f;

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

        /// <summary>Spell Accumulator(X) as the shipped supplement authors it: a capped round-start grant
        /// into its own pool, plus the capability entry that opens that pool to nearby friendly casters and
        /// closes it while the holder is Shaken.</summary>
        private static SpecialRuleDefinition SpellAccumulator() => new("Spell Accumulator",
            new[]
            {
                new HookEntry(EHookID.Round_OnRoundStart, new Condition.Always(),
                    new Effect.GrantToken(TokenType.AccumulatorTokens, new ValueSource.Arg(0),
                        new TokenClearTrigger.ManualOnly(), MaxTotal: 6),
                    ELifetime.UntilEndOfGame),
                new HookEntry(EHookID.Lifecycle_OnCapabilityQuery,
                    new Condition.Not(new Condition.TokenPresent(TokenType.Shaken)),
                    new Effect.EnableSpellLending(TokenType.AccumulatorTokens, LendRangeInches),
                    ELifetime.UntilEndOfGame),
            },
            System.Array.Empty<ActivatedAbility>());

        // --- the pool ------------------------------------------------------------------------------

        [Test]
        public async Task RoundStart_FillsTheLendingPool_AndNotTheSpellPool()
        {
            DataBinding<UnitData> accumulator = MakeUnit("Change Boon", _player, new Position(10f, 10f),
                accumulatorRating: 2);

            await RunRoundStart(roundCount: 1);

            Assert.That(accumulator.GetValue().Tokens.GetTokenCount(TokenType.AccumulatorTokens),
                Is.EqualTo(2), "'gets X accumulator tokens at the start of each round'.");
            Assert.That(accumulator.GetValue().Tokens.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(0),
                "the pool is not spell tokens - the holder gains nothing it can spend itself.");
        }

        [Test]
        public async Task ThePool_CarriesOverBetweenRounds_ButNeverExceedsSix()
        {
            DataBinding<UnitData> accumulator = MakeUnit("Change Boon", _player, new Position(10f, 10f),
                accumulatorRating: 4);

            for (int round = 1; round <= 4; round++)
            {
                await RunRoundStart(round);
            }

            // 4 a round across 4 rounds is 16 uncapped. The cap rides the rule's own grant
            // (Effect.GrantToken.MaxTotal), not the engine's MAX_SPELL_TOKENS clamp, which is why it can
            // hold a different number from a caster's pool.
            Assert.That(accumulator.GetValue().Tokens.GetTokenCount(TokenType.AccumulatorTokens),
                Is.EqualTo(6), "'can't hold more than 6 tokens at once'.");
        }

        [Test]
        public async Task HoldingAPool_DoesNotMakeAUnitACaster()
        {
            DataBinding<UnitData> accumulator = MakeUnit("Change Boon", _player, new Position(10f, 10f),
                accumulatorRating: 3);

            await RunRoundStart(roundCount: 1);

            var ctx = new TriggeredMoveTestContext(_store, new NoRequestsRequester());
            Assert.That(SpellTargeting.IsCaster(ctx, accumulator.GetValue()), Is.False,
                "a full pool must never make its holder look like a caster - to the Cast action, to the " +
                "#103 assist scan, or to anything else that asks.");
        }

        // --- who may draw on it --------------------------------------------------------------------

        [Test]
        public void ANearbyFriendlyCaster_MaySpendThePool()
        {
            (TriggeredMoveTestContext ctx, IUnit caster, IUnit accumulator) = Pair(
                casterAt: new Position(10f, 10f), accumulatorAt: new Position(16f, 10f),
                casterTokens: 1, poolTokens: 4);

            Assert.That(Available(ctx, caster), Is.EqualTo(5),
                "'may spend this model's accumulator tokens as if they were their own spell tokens'.");
            Assert.That(accumulator.Tokens.GetTokenCount(TokenType.AccumulatorTokens), Is.EqualTo(4),
                "asking what is available spends nothing.");
        }

        [Test]
        public void TheAccumulatorItself_CannotSpendItsOwnPool()
        {
            // "Casters from OTHER friendly units". The corpus puts Change Boon on caster units, so this is
            // a live case, not a hypothetical - and it is the reason the pool is not plain spell tokens.
            (TriggeredMoveTestContext ctx, IUnit caster, IUnit accumulator) = Pair(
                casterAt: new Position(10f, 10f), accumulatorAt: new Position(16f, 10f),
                casterTokens: 1, poolTokens: 4);
            AttachCaster(accumulator, rating: 2);
            accumulator.Tokens.AddToken(new Token(TokenType.SpellTokens, 2, new TokenClearTrigger.ManualOnly()));

            Assert.That(Available(ctx, accumulator), Is.EqualTo(2),
                "its own 2 spell tokens and none of its own 4 accumulator tokens.");
            Assert.That(Available(ctx, caster), Is.EqualTo(5),
                "the other caster is unaffected - the pool is still lent outward.");
        }

        [Test]
        public void AnEnemyCaster_CannotSpendThePool()
        {
            (TriggeredMoveTestContext ctx, IUnit caster, _) = Pair(
                casterAt: new Position(10f, 10f), accumulatorAt: new Position(16f, 10f),
                casterTokens: 1, poolTokens: 4, accumulatorOwner: EnemyOwner);

            Assert.That(Available(ctx, caster), Is.EqualTo(1), "'from other FRIENDLY units'.");
        }

        [Test]
        public void BeyondTwelveInches_ThePoolIsOutOfReach()
        {
            // Base to base, matching every other range in the engine: 0.5" radii, so 13" between centres
            // is 12" apart exactly, and one more inch puts it out.
            (TriggeredMoveTestContext ctx, IUnit caster, _) = Pair(
                casterAt: new Position(10f, 10f), accumulatorAt: new Position(24f, 10f),
                casterTokens: 1, poolTokens: 4);

            Assert.That(Available(ctx, caster), Is.EqualTo(1), "'within 12 inches'.");
        }

        [Test]
        public void AShakenAccumulator_LendsNothing_AndRecoveringRestoresIt()
        {
            (TriggeredMoveTestContext ctx, IUnit caster, IUnit accumulator) = Pair(
                casterAt: new Position(10f, 10f), accumulatorAt: new Position(16f, 10f),
                casterTokens: 1, poolTokens: 4);

            accumulator.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.Shaken));
            Assert.That(Available(ctx, caster), Is.EqualTo(1),
                "'friendly casters may only use this rule if this unit isn't Shaken'.");

            accumulator.Tokens.RemoveTokens(TokenType.Shaken);
            Assert.That(Available(ctx, caster), Is.EqualTo(5),
                "the capability is re-asked every time, so the pool reopens the moment the unit recovers - " +
                "which is why the Shaken clause is a Condition on the entry and not stage code.");
        }

        [Test]
        public void ADestroyedAccumulator_LendsNothing()
        {
            (TriggeredMoveTestContext ctx, IUnit caster, IUnit accumulator) = Pair(
                casterAt: new Position(10f, 10f), accumulatorAt: new Position(16f, 10f),
                casterTokens: 1, poolTokens: 4);

            ((ModelData)accumulator.Models[0]).DealWounds(((ModelData)accumulator.Models[0]).TotalWounds);

            Assert.That(Available(ctx, caster), Is.EqualTo(1));
        }

        // --- spending ------------------------------------------------------------------------------

        [Test]
        public void OwnTokensAreSpentBeforeBorrowedOnes()
        {
            // A caster's own tokens are usable by nobody else, while the pool is shared with every friendly
            // caster in range - so spending the restricted resource first leaves the team the most options.
            (TriggeredMoveTestContext ctx, IUnit caster, IUnit accumulator) = Pair(
                casterAt: new Position(10f, 10f), accumulatorAt: new Position(16f, 10f),
                casterTokens: 2, poolTokens: 6);

            IReadOnlyList<SpellPurse.Loan> loans = SpellPurse.Spend(
                ctx.TableState, ctx.RuleEvaluator, caster, 3);

            Assert.That(caster.Tokens.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(0));
            Assert.That(accumulator.Tokens.GetTokenCount(TokenType.AccumulatorTokens), Is.EqualTo(5),
                "only the 1 the caster could not cover itself came out of the pool.");
            Assert.That(loans, Has.Count.EqualTo(1), "the borrowed part is reported back for the log line.");
            Assert.That(loans[0].Count, Is.EqualTo(1));
        }

        [Test]
        public void ASpendDrawsFromSeveralLenders_UntilItIsPaid()
        {
            (TriggeredMoveTestContext ctx, IUnit caster, IUnit first) = Pair(
                casterAt: new Position(10f, 10f), accumulatorAt: new Position(16f, 10f),
                casterTokens: 0, poolTokens: 2);
            IUnit second = MakeUnit("Second Boon", _player, new Position(14f, 10f), accumulatorRating: 1)
                .GetValue();
            second.Tokens.AddToken(new Token(TokenType.AccumulatorTokens, 3, new TokenClearTrigger.ManualOnly()));

            Assert.That(Available(ctx, caster), Is.EqualTo(5));

            SpellPurse.Spend(ctx.TableState, ctx.RuleEvaluator, caster, 4);

            Assert.That(first.Tokens.GetTokenCount(TokenType.AccumulatorTokens)
                + second.Tokens.GetTokenCount(TokenType.AccumulatorTokens), Is.EqualTo(1),
                "4 of the 5 tokens on offer across two pools were spent.");
        }

        [Test]
        public void OneCasterSpending_LeavesLessForTheNext()
        {
            // Two casters sharing one pool is the whole point of a shared resource, and the second must see
            // the first one's spending rather than a stale snapshot.
            (TriggeredMoveTestContext ctx, IUnit first, IUnit accumulator) = Pair(
                casterAt: new Position(10f, 10f), accumulatorAt: new Position(16f, 10f),
                casterTokens: 0, poolTokens: 4);
            IUnit second = MakeCaster("Second Caster", _player, new Position(18f, 10f), rating: 2, tokens: 0);

            Assert.That(Available(ctx, second), Is.EqualTo(4));

            SpellPurse.Spend(ctx.TableState, ctx.RuleEvaluator, first, 3);

            Assert.That(Available(ctx, second), Is.EqualTo(1));
            Assert.That(accumulator.Tokens.GetTokenCount(TokenType.AccumulatorTokens), Is.EqualTo(1));
        }

        // --- the stages ask the purse, not the unit's own pool ---------------------------------------
        //
        // Pinned separately from the SpellPurse tests above, which call it directly: without these,
        // reverting either stage to `unit.Tokens.GetTokenCount(SpellTokens)` would leave every test above
        // green while a borrowing caster silently lost the Cast action and the ability to pay for a spell.

        [Test]
        public async Task ChooseAction_OffersCast_WhenOnlyBorrowedTokensCanPayForIt()
        {
            var requester = new RecordingActionRequester("Pass");
            var ctx = new TriggeredMoveTestContext(_store, requester);

            // Friend affinity so the caster is its own legal target; Cast needs one.
            DataBinding<UnitData> caster = MakeCasterWithArmy(rating: 2, tokens: 0, new Position(10f, 10f),
                new[] { BuffSpell("Bless", threshold: 2) }, out ArmyData army);
            AddAccumulatorToArmy(army, new Position(16f, 10f), rating: 2, poolTokens: 2);

            UnitActionContext unitCtx = NewActivation(ctx, caster);
            var stage = new ChooseActionStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.ToReconcileEndOfActivation.Bind("Pass");
            await stage.Enter(unitCtx);

            Assert.That(requester.OfferedOptions, Contains.Item("Cast"),
                "the caster holds no spell tokens of its own but can pay the 2-token spell from the " +
                "accumulator 5\" away.");
        }

        [Test]
        public async Task CastSpellStage_PaysFromTheAccumulator_WhenTheCasterIsShort()
        {
            var ctx = new TriggeredMoveTestContext(_store, new AccumulatorCastRequester());

            DataBinding<UnitData> caster = MakeCasterWithArmy(rating: 2, tokens: 1, new Position(10f, 10f),
                new[] { BuffSpell("Bless", threshold: 3) }, out ArmyData army);
            IUnit accumulator = AddAccumulatorToArmy(army, new Position(16f, 10f), rating: 2, poolTokens: 4);

            UnitActionContext unitCtx = NewActivation(ctx, caster);
            var stage = new CastSpellStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("OnFinished");
            await stage.Enter(unitCtx);

            Assert.That(caster.GetValue().Tokens.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(0),
                "the caster's own token went first.");
            Assert.That(accumulator.Tokens.GetTokenCount(TokenType.AccumulatorTokens), Is.EqualTo(2),
                "the remaining 2 of the 3-token cost came out of the pool.");
        }

        // --- helpers ---------------------------------------------------------------------------------

        private const bool EnemyOwner = true;

        private static int Available(TriggeredMoveTestContext ctx, IUnit unit) =>
            SpellPurse.Available(ctx.TableState, ctx.RuleEvaluator, unit);

        /// <summary>A caster and an accumulator, each in its own single-unit army, plus a live context.</summary>
        private (TriggeredMoveTestContext ctx, IUnit caster, IUnit accumulator) Pair(
            Position casterAt, Position accumulatorAt, int casterTokens, int poolTokens,
            bool accumulatorOwner = false)
        {
            IUnit caster = MakeCaster("Psy-Seer", _player, casterAt, rating: 2, tokens: casterTokens);
            DataBinding<UnitData> accumulator = MakeUnit("Change Boon",
                accumulatorOwner == EnemyOwner ? _enemy : _player, accumulatorAt, accumulatorRating: 2);
            if (poolTokens > 0)
            {
                accumulator.GetValue().Tokens.AddToken(
                    new Token(TokenType.AccumulatorTokens, poolTokens, new TokenClearTrigger.ManualOnly()));
            }

            return (new TriggeredMoveTestContext(_store, new NoRequestsRequester()), caster,
                accumulator.GetValue());
        }

        private DataBinding<UnitData> MakeUnit(string name, PlayerID owner, Position pos,
            int? accumulatorRating)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), pos, _store);
            var bindings = new List<DataBinding<ModelData>>
            {
                _store.GetDataBinding<ModelData>(_store.Create(model)),
            };

            var unit = new UnitData(owner, name, quality: 4, defense: 4, modelBindings: bindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            if (accumulatorRating.HasValue)
            {
                AttachAccumulator(binding.GetValue(), accumulatorRating.Value);
            }

            _store.Create(new ArmyData(owner, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        private IUnit MakeCaster(string name, PlayerID owner, Position pos, int rating, int tokens)
        {
            DataBinding<UnitData> binding = MakeUnit(name, owner, pos, accumulatorRating: null);
            AttachCaster(binding.GetValue(), rating);
            if (tokens > 0)
            {
                binding.GetValue().Tokens.AddToken(
                    new Token(TokenType.SpellTokens, tokens, new TokenClearTrigger.ManualOnly()));
            }

            return binding.GetValue();
        }

        /// <summary>A caster whose army carries <paramref name="spells"/>, so the cast stages have something
        /// to offer; the army is handed back so an accumulator can join the same list.</summary>
        private DataBinding<UnitData> MakeCasterWithArmy(int rating, int tokens, Position pos,
            IReadOnlyList<RuntimeSpell> spells, out ArmyData army)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), pos, _store);
            var bindings = new List<DataBinding<ModelData>>
            {
                _store.GetDataBinding<ModelData>(_store.Create(model)),
            };

            var unit = new UnitData(_player, "Psy-Seer", quality: 4, defense: 4, modelBindings: bindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            AttachCaster(binding.GetValue(), rating);
            if (tokens > 0)
            {
                binding.GetValue().Tokens.AddToken(
                    new Token(TokenType.SpellTokens, tokens, new TokenClearTrigger.ManualOnly()));
            }

            army = new ArmyData(_player, new List<DataBinding<UnitData>> { binding });
            army.SetSpells(spells);
            _store.Create(army);
            return binding;
        }

        private IUnit AddAccumulatorToArmy(ArmyData army, Position pos, int rating, int poolTokens)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), pos, _store);
            var bindings = new List<DataBinding<ModelData>>
            {
                _store.GetDataBinding<ModelData>(_store.Create(model)),
            };

            var unit = new UnitData(_player, "Change Boon", quality: 4, defense: 4, modelBindings: bindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            AttachAccumulator(binding.GetValue(), rating);
            binding.GetValue().Tokens.AddToken(
                new Token(TokenType.AccumulatorTokens, poolTokens, new TokenClearTrigger.ManualOnly()));

            army.UnitBindings.Add(binding);
            return binding.GetValue();
        }

        private static void AttachAccumulator(IUnit unit, int rating) =>
            ((UnitData)unit).AttachRuleDefinition(new ResolvedRule("Spell Accumulator", SpellAccumulator(),
                new RuleArgument[] { new RuleArgument.Int(rating) }));

        private static void AttachCaster(IUnit unit, int rating) =>
            ((UnitData)unit).AttachRuleDefinition(new ResolvedRule("Caster", CoreRuleCatalog.Caster,
                new RuleArgument[] { new RuleArgument.Int(rating) }));

        private static RuntimeSpell BuffSpell(string name, int threshold) =>
            new RuntimeSpell(
                new SpellDefinition(name, threshold,
                    new TargetSelector(18f, 1, 1, ETargetAffinity.Friend, RequireLineOfSight: false),
                    new Effect.AddRule("Furious", ELifetime.NextTrigger)),
                System.Array.Empty<ResolvedRule>());

        private static UnitActionContext NewActivation(TriggeredMoveTestContext ctx,
            DataBinding<UnitData> unit)
        {
            var unitCtx = new UnitActionContext(ctx, unit);
            unitCtx.Reset(unit);
            return unitCtx;
        }

        private async Task RunRoundStart(int roundCount)
        {
            var ctx = new TriggeredMoveTestContext(_store, new NoRequestsRequester());
            var stage = new StartOfRoundExtraActionStage(ctx, new NoOpLayer<IMainPhaseContext>());
            stage.OnFinished.Bind("done");
            await stage.Enter(new TestMainPhaseContext(ctx, roundCount));
        }
    }

    // Picks the first castable spell and the first target, and declines every #103 assist - so the only
    // token movement in these tests is the cast's own cost.
    internal sealed class AccumulatorCastRequester : IPlayerRequestByID
    {
        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            switch (request)
            {
                case ChooseSpellRequest spellPick:
                    return Task.FromResult((TReply)(object)CannedSpellPick.FirstCastable(spellPick));
                case SelectionRequest<UnitData> targetPick:
                    return Task.FromResult((TReply)(object)targetPick.ValidOptions[0].Option);
                case CastAssistRequest:
                    return Task.FromResult((TReply)(object)0);
                default:
                    throw new System.InvalidOperationException("Unexpected request: " + request.GetType());
            }
        }
    }
}
