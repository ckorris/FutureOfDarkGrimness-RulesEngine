using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.StageResolution;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #197 Vengeance - "place N markers on the unit that destroyed this one; friendly units get +N to hit
    // against it". The read side is P14b's PersistentHitBonusMarker verbatim (counted on every friendly
    // attack, never removed), so the only new engine work is the PLACEMENT: a rule borne by the dead unit,
    // firing from the Subject seat at Shooting_OnUnitDestroyed, whose tokens land on the KILLER. Nothing
    // could express that before - on the passive path RuleInvocation.EffectiveTarget is always the bearer -
    // hence Effect.GrantTokenToKiller over the new IHasKillerUnit capability.
    //
    // N is a LITERAL 1, not a carrier count (owner-signed 2026-07-30). All three corpus sites buy Vengeance
    // through the "Honor-Bound" item on an Affects.One upgrade section, so exactly one per unit; and the
    // filed blocker ("needs a game-start carrier count") was doubly wrong - see
    // RuleCarrierCount_AtThisHook_IsZero_WhichIsWhyTheCountIsALiteral below.
    [TestFixture]
    public class VengeanceRuleIntegrationTests
    {
        private static SpecialRuleDefinition VengeanceDefinition() => new("Vengeance",
            new[]
            {
                new HookEntry(EHookID.Shooting_OnUnitDestroyed, new Condition.Always(),
                    new Effect.GrantTokenToKiller(TokenType.PersistentHitBonus, new ValueSource.Literal(1),
                        new TokenClearTrigger.ManualOnly()),
                    ELifetime.UntilEndOfGame, ERuleSeat.Subject),
            },
            Array.Empty<ActivatedAbility>());

        private GameDataStore _store = null!;
        private RuleResolver _resolver = null!;
        private PlayerID _avenger;
        private PlayerID _killer;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _resolver = CoreRuleCatalog.CreateResolver();
            _resolver.Register(VengeanceDefinition());
            _avenger = new PlayerID(Guid.NewGuid());
            _killer = new PlayerID(Guid.NewGuid());
        }

        [Test]
        public async Task WhenTheBearerIsDestroyed_TheKillerIsMarked()
        {
            (IUnit victim, IUnit slayer) = MakeUnits();
            KillEveryModel(victim);

            await UnitDestructionNotifier.NotifyUnitDestroyed(Ctx(), victim, slayer);

            Assert.That(slayer.Tokens.GetTokenCount(TokenType.PersistentHitBonus), Is.EqualTo(1),
                "one Honor-Bound model -> one marker, placed on the unit that did the killing");
        }

        [Test]
        public async Task TheMarkerLandsOnTheKiller_NotOnTheBearer()
        {
            // The whole point of the slice: every other passive token grant lands on EffectiveTarget,
            // which is the bearer. Swapping GrantTokenToKiller back to GrantToken passes the test above
            // (the count is still 1) and fails this one.
            (IUnit victim, IUnit slayer) = MakeUnits();
            KillEveryModel(victim);

            await UnitDestructionNotifier.NotifyUnitDestroyed(Ctx(), victim, slayer);

            Assert.That(victim.Tokens.HasToken(TokenType.PersistentHitBonus), Is.False,
                "the corpse must not mark itself");
            Assert.That(slayer.Tokens.HasToken(TokenType.PersistentHitBonus), Is.True);
        }

        [Test]
        public async Task TheMarkerIsReadAsAnAttackerBonus_AgainstTheKiller()
        {
            // Standing lesson 1: emission is not consumption. TargetMarkerSpend is the one seam the hit
            // and save stages read attacker-bonus markers through (its stage folding is pinned by
            // TargetBonusMarkerTests), so a marker it cannot see is a marker that does nothing in play.
            (IUnit victim, IUnit slayer) = MakeUnits();
            KillEveryModel(victim);
            IGameContext context = Ctx();

            await UnitDestructionNotifier.NotifyUnitDestroyed(context, victim, slayer);
            int bonus = await TargetMarkerSpend.ConsumeNet(context, _avenger, slayer, ERollKind.Hit);

            Assert.That(bonus, Is.EqualTo(1), "+1 to hit for friendly attacks against the killer");
        }

        [Test]
        public async Task TheMarkerIsPersistent_AndSurvivesRepeatedAttacks()
        {
            // "for the rest of the game", not a one-shot: PersistentHitBonus is never consumed, unlike its
            // Spendable sibling. Also the reason the clear trigger is ManualOnly - an OwnerDestroyed marker
            // would be self-cancelling here, its owner being dead by construction.
            (IUnit victim, IUnit slayer) = MakeUnits();
            KillEveryModel(victim);
            IGameContext context = Ctx();

            await UnitDestructionNotifier.NotifyUnitDestroyed(context, victim, slayer);
            await TargetMarkerSpend.ConsumeNet(context, _avenger, slayer, ERollKind.Hit);
            int second = await TargetMarkerSpend.ConsumeNet(context, _avenger, slayer, ERollKind.Hit);

            Assert.That(second, Is.EqualTo(1), "the marker is counted again on the next attack");
            Assert.That(slayer.Tokens.GetTokenCount(TokenType.PersistentHitBonus), Is.EqualTo(1));
        }

        [Test]
        public async Task TwoAvengedUnits_StackTheirMarkersOnASharedKiller()
        {
            // Nothing caps the marker count (the rule text sets no maximum, unlike the Frenzy family's
            // "max 2"), so a unit that kills two Honor-Bound units carries +2. Recorded as a decision
            // rather than an accident: MaxTotal is available on the effect and deliberately left at 0.
            (IUnit victim, IUnit slayer) = MakeUnits();
            IUnit secondVictim = MakeVengefulUnit("Royal Guard");
            KillEveryModel(victim);
            KillEveryModel(secondVictim);

            await UnitDestructionNotifier.NotifyUnitDestroyed(Ctx(), victim, slayer);
            await UnitDestructionNotifier.NotifyUnitDestroyed(Ctx(), secondVictim, slayer);

            Assert.That(slayer.Tokens.GetTokenCount(TokenType.PersistentHitBonus), Is.EqualTo(2));
        }

        [Test]
        public async Task AKillerlessDeath_MarksNothing()
        {
            // A rout or a dangerous-terrain death has no one to avenge against. UnitDestructionNotifier
            // returns before the killer-seat evaluation, and UnitDestroyedContext could not be built
            // anyway - IHasKillerUnit.KillerUnit is non-null by construction.
            (IUnit victim, IUnit slayer) = MakeUnits();
            KillEveryModel(victim);

            await UnitDestructionNotifier.NotifyUnitDestroyed(Ctx(), victim, killer: null);

            Assert.That(slayer.Tokens.HasToken(TokenType.PersistentHitBonus), Is.False);
        }

        [Test]
        public async Task AUnitWithoutVengeance_MarksNothing()
        {
            (_, IUnit slayer) = MakeUnits();
            IUnit ordinary = MakeVengefulUnit("Warriors", attachVengeance: false);
            KillEveryModel(ordinary);

            await UnitDestructionNotifier.NotifyUnitDestroyed(Ctx(), ordinary, slayer);

            Assert.That(slayer.Tokens.HasToken(TokenType.PersistentHitBonus), Is.False);
        }

        [Test]
        public void RuleCarrierCount_AtThisHook_IsZero_WhichIsWhyTheCountIsALiteral()
        {
            // Pins the finding that struck the filed blocker, so it is not re-filed: ValueSource
            // .RuleCarrierCount counts LIVING carriers, and at this hook the bearer's models are all dead
            // by definition - it resolves to 0, i.e. no markers at all. A "carriers at game start" variant
            // would be worse, not better: ListCompiler folds item rules onto the UNIT, and RuleCarrierCount
            // credits every model of a unit that holds the rule, so it would return the unit's starting
            // model count (5 for Warriors) - a permanent +5 to hit. Hence ValueSource.Literal(1).
            (IUnit victim, IUnit slayer) = MakeUnits();
            KillEveryModel(victim);

            int carriers = new ValueSource.RuleCarrierCount().Resolve(new RuleInvocation(
                new UnitDestroyedContext(victim, slayer), victim,
                Array.Empty<RuleArgument>(), Definition: VengeanceDefinition()));

            Assert.That(carriers, Is.EqualTo(0),
                "a living-carrier count is a guaranteed no-op at the destruction hook");
        }

        private TriggeredMoveTestContext Ctx() =>
            new(_store, new NoRequester(), ruleResolver: _resolver);

        private static void KillEveryModel(IUnit unit)
        {
            foreach (DataBinding<ModelData> model in ((UnitData)unit).ModelBindings)
            {
                model.GetValue().DealWounds(model.GetValue().TotalWounds);
            }
        }

        private (IUnit Victim, IUnit Slayer) MakeUnits()
        {
            IUnit victim = MakeVengefulUnit("Warriors");

            var slayerModel = new ModelData(0.75f, new List<Weapon>(), new Position(30f, 20f), _store);
            var slayer = new UnitData(_killer, "Raiders", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>>
                {
                    _store.GetDataBinding<ModelData>(_store.Create(slayerModel)),
                });
            _store.Create(slayer);

            return (victim, slayer);
        }

        private IUnit MakeVengefulUnit(string name, bool attachVengeance = true)
        {
            // Five models, matching the corpus unit the rule is bought on - so a count that reads the
            // unit's models instead of the item cannot pass by coincidence.
            var models = new List<DataBinding<ModelData>>();
            for (int i = 0; i < 5; i++)
            {
                var model = new ModelData(0.75f, new List<Weapon>(), new Position(20f + i, 20f), _store);
                models.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(_avenger, name, quality: 4, defense: 4, modelBindings: models);
            if (attachVengeance)
            {
                // Attached at unit scope, which is where ListCompiler folds an item's rules.
                unit.AttachRuleDefinition(new ResolvedRule("Vengeance", VengeanceDefinition(),
                    Array.Empty<RuleArgument>()));
            }

            _store.Create(unit);
            return unit;
        }

        private sealed class NoRequester : IPlayerRequestByID
        {
            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply> =>
                throw new InvalidOperationException(
                    "Vengeance places its markers unprompted: " + request.GetType().Name);
        }
    }
}
