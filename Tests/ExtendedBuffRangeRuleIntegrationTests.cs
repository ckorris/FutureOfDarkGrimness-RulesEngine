using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
    // #197 Extended Buff Range (the HDF radios): "relay non-spell Hero picks across 24\" via another
    // friendly unit with the rule." The bearer answers Lifecycle_OnCapabilityQuery with EnableBuffRelay,
    // and AbilityTargeting lets a FRIENDLY pick measure from the relay's position: user within the relay's
    // 12\", target within the ability's own range of the relay - the ability twin of Spell Conduit's
    // SpellRelay, minus the roll bonus. The relay relaxes range ONLY: Foe picks and sight-requiring picks
    // are never relayed, and every other targeting filter still applies to the candidate.
    [TestFixture]
    public class ExtendedBuffRangeRuleIntegrationTests
    {
        private const float RelayRangeInches = 12f;

        private GameDataStore _store = null!;
        private PlayerID _player;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _player = new PlayerID(Guid.NewGuid());
        }

        private static SpecialRuleDefinition ExtendedBuffRange() => new("Extended Buff Range",
            new[]
            {
                new HookEntry(EHookID.Lifecycle_OnCapabilityQuery, new Condition.Always(),
                    new Effect.EnableBuffRelay(RelayRangeInches), ELifetime.UntilEndOfGame),
            },
            Array.Empty<ActivatedAbility>());

        // A 12\" friendly pick - the shape of the entire supplement Buff family.
        private static TargetSelector FriendPick(bool requireLineOfSight = false) =>
            new TargetSelector(12f, 1, 1, ETargetAffinity.Friend, requireLineOfSight);

        // --- the capability answer --------------------------------------------------------------------

        [Test]
        public void BuffRelayOffers_CollectsTheAuthoredOffer()
        {
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> bearer = MakeUnitAt(new Position(0f, 0f), ExtendedBuffRange());

            IReadOnlyList<RuleOperation.EnableBuffRelay> offers =
                CapabilityRuleQueries.BuffRelayOffers(bearer.GetValue(), ctx.RuleEvaluator);

            Assert.That(offers, Has.Count.EqualTo(1), "the bearer answers the capability question");
            Assert.That(offers[0].RangeInches, Is.EqualTo(RelayRangeInches));
        }

        // --- what the relay reaches -------------------------------------------------------------------

        [Test]
        public void RelayInReach_ExtendsAFriendPickBeyondItsOwnRange()
        {
            // Actor 0 -- relay 10 (within the relay's 12) -- far ally 20 (out of the pick's own 12 from
            // the actor, within 12 of the relay). Margins sit well away from every boundary.
            DataBinding<UnitData> actor = MakeUnitAt(new Position(0f, 0f), null);
            DataBinding<UnitData> relay = MakeUnitAt(new Position(10f, 0f), ExtendedBuffRange());
            DataBinding<UnitData> farAlly = MakeUnitAt(new Position(20f, 0f), null);
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());

            List<DataBinding<UnitData>> eligible = AbilityTargeting.EligibleTargets(actor, FriendPick(), ctx);

            Assert.That(eligible, Does.Contain(farAlly),
                "20\" away is pickable: 10\" to the relay, 10\" from the relay to the target");
            Assert.That(eligible, Does.Contain(relay), "the relay itself is directly in range");
        }

        [Test]
        public void NoRelay_TheSameFarAllyIsOutOfReach()
        {
            DataBinding<UnitData> actor = MakeUnitAt(new Position(0f, 0f), null);
            DataBinding<UnitData> bystander = MakeUnitAt(new Position(10f, 0f), null); // no relay rule
            DataBinding<UnitData> farAlly = MakeUnitAt(new Position(20f, 0f), null);
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());

            List<DataBinding<UnitData>> eligible = AbilityTargeting.EligibleTargets(actor, FriendPick(), ctx);

            Assert.That(eligible, Does.Not.Contain(farAlly),
                "without the rule the 12\" pick stays a 12\" pick - the control for every test above");
        }

        [Test]
        public void RelayBeyondItsOwnRange_DoesNotHelp()
        {
            // The relay leg is the RELAY RULE's range: a radio 15\" from the user is out of its 12\".
            DataBinding<UnitData> actor = MakeUnitAt(new Position(0f, 0f), null);
            DataBinding<UnitData> relay = MakeUnitAt(new Position(15f, 0f), ExtendedBuffRange());
            DataBinding<UnitData> farAlly = MakeUnitAt(new Position(20f, 0f), null);
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());

            List<DataBinding<UnitData>> eligible = AbilityTargeting.EligibleTargets(actor, FriendPick(), ctx);

            Assert.That(eligible, Does.Not.Contain(farAlly),
                "a relay the user cannot reach relays nothing");
        }

        [Test]
        public void TargetBeyondTheAbilityRangeOfTheRelay_IsStillOut()
        {
            // The target leg is the ABILITY's own range measured from the relay: 16\" from the radio is out.
            DataBinding<UnitData> actor = MakeUnitAt(new Position(0f, 0f), null);
            DataBinding<UnitData> relay = MakeUnitAt(new Position(10f, 0f), ExtendedBuffRange());
            DataBinding<UnitData> farAlly = MakeUnitAt(new Position(26f, 0f), null);
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());

            List<DataBinding<UnitData>> eligible = AbilityTargeting.EligibleTargets(actor, FriendPick(), ctx);

            Assert.That(eligible, Does.Not.Contain(farAlly),
                "the relay is not a range doubler - it is a second position to measure the same 12\" from");
        }

        // --- what the relay never touches -------------------------------------------------------------

        [Test]
        public void FoePick_IsNeverRelayed()
        {
            // Same geometry as the happy path, but the candidate is an enemy and the pick is a Foe pick
            // (a debuff): Extended Buff Range relays buffs, not target acquisition.
            DataBinding<UnitData> actor = MakeUnitAt(new Position(0f, 0f), null);
            DataBinding<UnitData> relay = MakeUnitAt(new Position(10f, 0f), ExtendedBuffRange());
            DataBinding<UnitData> enemy = MakeEnemyUnitAt(new Position(20f, 0f));
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());

            var foePick = new TargetSelector(12f, 1, 1, ETargetAffinity.Foe, false);
            List<DataBinding<UnitData>> eligible = AbilityTargeting.EligibleTargets(actor, foePick, ctx);

            Assert.That(eligible, Is.Empty, "a Foe pick ignores the relay entirely");
        }

        [Test]
        public void EnemyBearer_IsNotARelay()
        {
            DataBinding<UnitData> actor = MakeUnitAt(new Position(0f, 0f), null);
            DataBinding<UnitData> enemyRelay = MakeEnemyUnitAt(new Position(10f, 0f), ExtendedBuffRange());
            DataBinding<UnitData> farAlly = MakeUnitAt(new Position(20f, 0f), null);
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());

            List<DataBinding<UnitData>> eligible = AbilityTargeting.EligibleTargets(actor, FriendPick(), ctx);

            Assert.That(eligible, Does.Not.Contain(farAlly), "only ANOTHER FRIENDLY unit relays");
        }

        [Test]
        public void SightRequiringPick_IsNotRelayed()
        {
            // A relay lends position, not eyes. No corpus Friend-pick requires line of sight, so the
            // combination is gated out rather than guessed at - this pins the gate.
            DataBinding<UnitData> actor = MakeUnitAt(new Position(0f, 0f), null);
            DataBinding<UnitData> relay = MakeUnitAt(new Position(10f, 0f), ExtendedBuffRange());
            DataBinding<UnitData> farAlly = MakeUnitAt(new Position(20f, 0f), null);
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());

            List<DataBinding<UnitData>> eligible = AbilityTargeting.EligibleTargets(
                actor, FriendPick(requireLineOfSight: true), ctx);

            Assert.That(eligible, Does.Not.Contain(farAlly));
        }

        // --- end to end through the stage -------------------------------------------------------------

        // The relayed pick is not just listed - the stage accepts it and the buff lands on the far ally.
        [Test]
        public async Task RelayedPick_ResolvesThroughBeforeAttackActionStage()
        {
            (SpecialRuleDefinition buffDef, TokenType marker) = MakeFriendBuffRule();
            DataBinding<UnitData> bearer = MakeUnitAt(new Position(0f, 0f), buffDef);
            DataBinding<UnitData> relay = MakeUnitAt(new Position(10f, 0f), ExtendedBuffRange());
            DataBinding<UnitData> farAlly = MakeUnitAt(new Position(20f, 0f), null);

            // Not CannedTargetRequester: that one force-picks its target whether or not the stage OFFERED
            // it, which would let this test pass with the relay dead. This requester behaves like a real
            // resolver - it takes farAlly only when farAlly is among the request's valid options.
            var ctx = new TriggeredMoveTestContext(_store, new OfferedTargetRequester(farAlly));
            UnitActionContext unitCtx = new UnitActionContext(ctx, bearer);
            unitCtx.Reset(bearer);
            AbilityOffer offer = ctx.RuleEvaluator.GatherOffers(
                new Rules.Dispatch.Contexts.BeforeAttackActionContext(bearer.GetValue()))[0];
            unitCtx.SetPendingCustomAction(offer);

            var stage = new BeforeAttackActionStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("OnFinished");
            await stage.Enter(unitCtx);

            Assert.That(farAlly.GetValue().Tokens.HasToken(marker), Is.True,
                "the buff reached a unit 20\" away through the 10\"-away radio");
        }

        // --- helpers ----------------------------------------------------------------------------------

        // A Friend-targeted before-attack ability: pick one friendly unit within 12\" and grant a marker -
        // the same shape as every supplement "* Buff".
        private static (SpecialRuleDefinition def, TokenType marker) MakeFriendBuffRule()
        {
            var marker = new TokenType("RelayedBuffFired");
            var ability = new ActivatedAbility(
                EHookID.Activation_OnBeforeAttackAction, new Cost.OncePerActivation(),
                FriendPick(),
                new Effect.GrantToken(marker, new ValueSource.Literal(1), new TokenClearTrigger.ManualOnly()),
                new Condition.Always());
            return (new SpecialRuleDefinition("Test Buff", Array.Empty<HookEntry>(), new[] { ability }), marker);
        }

        private DataBinding<UnitData> MakeUnitAt(Position position, SpecialRuleDefinition? rule) =>
            MakeUnitFor(_player, position, rule);

        private DataBinding<UnitData> MakeEnemyUnitAt(Position position, SpecialRuleDefinition? rule = null) =>
            MakeUnitFor(new PlayerID(Guid.NewGuid()), position, rule);

        // Selects the given target IF the stage offered it as a valid option; cancels otherwise - the
        // contract every real resolver honours, unlike CannedTargetRequester's unconditional pick.
        private sealed class OfferedTargetRequester : IPlayerRequestByID
        {
            private readonly DataBinding<UnitData> _target;

            public OfferedTargetRequester(DataBinding<UnitData> target) => _target = target;

            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply>
            {
                if (request is CancellableSelectionRequest<UnitData> selection)
                {
                    CancellableResult<DataBinding<UnitData>> result =
                        selection.ValidOptions.Any(o => o.Option == _target)
                            ? new Selected<DataBinding<UnitData>>(_target)
                            : new Cancelled<DataBinding<UnitData>>();
                    return Task.FromResult((TReply)(object)result);
                }

                throw new InvalidOperationException("Unexpected request type: " + request.GetType());
            }
        }

        private DataBinding<UnitData> MakeUnitFor(PlayerID player, Position position,
            SpecialRuleDefinition? rule)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), position, _store);
            var modelBindings = new List<DataBinding<ModelData>>
            {
                _store.GetDataBinding<ModelData>(_store.Create(model)),
            };
            var unit = new UnitData(player, "Test Unit", quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));

            if (rule != null)
            {
                binding.GetValue().AttachRuleDefinition(new ResolvedRule(rule.Name, rule));
            }

            _store.Create(new ArmyData(player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }
    }
}
