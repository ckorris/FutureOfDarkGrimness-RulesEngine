using System.Collections.Generic;
using System.Linq;
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
    // Vertical-slice integration test for #197 P23's Spell Conduit (9 refs), which closes P23 at 19/19:
    //
    //   "Casters within 12" that are from other friendly units may cast spells as if they were in this
    //    model's position, and get +1 to casting rolls when doing so. Friendly casters may only use this
    //    rule if this unit isn't Shaken."
    //
    // Same capability shape as Spell Accumulator - an offer made outward, gated on range, friendliness,
    // otherness and not-Shaken - so those gates are tested the same way. What is new is that the offer
    // changes WHERE a spell is measured from, which is a property of the whole cast rather than a pool to
    // draw on.
    //
    // No prompt asks which origin to use: a relay origin is never worse than the caster's own (it can only
    // add reach, and it adds the bonus), so the origin is derived from the targets picked. The tests
    // therefore cover both directions - a target only the relay reaches (bonus applies) and one only the
    // caster reaches (it does not) - plus the strings that make the bonus visible, which is the whole
    // reason the derivation is acceptable in place of a choice.
    [TestFixture]
    public class SpellConduitRuleIntegrationTests
    {
        private const float RelayRangeInches = 12f;
        private const int RelayBonus = 1;

        private GameDataStore _store = null!;
        private PlayerID _player;
        private PlayerID _enemy;

        // Geometry, all base-to-base with 0.5" radii and an 18" spell:
        //   caster (10,10) - conduit (10,20)   =  9" apart, inside the relay's 12"
        //   NearOnlyRelay  (10,36)             = 25" from the caster (out), 15" from the conduit (in)
        //   NearOnlySelf   (28,10)             = 17" from the caster (in), ~19.6" from the conduit (out)
        private static readonly Position CasterAt = new Position(10f, 10f);
        private static readonly Position ConduitAt = new Position(10f, 20f);
        private static readonly Position ReachableOnlyByRelay = new Position(10f, 36f);
        private static readonly Position ReachableOnlyBySelf = new Position(28f, 10f);

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _player = new PlayerID(System.Guid.NewGuid());
            _enemy = new PlayerID(System.Guid.NewGuid());
        }

        /// <summary>Spell Conduit as the shipped supplement authors it: one capability entry, shut while
        /// the conduit is Shaken.</summary>
        private static SpecialRuleDefinition SpellConduit() => new("Spell Conduit",
            new[]
            {
                new HookEntry(EHookID.Lifecycle_OnCapabilityQuery,
                    new Condition.Not(new Condition.TokenPresent(TokenType.Shaken)),
                    new Effect.EnableSpellRelay(RelayRangeInches, RelayBonus),
                    ELifetime.UntilEndOfGame),
            },
            System.Array.Empty<ActivatedAbility>());

        // --- who relays -----------------------------------------------------------------------------

        [Test]
        public void ANearbyFriendlyConduit_OffersItselfAsAnOrigin()
        {
            var ctx = Context();
            IUnit caster = MakeCaster(CasterAt, tokens: 3);
            IUnit conduit = MakeConduit(ConduitAt, _player);

            IReadOnlyList<SpellRelay.CastOrigin> origins = Origins(ctx, caster);

            Assert.That(origins.Select(o => o.Unit.Name), Does.Contain(conduit.Name));
            Assert.That(origins.Last().IsSelf, Is.True,
                "the caster's own position is always available and always last, so preferring the head of " +
                "the list prefers the bonus.");
            Assert.That(origins.First().RollBonus, Is.EqualTo(RelayBonus));
        }

        [Test]
        public void TheConduitItself_CannotRelayForItself()
        {
            // "Casters within 12" that are from OTHER friendly units."
            var ctx = Context();
            IUnit conduit = MakeConduit(ConduitAt, _player);
            AttachCaster(conduit);

            Assert.That(Origins(ctx, conduit).Where(o => !o.IsSelf), Is.Empty);
        }

        [Test]
        public void AnEnemyConduit_DoesNotRelay()
        {
            var ctx = Context();
            IUnit caster = MakeCaster(CasterAt, tokens: 3);
            MakeConduit(ConduitAt, _enemy);

            Assert.That(Origins(ctx, caster).Where(o => !o.IsSelf), Is.Empty, "'other FRIENDLY units'.");
        }

        [Test]
        public void BeyondTwelveInches_TheConduitDoesNotRelay()
        {
            var ctx = Context();
            IUnit caster = MakeCaster(CasterAt, tokens: 3);
            MakeConduit(new Position(10f, 24f), _player); // 14" centre to centre, 13" base to base.

            Assert.That(Origins(ctx, caster).Where(o => !o.IsSelf), Is.Empty, "'within 12 inches'.");
        }

        [Test]
        public void AShakenConduit_DoesNotRelay_AndRecoveringRestoresIt()
        {
            var ctx = Context();
            IUnit caster = MakeCaster(CasterAt, tokens: 3);
            IUnit conduit = MakeConduit(ConduitAt, _player);

            conduit.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.Shaken));
            Assert.That(Origins(ctx, caster).Where(o => !o.IsSelf), Is.Empty,
                "'friendly casters may only use this rule if this unit isn't Shaken'.");

            conduit.Tokens.RemoveTokens(TokenType.Shaken);
            Assert.That(Origins(ctx, caster).Where(o => !o.IsSelf), Is.Not.Empty,
                "the capability is re-asked every time, so the relay reopens the moment it recovers.");
        }

        [Test]
        public void ADestroyedConduit_DoesNotRelay()
        {
            var ctx = Context();
            IUnit caster = MakeCaster(CasterAt, tokens: 3);
            IUnit conduit = MakeConduit(ConduitAt, _player);
            ((ModelData)conduit.Models[0]).DealWounds(((ModelData)conduit.Models[0]).TotalWounds);

            Assert.That(Origins(ctx, caster).Where(o => !o.IsSelf), Is.Empty);
        }

        // --- what a relay changes -------------------------------------------------------------------

        [Test]
        public void TheRelayExtendsReach_ToATargetTheCasterCannotReach()
        {
            var ctx = Context();
            DataBinding<UnitData> caster = MakeCasterBinding(CasterAt, tokens: 3);
            IUnit conduit = MakeConduit(ConduitAt, _player);
            DataBinding<UnitData> distant = MakeEnemy(ReachableOnlyByRelay);

            var selector = new TargetSelector(18f, 1, 1, ETargetAffinity.Foe, RequireLineOfSight: false);

            Assert.That(SpellTargeting.HasAnyEligibleTarget(ctx, caster, _player, selector), Is.False,
                "25 inches away - out of the spell's 18 inch range from where the caster stands.");
            Assert.That(SpellTargeting.HasAnyEligibleTarget(ctx, caster, _player, selector, conduit), Is.True,
                "'may cast spells as if they were in this model's position' - 15 inches from the conduit.");
            Assert.That(SpellTargeting.GetEligibleTargets(ctx, caster, _player, selector, conduit)
                .Select(u => u.Reference), Does.Contain(distant.Reference));
        }

        [Test]
        public void AffinityIsStillJudgedFromTheCaster_NotTheRelay()
        {
            // A relay moves where the spell is measured FROM; it does not change whose side anyone is on.
            // A Friend-affinity spell must not start treating the caster's allies as foes because the
            // measuring point moved.
            var ctx = Context();
            DataBinding<UnitData> caster = MakeCasterBinding(CasterAt, tokens: 3);
            IUnit conduit = MakeConduit(ConduitAt, _player);
            MakeEnemy(ReachableOnlyByRelay);

            var friendly = new TargetSelector(18f, 1, 1, ETargetAffinity.Friend, RequireLineOfSight: false);
            List<DataBinding<UnitData>> targets = SpellTargeting.GetEligibleTargets(
                ctx, caster, _player, friendly, conduit);

            Assert.That(targets.Select(t => t.GetValue().PlayerID), Has.All.EqualTo(_player));
        }

        // --- the stages -----------------------------------------------------------------------------
        //
        // Pinned separately from the query-level tests above: without these, reverting either stage to
        // measuring from the caster alone would leave every test above green while a relayed cast silently
        // stopped being offered and stopped getting its bonus.

        [Test]
        public async Task ChooseAction_OffersCast_WhenOnlyTheRelayCanReachATarget()
        {
            var requester = new RecordingActionRequester("Pass");
            var ctx = new TriggeredMoveTestContext(_store, requester);
            DataBinding<UnitData> caster = MakeCasterWithArmy(tokens: 3,
                new[] { DebuffSpell("Hex", threshold: 1) }, out ArmyData army);
            AddConduitToArmy(army, ConduitAt);
            MakeEnemy(ReachableOnlyByRelay);

            UnitActionContext unitCtx = NewActivation(ctx, caster);
            var stage = new ChooseActionStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.ToReconcileEndOfActivation.Bind("Pass");
            await stage.Enter(unitCtx);

            Assert.That(requester.OfferedOptions, Contains.Item("Cast"),
                "the only enemy is out of the caster's own reach but inside the conduit's.");
        }

        [Test]
        public async Task ARelayedCast_GetsThePlusOne()
        {
            // Every die comes up 3. Base 4+ fails; the relay's +1 shifts the threshold to 3+, so the spell
            // lands - and landing is observable as the debuff on the target.
            var requester = new RecordingCastRequester();
            var ctx = new TriggeredMoveTestContext(_store, requester, new FixedFaceDiceRoller(3));
            DataBinding<UnitData> caster = MakeCasterWithArmy(tokens: 3,
                new[] { DebuffSpell("Hex", threshold: 1) }, out ArmyData army);
            AddConduitToArmy(army, ConduitAt);
            DataBinding<UnitData> target = MakeEnemy(ReachableOnlyByRelay);

            await RunCast(ctx, caster);

            Assert.That(target.GetValue().Tokens.GetAllTokens(TokenType.RuleGrant), Is.Not.Empty,
                "rolled 3, needed 3+ (base 4+, relay +1) - without the bonus this cast fails.");
        }

        [Test]
        public async Task WhenBothOriginsReachTheTarget_TheRelayIsPreferredForItsBonus()
        {
            // A target inside BOTH the caster's range and the conduit's. Nothing forces the relay here -
            // the caster could cast it unaided - so this pins the "relays first" preference: given a free
            // choice, the cast takes the origin that carries the bonus. Rolled 3 needs the +1 to land.
            var requester = new RecordingCastRequester();
            var ctx = new TriggeredMoveTestContext(_store, requester, new FixedFaceDiceRoller(3));
            DataBinding<UnitData> caster = MakeCasterWithArmy(tokens: 3,
                new[] { DebuffSpell("Hex", threshold: 1) }, out ArmyData army);
            IUnit conduit = AddConduitToArmy(army, ConduitAt);
            DataBinding<UnitData> target = MakeEnemy(new Position(12f, 14f)); // ~4" from caster, ~6" from conduit

            await RunCast(ctx, caster);

            Assert.That(target.GetValue().Tokens.GetAllTokens(TokenType.RuleGrant), Is.Not.Empty,
                "the caster could reach this itself, but the relay origin is chosen for the +1, so 3 lands.");
            Assert.That(requester.TargetLabels.Single(l => l.StartsWith(target.GetValue().Name)),
                Does.Contain($"(via {conduit.Name}, +{RelayBonus})"),
                "and the row advertises the relay even though the caster's own position would also work.");
        }

        [Test]
        public async Task ACastTheRelayCannotReach_GetsNoBonus()
        {
            // The other side of deriving the origin from the targets: the only enemy is inside the caster's
            // range but outside the conduit's, so the cast is made from the caster and the 3 fails against
            // the unmodified 4+. Same board, same dice, no debuff - the bonus is not a flat rider on
            // "a conduit is nearby".
            var requester = new RecordingCastRequester();
            var ctx = new TriggeredMoveTestContext(_store, requester, new FixedFaceDiceRoller(3));
            DataBinding<UnitData> caster = MakeCasterWithArmy(tokens: 3,
                new[] { DebuffSpell("Hex", threshold: 1) }, out ArmyData army);
            AddConduitToArmy(army, ConduitAt);
            DataBinding<UnitData> target = MakeEnemy(ReachableOnlyBySelf);

            await RunCast(ctx, caster);

            Assert.That(target.GetValue().Tokens.GetAllTokens(TokenType.RuleGrant), Is.Empty,
                "the conduit cannot see this target, so the cast is made from the caster at base 4+.");
        }

        [Test]
        public async Task WithNoConduitOnTheTable_NothingChanges()
        {
            // The degrade path: one origin, the caster's own, and the old single-list behaviour exactly.
            var requester = new RecordingCastRequester();
            var ctx = new TriggeredMoveTestContext(_store, requester, new FixedFaceDiceRoller(4));
            DataBinding<UnitData> caster = MakeCasterWithArmy(tokens: 3,
                new[] { DebuffSpell("Hex", threshold: 1) }, out _);
            DataBinding<UnitData> target = MakeEnemy(ReachableOnlyBySelf);

            await RunCast(ctx, caster);

            Assert.That(requester.SpellRequest!.RelaysInRange, Is.Empty);
            Assert.That(requester.TargetLabels, Is.EqualTo(new[] { target.GetValue().Name }),
                "no relay, no suffix - the target row is the plain unit name.");
            Assert.That(target.GetValue().Tokens.GetAllTokens(TokenType.RuleGrant), Is.Not.Empty,
                "rolled 4, needed 4+.");
        }

        // --- the bonus is visible, not incidental ---------------------------------------------------
        //
        // The reason no prompt asks which origin to use is that the player is told, in the picker and then
        // per target row, that the relay exists and what it does. If these strings go, the derivation stops
        // being acceptable - so they are pinned like behaviour.

        [Test]
        public async Task TheSpellPicker_AnnouncesTheRelayInRange()
        {
            var requester = new RecordingCastRequester();
            var ctx = new TriggeredMoveTestContext(_store, requester, new FixedFaceDiceRoller(3));
            DataBinding<UnitData> caster = MakeCasterWithArmy(tokens: 3,
                new[] { DebuffSpell("Hex", threshold: 1) }, out ArmyData army);
            IUnit conduit = AddConduitToArmy(army, ConduitAt);
            MakeEnemy(ReachableOnlyByRelay);

            await RunCast(ctx, caster);

            Assert.That(requester.SpellRequest!.RelaysInRange.Select(r => r.UnitName),
                Is.EqualTo(new[] { conduit.Name }),
                "the picker is where a player decides a spell is not worth casting - the bonus has to be " +
                "on the table before that decision, not revealed after it.");
            Assert.That(requester.SpellRequest!.RelaysInRange.Single().RollBonus, Is.EqualTo(RelayBonus));
        }

        [Test]
        public async Task EachTargetRow_NamesTheOriginThatWouldBeUsed()
        {
            var requester = new RecordingCastRequester();
            var ctx = new TriggeredMoveTestContext(_store, requester, new FixedFaceDiceRoller(3));
            DataBinding<UnitData> caster = MakeCasterWithArmy(tokens: 3,
                new[] { DebuffSpell("Hex", threshold: 1) }, out ArmyData army);
            IUnit conduit = AddConduitToArmy(army, ConduitAt);
            MakeEnemy(ReachableOnlyByRelay);
            MakeEnemy(ReachableOnlyBySelf);

            await RunCast(ctx, caster);

            // Relay-reachable rows carry the origin and the bonus; a self-only row is the plain name. This
            // is the exact information a player needs to aim for the bonus.
            Assert.That(requester.TargetLabels.Any(l => l.Contains($"(via {conduit.Name}, +{RelayBonus})")),
                Is.True, $"expected a relayed row; got [{string.Join(" | ", requester.TargetLabels)}]");
            Assert.That(requester.TargetLabels.Any(l => !l.Contains("(via ")), Is.True,
                "and the target only the caster reaches must NOT claim the bonus.");
        }

        // --- helpers ---------------------------------------------------------------------------------

        private TriggeredMoveTestContext Context() =>
            new TriggeredMoveTestContext(_store, new NoRequestsRequester());

        private static IReadOnlyList<SpellRelay.CastOrigin> Origins(TriggeredMoveTestContext ctx,
            IUnit caster) => SpellRelay.OriginsFor(ctx.TableState, ctx.RuleEvaluator, caster);

        private async Task RunCast(TriggeredMoveTestContext ctx, DataBinding<UnitData> caster)
        {
            UnitActionContext unitCtx = NewActivation(ctx, caster);
            var stage = new CastSpellStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("OnFinished");
            await stage.Enter(unitCtx);
        }

        private DataBinding<UnitData> MakeUnitBinding(string name, PlayerID owner, Position pos,
            bool ownArmy = true)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), pos, _store);
            var bindings = new List<DataBinding<ModelData>>
            {
                _store.GetDataBinding<ModelData>(_store.Create(model)),
            };

            var unit = new UnitData(owner, name, quality: 4, defense: 4, modelBindings: bindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            if (ownArmy) _store.Create(new ArmyData(owner, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        private IUnit MakeConduit(Position pos, PlayerID owner)
        {
            DataBinding<UnitData> binding = MakeUnitBinding("Synaptic Relay", owner, pos);
            binding.GetValue().AttachRuleDefinition(
                new ResolvedRule("Spell Conduit", SpellConduit()));
            return binding.GetValue();
        }

        private IUnit MakeCaster(Position pos, int tokens) => MakeCasterBinding(pos, tokens).GetValue();

        private DataBinding<UnitData> MakeCasterBinding(Position pos, int tokens)
        {
            DataBinding<UnitData> binding = MakeUnitBinding("Psy-Seer", _player, pos);
            AttachCaster(binding.GetValue());
            if (tokens > 0)
            {
                binding.GetValue().Tokens.AddToken(
                    new Token(TokenType.SpellTokens, tokens, new TokenClearTrigger.ManualOnly()));
            }

            return binding;
        }

        private DataBinding<UnitData> MakeEnemy(Position pos) =>
            MakeUnitBinding("Grunts", _enemy, pos);

        private DataBinding<UnitData> MakeCasterWithArmy(int tokens, IReadOnlyList<RuntimeSpell> spells,
            out ArmyData army)
        {
            DataBinding<UnitData> binding = MakeUnitBinding("Psy-Seer", _player, CasterAt, ownArmy: false);
            AttachCaster(binding.GetValue());
            binding.GetValue().Tokens.AddToken(
                new Token(TokenType.SpellTokens, tokens, new TokenClearTrigger.ManualOnly()));

            army = new ArmyData(_player, new List<DataBinding<UnitData>> { binding });
            army.SetSpells(spells);
            _store.Create(army);
            return binding;
        }

        private IUnit AddConduitToArmy(ArmyData army, Position pos)
        {
            DataBinding<UnitData> binding = MakeUnitBinding("Synaptic Relay", _player, pos, ownArmy: false);
            binding.GetValue().AttachRuleDefinition(new ResolvedRule("Spell Conduit", SpellConduit()));
            army.UnitBindings.Add(binding);
            return binding.GetValue();
        }

        private static void AttachCaster(IUnit unit) =>
            ((UnitData)unit).AttachRuleDefinition(new ResolvedRule("Caster", CoreRuleCatalog.Caster,
                new RuleArgument[] { new RuleArgument.Int(2) }));

        // Foe-affinity, non-damage: the debuff lands as a RuleGrant token on the target, so "did the cast
        // succeed" is one assertion with no wound pipeline in the way.
        private static RuntimeSpell DebuffSpell(string name, int threshold) =>
            new RuntimeSpell(
                new SpellDefinition(name, threshold,
                    new TargetSelector(18f, 1, 1, ETargetAffinity.Foe, RequireLineOfSight: false),
                    new Effect.AddRule("Slow", ELifetime.NextTrigger)),
                System.Array.Empty<ResolvedRule>());

        private static UnitActionContext NewActivation(TriggeredMoveTestContext ctx,
            DataBinding<UnitData> unit)
        {
            var unitCtx = new UnitActionContext(ctx, unit);
            unitCtx.Reset(unit);
            return unitCtx;
        }
    }

    // Picks the first castable spell and the first target, declines every assist, and keeps the request it
    // saw plus every target row's label - the observables for the relay's visibility.
    internal sealed class RecordingCastRequester : IPlayerRequestByID
    {
        public ChooseSpellRequest? SpellRequest { get; private set; }
        public List<string> TargetLabels { get; } = new List<string>();

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            switch (request)
            {
                case ChooseSpellRequest spellPick:
                    SpellRequest = spellPick;
                    return Task.FromResult((TReply)(object)CannedSpellPick.FirstCastable(spellPick));
                case SelectionRequest<UnitData> targetPick:
                    TargetLabels.AddRange(targetPick.ValidOptions.Select(o => o.Name));
                    return Task.FromResult((TReply)(object)targetPick.ValidOptions[0].Option);
                case CastAssistRequest:
                    return Task.FromResult((TReply)(object)0);
                default:
                    throw new System.InvalidOperationException("Unexpected request: " + request.GetType());
            }
        }
    }
}
