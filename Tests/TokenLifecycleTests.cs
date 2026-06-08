using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using FDG.StageResolution;
using NUnit.Framework;

namespace FDG.Tests
{
    // #042 cost-gate lifecycle: the two halves the once-per-X gates need that weren't applied in production.
    //  - OperationApplier grants/consumes the cost tokens the activated-ability dispatcher emits (which
    //    OperationExecutor, running only ExecutableOperations, never touched).
    //  - TokenClearService.ClearForHook clears ActivationEnd / RoundEnd markers at the matching hook, and
    //    ReconcileEndOfActivationStage drives it so a once-per-activation gate (Strafing) resets next turn.
    [TestFixture]
    public class TokenLifecycleTests
    {
        private static readonly TokenType Marker = new("AbilityUsed:Test");

        [Test]
        public void ClearForHook_ActivationEnd_RemovesActivationMarker_LeavesManual()
        {
            var container = new TokenContainer();
            container.AddToken(new Token(Marker, 1, new TokenClearTrigger.ActivationEnd()));
            var persistent = new TokenType("Persistent");
            container.AddToken(new Token(persistent, 1, new TokenClearTrigger.ManualOnly()));

            new TokenClearService().ClearForHook(EHookID.Activation_OnEndOfActivation,
                new ITokenContainer[] { container });

            Assert.That(container.HasToken(Marker), Is.False, "the activation-scoped marker clears at end of activation");
            Assert.That(container.HasToken(persistent), Is.True, "a ManualOnly token is untouched");
        }

        [Test]
        public void ClearForHook_RoundEnd_RemovesRoundMarker()
        {
            var container = new TokenContainer();
            container.AddToken(new Token(Marker, 1, new TokenClearTrigger.RoundEnd()));

            new TokenClearService().ClearForHook(EHookID.Round_OnRoundEnd, new ITokenContainer[] { container });

            Assert.That(container.HasToken(Marker), Is.False);
        }

        [Test]
        public void ClearForHook_NonMatchingHook_LeavesToken()
        {
            var container = new TokenContainer();
            container.AddToken(new Token(Marker, 1, new TokenClearTrigger.ActivationEnd()));

            new TokenClearService().ClearForHook(EHookID.Round_OnRoundEnd, new ITokenContainer[] { container });

            Assert.That(container.HasToken(Marker), Is.True, "an activation marker does not clear at round end");
        }

        [Test]
        public void ApplyTokenOperations_GrantsAndConsumes()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            DataBinding<UnitData> unit = MakeUnit(store);
            var spell = new TokenType("SpellTokens");

            OperationApplier.ApplyTokenOperations(new RuleOperation[]
            {
                new RuleOperation.GrantTokenToUnit(unit.GetValue(),
                    new Token(spell, 3, new TokenClearTrigger.ManualOnly())),
            });
            Assert.That(unit.GetValue().Tokens.GetTokenCount(spell), Is.EqualTo(3), "grant adds tokens");

            OperationApplier.ApplyTokenOperations(new RuleOperation[]
            {
                new RuleOperation.ConsumeTokensFromUnit(unit.GetValue(), spell, 2),
            });
            Assert.That(unit.GetValue().Tokens.GetTokenCount(spell), Is.EqualTo(1), "consume removes tokens");
        }

        [Test]
        public async Task ReconcileEndOfActivation_ClearsActivatedUnitsActivationMarker()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var ctx = new WoundTestContext(store, new NullPlayerRequester());
            DataBinding<UnitData> unit = MakeUnit(store);
            unit.GetValue().Tokens.AddToken(new Token(Marker, 1, new TokenClearTrigger.ActivationEnd()));

            var turn = new SingleTurnContext(ctx, unit.GetValue().PlayerID,
                new List<DataBinding<UnitData>> { unit });
            turn.ChooseUnitToActivate(unit);

            var stage = new ReconcileEndOfActivationStage(ctx, new NoOpLayer<ISingleTurnContext>());
            stage.OnFinished.Bind("done");
            await stage.Enter(turn);

            Assert.That(unit.GetValue().Tokens.HasToken(Marker), Is.False,
                "end of activation clears the activated unit's once-per-activation marker");
        }

        private static DataBinding<UnitData> MakeUnit(GameDataStore store)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), new List<SpecialRule>(), new Position(0f, 0f), store);
            DataBinding<ModelData> modelBinding = store.GetDataBinding<ModelData>(store.Create(model));
            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "Unit", quality: 4, defense: 4,
                specialRules: new List<SpecialRule>(),
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            return store.GetDataBinding<UnitData>(store.Create(unit));
        }
    }
}
