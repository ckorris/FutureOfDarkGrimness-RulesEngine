using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // Pins the contained fixes from the 2026-07 special-rules audit (SpecialRulesAudit.md in the
    // superproject): duration-aware token merging/consumption, take-the-best multiplier sinks,
    // Effect.ConsumeToken's operation emission, and PostCombatMoveGate's budget-spent path applying
    // unrelated token ops. Each test targets exactly one audit finding.
    [TestFixture]
    public class AuditHardeningTests
    {
        private GameDataStore _store = null!;
        private PlayerID _player;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _player = new PlayerID(Guid.NewGuid());
        }

        // ── LAT-2: same-delta buffs with different durations must not merge ─────────────────────

        [Test]
        public void AddToken_SamePayloadDifferentClearTrigger_KeepsSeparateEntries()
        {
            var container = new TokenContainer();
            container.AddToken(new Token(TokenType.HitRollModifier, 1,
                new TokenClearTrigger.FirstTrigger(), Payload: new TokenPayload.StatModifier(1)));
            container.AddToken(new Token(TokenType.HitRollModifier, 1,
                new TokenClearTrigger.RoundEnd(), Payload: new TokenPayload.StatModifier(1)));

            Assert.That(container.GetAllTokens(TokenType.HitRollModifier).Count(), Is.EqualTo(2),
                "a 'next roll' and a 'this round' buff of the same delta are different tokens; merging " +
                "would give one of them the other's lifetime");
        }

        // ── LAT-3 (#033 edge): spending a one-shot grant must not drain a duration grant ────────

        [Test]
        public void ConsumeNet_MixedOneShotAndDurationGrants_ConsumesOnlyTheOneShot()
        {
            DataBinding<UnitData> unit = MakeUnit();

            // Duration grant inserted FIRST, so the old type-only FIFO drain would have hit it.
            unit.GetValue().Tokens.AddToken(new Token(TokenType.HitRollModifier, 1,
                new TokenClearTrigger.RoundEnd(), Payload: new TokenPayload.StatModifier(2)));
            unit.GetValue().Tokens.AddToken(new Token(TokenType.HitRollModifier, 1,
                new TokenClearTrigger.FirstTrigger(), Payload: new TokenPayload.StatModifier(1)));

            Assert.That(GrantedRollModifiers.ConsumeNet(unit.GetValue(), ERollKind.Hit), Is.EqualTo(3),
                "both grants apply to the roll that spends the one-shot");
            Assert.That(GrantedRollModifiers.ConsumeNet(unit.GetValue(), ERollKind.Hit), Is.EqualTo(2),
                "the one-shot is spent; the duration grant must survive untouched");
        }

        [Test]
        public void RemoveFirstTriggerTokens_LeavesDurationEntriesAlone()
        {
            var container = new TokenContainer();
            container.AddToken(new Token(TokenType.HitRollModifier, 1,
                new TokenClearTrigger.RoundEnd(), Payload: new TokenPayload.StatModifier(2)));
            container.AddToken(new Token(TokenType.HitRollModifier, 1,
                new TokenClearTrigger.FirstTrigger(), Payload: new TokenPayload.StatModifier(1)));

            int removed = container.RemoveFirstTriggerTokens(TokenType.HitRollModifier, 1);

            Assert.That(removed, Is.EqualTo(1));
            Token remaining = container.GetAllTokens(TokenType.HitRollModifier).Single();
            Assert.That(remaining.ClearTrigger, Is.TypeOf<TokenClearTrigger.RoundEnd>(),
                "only the one-shot entry is spendable here");
        }

        // ── SYS-6: parallel multiplier sources take the best, they don't compound ───────────────

        [Test]
        public void HitMultiplierSink_TwoSources_KeepsHighest()
        {
            var sink = new HitMultiplierSink();
            sink.Multiply(2);
            sink.Multiply(3);
            sink.Multiply(2);

            Assert.That(sink.NetMultiplier, Is.EqualTo(3),
                "two multiplier rules on one attack take-the-best like the sibling sinks, not x6");
        }

        [Test]
        public void WoundModifierSink_TwoSources_KeepsHighest()
        {
            var sink = new WoundModifierSink();
            sink.Multiply(3);
            sink.Multiply(2);

            Assert.That(sink.NetMultiplier, Is.EqualTo(3));
        }

        // ── LAT-7: Effect.ConsumeToken is applyable (was NotImplementedException) ───────────────

        [Test]
        public void ConsumeTokenEffect_EmitsConsumeOperationForBearer()
        {
            DataBinding<UnitData> unit = MakeUnit();
            var effect = new Effect.ConsumeToken(TokenType.SpellTokens, 2);
            var operations = new List<RuleOperation>();

            effect.Apply(new RuleInvocation(Hook: null, Bearer: unit.GetValue(),
                Arguments: Array.Empty<RuleArgument>()), operations);

            RuleOperation.ConsumeTokensFromUnit consume =
                (RuleOperation.ConsumeTokensFromUnit)operations.Single();
            Assert.That(consume.Unit, Is.SameAs(unit.GetValue()));
            Assert.That(consume.TType, Is.EqualTo(TokenType.SpellTokens));
            Assert.That(consume.Count, Is.EqualTo(2));
        }

        // ── LAT-4: budget-spent PostCombatMoveGate still applies unrelated token ops ────────────

        [Test]
        public async Task PostCombatMoveGate_BudgetSpent_StillAppliesTokenOps()
        {
            DataBinding<UnitData> unit = MakeUnit();
            UnitData unitData = unit.GetValue();
            unitData.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.PostCombatMoveUsed));

            Token marker = new Token(TokenType.SpellTokens, 1, new TokenClearTrigger.ManualOnly());
            var operations = new List<RuleOperation>
            {
                new RuleOperation.GrantTokenToUnit(unitData, marker),
                new RuleOperation.InvokeTriggeredMove(unitData, 3f, IsOptional: true),
            };

            var ctx = new TestGameContext(_store, new FixedDiceRoller(4));
            await PostCombatMoveGate.OfferIfAvailable(ctx, unitData, operations);

            Assert.That(unitData.Tokens.HasToken(TokenType.SpellTokens), Is.True,
                "a non-move rule's token op sharing the hook must apply even when the move budget is spent");
        }

        // ── LAT-5/LAT-6: the unit-destroyed seam clears OwnerDestroyed marks and fires the hook ─

        [Test]
        public async Task NotifyUnitDestroyed_ClearsOwnerDestroyedMarks_AndFiresHookForKiller()
        {
            DataBinding<UnitData> victim = MakeUnit();
            DataBinding<UnitData> killer = MakeUnit();
            DataBinding<UnitData> bystander = MakeUnit();

            // The victim placed an OwnerDestroyed mark on the bystander (an "Unstoppable Mark" shape).
            bystander.GetValue().Tokens.AddToken(new Token(TokenType.SpellTokens, 1,
                new TokenClearTrigger.OwnerDestroyed(),
                OwnerUnitID: victim.GetValue().ID));

            // The killer bears a rule that fires on Shooting_OnUnitDestroyed (Piercing-Frenzy shape).
            TokenType reward = new TokenType("TestKillReward");
            var onKill = new SpecialRuleDefinition("Test Kill Reward",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnUnitDestroyed, new Condition.Always(),
                        new Effect.GrantToken(reward, new ValueSource.Literal(1),
                            new TokenClearTrigger.ManualOnly()),
                        ELifetime.UntilEndOfGame),
                },
                Array.Empty<ActivatedAbility>());
            killer.GetValue().AttachRuleDefinition(new ResolvedRule("Test Kill Reward", onKill));

            var ctx = new TestGameContext(_store, new FixedDiceRoller(4));
            await UnitDestructionNotifier.NotifyUnitDestroyed(ctx, victim.GetValue(), killer.GetValue());

            Assert.That(bystander.GetValue().Tokens.HasToken(TokenType.SpellTokens), Is.False,
                "the dead placer's OwnerDestroyed mark must clear from the bystander");
            Assert.That(killer.GetValue().Tokens.HasToken(reward), Is.True,
                "the killer's on-unit-destroyed rule must fire through the seam");
        }

        private DataBinding<UnitData> MakeUnit()
        {
            var model = new ModelData(0.5f, new List<Weapon>(), new Position(0f, 0f), _store);
            var modelBindings = new List<DataBinding<ModelData>>
            {
                _store.GetDataBinding<ModelData>(_store.Create(model)),
            };
            var unit = new UnitData(_player, "Audit Unit", quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(_player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }
    }
}
