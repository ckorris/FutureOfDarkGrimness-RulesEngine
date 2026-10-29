using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #408 - an activation-start choice never traps the player. Discord: "when you activate a veteran prime
    // master with versatile attack you cannot back out of the main activation stage with backspace. you are
    // forced to pick a mode and complete an action". Owner's design (2026-10-28): apply the pick at once, as
    // before, so the action menu and the AI see it - but snapshot the unit's tokens when the activation starts
    // and restore them on a back-out. A rule-granting ability changes only tokens (the grant and the spent
    // "used" marker), so the restore undoes it completely:
    //   * Back at the "pick one effect" prompt un-picks the unit, nothing granted or spent;
    //   * after picking, the action menu is still cancellable, and backing out there undoes the pick;
    //   * a Versatile Defense grant the start-of-activation sweep retired comes back on a back-out;
    //   * an ability that is not a rule grant stays a one-way door (no Back, menu not cancellable).
    // Drives the real MainUnitActionStage, so ActivationStartStage -> ChooseActionStage wiring is under test.
    [TestFixture]
    public class ActivationStartBackOutTests
    {
        private const string Versatile = "Versatile Attack";
        private const string Piercing = "Versatile Attack (Piercing)";
        private const string Precision = "Versatile Attack (Precision)";
        private const string Guarded = "Versatile Defense (Guard)";
        private const string Marked = "Marking Shot";

        private GameDataStore _store = null!;
        private RuleResolver _resolver = null!;
        private ScriptedRequester _requester = null!;
        private TriggeredMoveTestContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _resolver = new RuleResolver();
            _resolver.Register(PickOne(Versatile, ("AP(+1)", new Effect.AddRule(Piercing, ELifetime.ThisActivation)),
                ("+1 to hit", new Effect.AddRule(Precision, ELifetime.ThisActivation))));
            _resolver.Register(Helper(Piercing));
            _resolver.Register(Helper(Precision));
            _resolver.Register(Helper(Guarded));
            _resolver.Register(PickOne(Marked,
                ("Mark", new Effect.GrantToken(TokenType.Fatigued, new ValueSource.Literal(1),
                    new TokenClearTrigger.ActivationEnd())),
                ("Mark twice", new Effect.GrantToken(TokenType.Fatigued, new ValueSource.Literal(2),
                    new TokenClearTrigger.ActivationEnd()))));
            _requester = new ScriptedRequester();
            _ctx = new TriggeredMoveTestContext(_store, _requester, ruleResolver: _resolver);
        }

        [Test]
        public async Task BackAtTheEffectPick_UnpicksTheUnit_WithNothingGrantedOrSpent()
        {
            DataBinding<UnitData> unit = MakeUnit(Versatile);
            _requester.EffectReplies.Enqueue(ChooseAbilityEffectRequest.CANCEL);

            (bool backedOut, bool reconciled) = await RunActivation(unit);

            Assert.That(_requester.EffectRequests.Single().AllowCancel, Is.True,
                "a rule-granting pick can be backed out of");
            Assert.That(backedOut, Is.True, "Back leaves through MainUnitActionStage.OnBackedOut");
            Assert.That(reconciled, Is.False, "and never through the exit that marks the unit activated");
            Assert.That(_requester.MenuCount, Is.EqualTo(0), "the action menu is never shown");
            Assert.That(Grants(unit), Is.Empty);
            Assert.That(OffersNow(unit), Has.Count.EqualTo(2), "nothing was spent: both effects are offered again");
        }

        [Test]
        public async Task AfterPicking_TheMenuStillBacksOut_AndTheBackOutUndoesThePick()
        {
            DataBinding<UnitData> unit = MakeUnit(Versatile);
            _requester.EffectReplies.Enqueue(0);
            _requester.MenuReplies.Enqueue(null);

            (bool backedOut, _) = await RunActivation(unit);

            Assert.That(_requester.MenuAllowCancel.Single(), Is.True,
                "picking a mode no longer forces the unit to act - the reported trap");
            Assert.That(_requester.GrantsSeenAtMenu.Single(), Is.EqualTo(new[] { Piercing }),
                "the pick is live while the menu is up, so the menu and the AI plan with it");
            Assert.That(backedOut, Is.True);
            Assert.That(Grants(unit), Is.Empty, "backing out of the menu undoes the grant");
            Assert.That(OffersNow(unit), Has.Count.EqualTo(2), "and the once-per-activation marker");
        }

        [Test]
        public async Task AfterPicking_ActingKeepsThePick()
        {
            DataBinding<UnitData> unit = MakeUnit(Versatile);
            _requester.EffectReplies.Enqueue(1);
            _requester.MenuReplies.Enqueue(ChooseActionStage.PASS_CHOICE_NAME);

            (bool backedOut, bool reconciled) = await RunActivation(unit);

            Assert.That(backedOut, Is.False);
            Assert.That(reconciled, Is.True);
            Assert.That(Grants(unit), Is.EqualTo(new[] { Precision }), "a real action keeps what was picked");
        }

        [Test]
        public async Task BackingOut_RestoresAGrantTheStartOfActivationSweepRetired()
        {
            // Versatile Defense's grant lasts "until the unit's next activation", and ActivationStartStage
            // sweeps it first thing. An activation the player backs out of never happened, so it comes back.
            DataBinding<UnitData> unit = MakeUnit();
            unit.GetValue().Tokens.AddToken(new Token(TokenType.RuleGrant, 1,
                new TokenClearTrigger.CustomHook(EHookID.Activation_OnActivationStart),
                Payload: new TokenPayload.RuleGrant(Guarded, ELifetime.UntilNextActivation)));
            Assert.That(Grants(unit), Is.EqualTo(new[] { Guarded }), "fixture: the defense buff is held");
            _requester.MenuReplies.Enqueue(null);

            (bool backedOut, _) = await RunActivation(unit);

            Assert.That(_requester.GrantsSeenAtMenu.Single(), Is.Empty, "swept when the activation began");
            Assert.That(backedOut, Is.True);
            Assert.That(Grants(unit), Is.EqualTo(new[] { Guarded }), "and restored by the back-out");
        }

        [Test]
        public async Task AnAbilityThatIsNotARuleGrant_StaysAOneWayDoor()
        {
            DataBinding<UnitData> unit = MakeUnit(Marked);
            _requester.EffectReplies.Enqueue(0);
            _requester.MenuReplies.Enqueue(ChooseActionStage.PASS_CHOICE_NAME);

            await RunActivation(unit);

            Assert.That(_requester.EffectRequests.Single().AllowCancel, Is.False,
                "only rule grants are known to be undone by a token restore");
            Assert.That(_requester.MenuAllowCancel.Single(), Is.False, "so the activation is committed, as before");
        }

        // --- helpers ---

        private async Task<(bool BackedOut, bool Reconciled)> RunActivation(DataBinding<UnitData> unit)
        {
            var turnContext = new SingleTurnContext(_ctx, unit.GetValue().PlayerID,
                new List<DataBinding<UnitData>> { unit });
            turnContext.ChooseUnitToActivate(unit);

            var main = new MainUnitActionStage(_ctx, new NoOpLayer<ISingleTurnContext>());
            bool backedOut = false, reconciled = false;
            main.OnBackedOut.Bind("backedOut");
            main.ToReconcileEndOfActivation.Bind("reconcile");
            main.OnBackedOut.OnWillActivate += _ => backedOut = true;
            main.ToReconcileEndOfActivation.OnWillActivate += _ => reconciled = true;
            _requester.Unit = unit.GetValue();

            await main.Enter(turnContext);
            return (backedOut, reconciled);
        }

        private IReadOnlyList<AbilityOffer> OffersNow(DataBinding<UnitData> unit) =>
            _ctx.RuleEvaluator.GatherOffers(new ActivationStartContext(unit.GetValue()));

        private static string[] Grants(DataBinding<UnitData> unit) => GrantsOf(unit.GetValue());

        private static string[] GrantsOf(IUnit unit) =>
            unit.Tokens.GetAllTokens(TokenType.RuleGrant)
                .Select(t => t.Payload).OfType<TokenPayload.RuleGrant>()
                .Select(g => g.RuleName).ToArray();

        private static SpecialRuleDefinition PickOne(string name, params (string Label, Effect Effect)[] effects) =>
            new(name, Array.Empty<HookEntry>(),
                effects.Select(e => new ActivatedAbility(EHookID.Activation_OnActivationStart,
                    new Cost.OncePerActivation(),
                    new TargetSelector(RangeInches: 0f, MinCount: 1, MaxCount: 1, ETargetAffinity.Self,
                        RequireLineOfSight: false),
                    e.Effect, new Condition.Always(), Label: e.Label)).ToArray());

        private static SpecialRuleDefinition Helper(string name) =>
            new(name, Array.Empty<HookEntry>(), Array.Empty<ActivatedAbility>());

        // A rifle and an enemy in range, so the menu has real options (Move, Shoot, Pass).
        private DataBinding<UnitData> MakeUnit(params string[] ruleNames)
        {
            PlayerID player = new PlayerID(Guid.NewGuid());
            DataBinding<UnitData> unit = MakeUnitFor(player, new Position(0f, 0f, 0f));
            foreach (string ruleName in ruleNames)
                unit.GetValue().AttachRuleDefinition(_resolver.Resolve(ruleName));
            _store.Create(new ArmyData(player, new List<DataBinding<UnitData>> { unit }));

            PlayerID enemy = new PlayerID(Guid.NewGuid());
            _store.Create(new ArmyData(enemy, new List<DataBinding<UnitData>> { MakeUnitFor(enemy, new Position(5f, 0f, 0f)) }));
            return unit;
        }

        private int _nextTeamIndex;

        private DataBinding<UnitData> MakeUnitFor(PlayerID player, Position position)
        {
            _store.Create(new TeamData(_nextTeamIndex++, new List<PlayerID> { player }));
            var weapons = new List<Weapon> { new Weapon("Rifle", rangeInches: 24f, attacks: 1, armorPenetration: 0) };
            var model = new ModelData(baseRadiusInches: 0.5f, weapons: weapons, initialPosition: position,
                gameDataStore: _store);
            var unit = new UnitData(player, "Squad", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { _store.GetDataBinding<ModelData>(_store.Create(model)) });
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }

        // Effect picks and menu replies come from queues; each menu records whether it was cancellable and
        // what grants the unit held at that moment.
        private sealed class ScriptedRequester : IPlayerRequestByID
        {
            public IUnit? Unit { get; set; }
            public Queue<int> EffectReplies { get; } = new Queue<int>();
            public Queue<string?> MenuReplies { get; } = new Queue<string?>();
            public List<ChooseAbilityEffectRequest> EffectRequests { get; } = new List<ChooseAbilityEffectRequest>();
            public List<bool> MenuAllowCancel { get; } = new List<bool>();
            public List<string[]> GrantsSeenAtMenu { get; } = new List<string[]>();
            public int MenuCount => MenuAllowCancel.Count;

            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply>
            {
                switch (request)
                {
                    case ChooseAbilityEffectRequest effect:
                        EffectRequests.Add(effect);
                        return Task.FromResult((TReply)(object)EffectReplies.Dequeue());
                    case ChooseActionRequest menu:
                        MenuAllowCancel.Add(menu.AllowCancel);
                        GrantsSeenAtMenu.Add(GrantsOf(Unit!));
                        return Task.FromResult((TReply)(object)MenuReplies.Dequeue()!);
                }
                throw new InvalidOperationException("Unexpected request type: " + request.GetType());
            }
        }
    }
}
