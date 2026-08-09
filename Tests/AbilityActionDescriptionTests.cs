using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #370 — the Choose Action menu listed an ability action as its bare rule name ("Courage Buff") with
    // nothing saying what taking it does, while the built-in actions at least explain themselves when
    // greyed out ("Move (Procession Altar has already moved.)"). Every such rule already carries a
    // player-facing SpecialRuleDefinition.Description, and the request already has an OptionDescriptions
    // channel both front ends render; these pin that the two are now connected.
    [TestFixture]
    public class AbilityActionDescriptionTests
    {
        private const string BuffName = "Courage Buff";
        private const string BuffText =
            "Once per activation, before attacking, pick one friendly unit within 12 inches, " +
            "which gains Courage for its next relevant roll.";

        private GameDataStore _store = null!;
        private PlayerID _player;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _player = new PlayerID(Guid.NewGuid());
        }

        // A before-attack ability (the Procession Altar's buffs) carries its rule's description as the
        // menu row's subtext, keyed by the option string the row is labelled with.
        [Test]
        public async Task BeforeAttackAbility_CarriesItsRuleDescription()
        {
            var capture = new CapturingChoiceRequester(BuffName);
            var ctx = new TriggeredMoveTestContext(_store, capture);
            DataBinding<UnitData> unit = MakeUnitWithAbility(
                EHookID.Activation_OnBeforeAttackAction, BuffName, BuffText);
            UnitActionContext unitCtx = NewActivation(ctx, unit);

            var stage = new ChooseActionStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.ToBeforeAttackAction.Bind("ToBeforeAttackAction");
            await stage.Enter(unitCtx);

            Assert.That(capture.Request!.ValidOptions, Does.Contain(BuffName),
                "precondition: the ability is offered as a menu action");
            Assert.That(capture.Request!.OptionDescriptions, Is.Not.Null,
                "an ability action with a documented rule gives the request a description map");
            Assert.That(capture.Request!.OptionDescriptions![BuffName], Is.EqualTo(BuffText));
        }

        // The generic custom-action path (Activation_OnActionChoice, e.g. a Caster's spell rule) goes
        // through a different branch of the same menu build, so it is pinned separately.
        [Test]
        public async Task CustomAction_CarriesItsRuleDescription()
        {
            var capture = new CapturingChoiceRequester(BuffName);
            var ctx = new TriggeredMoveTestContext(_store, capture);
            DataBinding<UnitData> unit = MakeUnitWithAbility(
                EHookID.Activation_OnActionChoice, BuffName, BuffText);
            UnitActionContext unitCtx = NewActivation(ctx, unit);

            var stage = new ChooseActionStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.ToCustomAction.Bind("ToCustomAction");
            await stage.Enter(unitCtx);

            Assert.That(capture.Request!.ValidOptions, Does.Contain(BuffName),
                "precondition: the custom action is offered");
            Assert.That(capture.Request!.OptionDescriptions![BuffName], Is.EqualTo(BuffText));
        }

        // An undocumented rule (no corpus rule with an activated ability is in this state today, but the
        // engine's own catalog may grow one) contributes no entry rather than an empty line under the
        // button - and with nothing else to describe, the map stays null, which is what the front ends
        // read to skip the whole subtext path.
        [Test]
        public async Task UndocumentedRule_LeavesTheDescriptionMapNull()
        {
            var capture = new CapturingChoiceRequester(BuffName);
            var ctx = new TriggeredMoveTestContext(_store, capture);
            DataBinding<UnitData> unit = MakeUnitWithAbility(
                EHookID.Activation_OnBeforeAttackAction, BuffName, description: "");
            UnitActionContext unitCtx = NewActivation(ctx, unit);

            var stage = new ChooseActionStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.ToBeforeAttackAction.Bind("ToBeforeAttackAction");
            await stage.Enter(unitCtx);

            Assert.That(capture.Request!.ValidOptions, Does.Contain(BuffName),
                "precondition: the ability is still offered, it just has nothing to say");
            Assert.That(capture.Request!.OptionDescriptions, Is.Null);
        }

        // The offer itself is what carries the definition through to the menu, so pin that too: a gathered
        // offer knows the rule it came from, not just its name.
        [Test]
        public void GatheredOffer_CarriesTheRuleDefinition()
        {
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> unit = MakeUnitWithAbility(
                EHookID.Activation_OnBeforeAttackAction, BuffName, BuffText);

            IReadOnlyList<AbilityOffer> offers = ctx.RuleEvaluator.GatherOffers(
                new Rules.Dispatch.Contexts.BeforeAttackActionContext(unit.GetValue()));

            Assert.That(offers.Count, Is.EqualTo(1));
            Assert.That(offers[0].Definition, Is.Not.Null);
            Assert.That(offers[0].Definition!.Description, Is.EqualTo(BuffText));
        }

        // #369 — the description names the rule the buff CONFERS ("...which gains Courage..."), and that
        // is the rule the player does not know. It rides alongside as a structured (name, description)
        // pair so a front end can underline it where it already sits and hover the text.
        [Test]
        public async Task Description_CarriesTheRulesItNames_WithTheirOwnText()
        {
            var resolver = new RuleResolver();
            resolver.Register(CoreRuleCatalog.Courage);

            var capture = new CapturingChoiceRequester(BuffName);
            var ctx = new TriggeredMoveTestContext(_store, capture, ruleResolver: resolver);
            DataBinding<UnitData> unit = MakeUnitWithAbility(
                EHookID.Activation_OnBeforeAttackAction, BuffName, BuffText,
                effect: new Effect.AddRule("Courage", ELifetime.NextTrigger));
            UnitActionContext unitCtx = NewActivation(ctx, unit);

            var stage = new ChooseActionStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.ToBeforeAttackAction.Bind("ToBeforeAttackAction");
            await stage.Enter(unitCtx);

            Assert.That(capture.Request!.OptionDescriptionRules, Is.Not.Null);
            List<StringSelectionRequest.OptionRule> rules =
                capture.Request!.OptionDescriptionRules![BuffName];
            Assert.That(rules.Count, Is.EqualTo(1));
            Assert.That(rules[0].Name, Is.EqualTo("Courage"),
                "the name is carried verbatim so a front end can find it inside the description");
            Assert.That(rules[0].Description, Is.EqualTo(CoreRuleCatalog.Courage.Description));

            Assert.That(capture.Request!.OptionRules, Is.Null,
                "it is NOT the label's rule map - that matcher would underline the 'Courage' inside the "
                + "label 'Courage Buff' and explain the wrong rule");
        }

        // A rule the effect references but the description never spells out has nowhere on screen to be
        // underlined, so it is dropped rather than shipped as an entry no front end can place.
        [Test]
        public async Task Description_SkipsAReferencedRuleItNeverMentions()
        {
            var resolver = new RuleResolver();
            resolver.Register(CoreRuleCatalog.Courage);

            var capture = new CapturingChoiceRequester(BuffName);
            var ctx = new TriggeredMoveTestContext(_store, capture, ruleResolver: resolver);
            DataBinding<UnitData> unit = MakeUnitWithAbility(
                EHookID.Activation_OnBeforeAttackAction, BuffName,
                "Once per activation, buff a friend.",
                effect: new Effect.AddRule("Courage", ELifetime.NextTrigger));
            UnitActionContext unitCtx = NewActivation(ctx, unit);

            var stage = new ChooseActionStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.ToBeforeAttackAction.Bind("ToBeforeAttackAction");
            await stage.Enter(unitCtx);

            Assert.That(capture.Request!.OptionDescriptions![BuffName],
                Is.EqualTo("Once per activation, buff a friend."),
                "precondition: the description is still shown");
            Assert.That(capture.Request!.OptionDescriptionRules, Is.Null);
        }

        // --- Helpers ---

        // A lone weaponless unit (its own only friendly) carrying one self-targeting activated ability at
        // the given hook - it cannot Shoot or Charge, so the ability action is the only thing in the menu
        // that could carry a description.
        private DataBinding<UnitData> MakeUnitWithAbility(EHookID hook, string ruleName, string description,
            Effect? effect = null)
        {
            var ability = new ActivatedAbility(
                hook, new Cost.OncePerActivation(),
                new TargetSelector(12f, 1, 1, ETargetAffinity.Friend, false),
                effect ?? new Effect.GrantToken(new TokenType("BuffFired"), new ValueSource.Literal(1),
                    new TokenClearTrigger.ManualOnly()),
                new Condition.Always());
            var rule = new SpecialRuleDefinition(ruleName, Array.Empty<HookEntry>(), new[] { ability },
                Description: description);

            // Off-origin so GetIsOnBattlefield sees the unit as placed (a unit at the origin reads as an
            // unplaced reserve, which is never an eligible target - even for its own Friend abilities).
            var model = new ModelData(0.5f, new List<Weapon>(), new Position(1f, 0f), _store);
            var modelBindings = new List<DataBinding<ModelData>>
            {
                _store.GetDataBinding<ModelData>(_store.Create(model)),
            };
            var unit = new UnitData(_player, "Procession Altar", quality: 4, defense: 4,
                modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            binding.GetValue().AttachRuleDefinition(new ResolvedRule(rule.Name, rule));
            _store.Create(new ArmyData(_player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        private static UnitActionContext NewActivation(IGameContext ctx, DataBinding<UnitData> unit)
        {
            var unitCtx = new UnitActionContext(ctx, unit);
            unitCtx.Reset(unit);
            return unitCtx;
        }
    }
}
