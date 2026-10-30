using System.Text.Json;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Serialization;
using FDG.Rules.Tokens;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #434 - OPR 3.6 Mind Control: "Once per activation, before attacking, pick one enemy unit within 18" in line of sight,
    // which must take a Morale Test. If failed you may move it by up to 6" in a straight line in any direction. Units may
    // only be moved once per round in this way." The new last sentence is a per-TARGET gate across every Mind Control on
    // the table: Effect.TriggeredMove gained OncePerRoundKey, and RuleOperation.InvokeTriggeredMove refuses a unit already
    // moved this round under the key and stamps one that actually moved (RoundEnd clear). Driven through the real
    // MoraleTestThen -> GameOperationServices.MoveUnit -> MovementExecutor path (MoraleTestThenRuleIntegrationTests'
    // harness); the morale test still happens - only the move is refused.
    [TestFixture]
    public class MindControlOncePerRoundRuleIntegrationTests
    {
        private const string Key = "Mind Control";

        private GameDataStore _store = null!;
        private PlayerID _owner;
        private PlayerID _victim;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _owner = new PlayerID(System.Guid.NewGuid());
            _victim = new PlayerID(System.Guid.NewGuid());
        }

        private static Effect MindControlMove(string? key = Key) => new Effect.TriggeredMove(6f, IsOptional: true, key);

        [Test]
        public async Task TheFirstFailedTest_MovesTheVictim_AndStampsIt()
        {
            var requester = new PushRequester(dx: 3f);
            DataBinding<UnitData> victim = MakeUnit(_victim, 10f);

            await FailTestThen(requester, MakeUnit(_owner, 0f), victim, MindControlMove());

            Assert.That(X(victim), Is.EqualTo(13f).Within(0.001f), "moved 3\"");
            Token stamp = victim.GetValue().Tokens
                .GetAllTokens(RuleOperation.InvokeTriggeredMove.OncePerRoundMoveToken(Key)).Single();
            Assert.That(stamp.ClearTrigger, Is.InstanceOf<TokenClearTrigger.RoundEnd>(), "'once per ROUND'");
        }

        [Test]
        public async Task ASecondMindControlThisRound_CannotMoveTheSameUnit()
        {
            DataBinding<UnitData> victim = MakeUnit(_victim, 10f);
            await FailTestThen(new PushRequester(dx: 3f), MakeUnit(_owner, 0f), victim, MindControlMove());

            // Another Mind Control user (a second owner unit) fails the victim's test again this round.
            var second = new PushRequester(dx: 3f);
            await FailTestThen(second, MakeUnit(_owner, 0f), victim, MindControlMove());

            Assert.That(second.MovesAsked, Is.EqualTo(0), "no move is offered");
            Assert.That(X(victim), Is.EqualTo(13f).Within(0.001f), "it stays where the first move left it");
        }

        [Test]
        public async Task ADeclinedMove_DoesNotSpendTheVictimsRound()
        {
            DataBinding<UnitData> victim = MakeUnit(_victim, 10f);
            await FailTestThen(new PushRequester(dx: 0f, cancel: true), MakeUnit(_owner, 0f), victim, MindControlMove());

            var second = new PushRequester(dx: 2f);
            await FailTestThen(second, MakeUnit(_owner, 0f), victim, MindControlMove());

            Assert.That(second.MovesAsked, Is.EqualTo(1), "'moved ... in this way' - the declined one moved nothing");
            Assert.That(X(victim), Is.EqualTo(12f).Within(0.001f));
        }

        [Test]
        public async Task AnUnkeyedForcedMove_IsUnrestricted()
        {
            // Every other triggered move (a reposition spell, the post-combat family) carries no key.
            DataBinding<UnitData> victim = MakeUnit(_victim, 10f);
            await FailTestThen(new PushRequester(dx: 1f), MakeUnit(_owner, 0f), victim, MindControlMove(key: null));
            await FailTestThen(new PushRequester(dx: 1f), MakeUnit(_owner, 0f), victim, MindControlMove(key: null));

            Assert.That(X(victim), Is.EqualTo(12f).Within(0.001f));
        }

        [Test]
        public void TheKey_RoundTripsThroughJson_AndOldDataIsUnkeyed()
        {
            Effect keyed = MindControlMove();
            string json = JsonSerializer.Serialize(keyed, RuleJson.Options);
            Assert.That(JsonSerializer.Deserialize<Effect>(json, RuleJson.Options), Is.EqualTo(keyed));

            string legacy = "{\"kind\":\"triggeredMove\",\"maxInches\":6,\"isOptional\":true}";
            Assert.That(JsonSerializer.Deserialize<Effect>(legacy, RuleJson.Options),
                Is.EqualTo(new Effect.TriggeredMove(6f, true, OncePerRoundKey: null)));
        }

        // --- helpers --------------------------------------------------------------------------------------------

        private async Task FailTestThen(IPlayerRequestByID requester, DataBinding<UnitData> owner,
            DataBinding<UnitData> victim, Effect onFailure)
        {
            // Quality 4 needs a 4+; a die fixed to 1 fails.
            var ctx = new TriggeredMoveTestContext(_store, requester, new FixedDiceRoller(1));
            var operations = new List<RuleOperation>
            {
                new RuleOperation.InvokeMoraleTestThen(owner.GetValue(), victim.GetValue(), onFailure),
            };
            await OperationExecutor.Execute(operations, new GameOperationServices(ctx));
        }

        private static float X(DataBinding<UnitData> unit) =>
            unit.GetValue().ModelBindings[0].GetValue().PositionBinding.GetValue().x;

        private DataBinding<UnitData> MakeUnit(PlayerID player, float x)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), new Position(x, 10f), _store);
            var unit = new UnitData(player, "Test Unit", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { _store.GetDataBinding<ModelData>(_store.Create(model)) });
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        private sealed class PushRequester : IPlayerRequestByID
        {
            private readonly float _dx;
            private readonly bool _cancel;
            public int MovesAsked;

            public PushRequester(float dx, bool cancel = false)
            {
                _dx = dx;
                _cancel = cancel;
            }

            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply>
            {
                if (request is DefineMovementPathRequest move)
                {
                    MovesAsked++;
                    if (_cancel) return Task.FromResult((TReply)(object)new Cancelled<List<ModelMoveEntry>>());
                    var entries = move.UnitDataBinding.GetValue().ModelBindings
                        .Select(m => new ModelMoveEntry(m, new List<Position>
                        {
                            new Position(m.GetValue().PositionBinding.GetValue().x + _dx,
                                m.GetValue().PositionBinding.GetValue().z),
                        }))
                        .ToList();
                    return Task.FromResult((TReply)(object)new Selected<List<ModelMoveEntry>>(entries));
                }

                throw new System.InvalidOperationException("Unexpected request: " + request.GetType());
            }
        }
    }
}
