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
using FDG.Stages;
using FDG.Tests.RulesHarness;
using NUnit.Framework;

namespace FDG.Tests
{
    // The capability seam, across every capability that uses it.
    //
    // The engine used to answer "is this a caster / a transport / re-deployable?" by comparing against the
    // core definition - an identity check standing in for a capability. Two things that breaks, and both
    // are pinned below for EACH capability:
    //   * a SECOND rule conferring the same thing is invisible (Caster Group is not Caster, and cannot be
    //     granted as one: its X is a live model count and granted rules carry no arguments);
    //   * a capability that depends on live state cannot be expressed at all.
    //
    // Each test therefore confers the capability from a rule that is NOT the core one - which is exactly
    // what an identity check cannot see, and what the tests over the core rules can never catch.
    [TestFixture]
    public class CapabilityRuleQueriesTests
    {
        private GameDataStore _store = null!;
        private PlayerID _player;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _player = new PlayerID(Guid.NewGuid());
        }

        private static SpecialRuleDefinition Confers(string name, Effect capability,
            Condition? availableWhen = null) =>
            new(name,
                new[]
                {
                    new HookEntry(EHookID.Lifecycle_OnCapabilityQuery, availableWhen ?? new Condition.Always(),
                        capability, ELifetime.UntilEndOfGame),
                },
                Array.Empty<ActivatedAbility>());

        // --- transport ---

        [Test]
        public void ARuleOtherThanTransport_CanConferAHold()
        {
            TestRuleHarness harness = Harness(
                Confers("Bio-Gullet", new Effect.EnableTransport(new ValueSource.Literal(4))));
            IUnit unit = harness.BuildUnit("P1", 1, "Bio-Gullet");

            Assert.That(TransportUtilities.IsTransport(unit, harness.Evaluator), Is.True);
            Assert.That(TransportUtilities.GetCapacity(unit, harness.Evaluator), Is.EqualTo(4));
            Assert.That(unit.RuleDefinitions.Any(r => r.Definition == CoreRuleCatalog.Transport), Is.False,
                "the point: a hold conferred by something that is not the Transport rule.");
        }

        [Test]
        public void CoreTransport_StillConfersItsCapacity()
        {
            var harness = new TestRuleHarness();
            IUnit unit = harness.BuildUnit("P1", 1);
            harness.AttachRule(unit, CoreRuleCatalog.Transport, new RuleArgument.Int(6));

            Assert.That(TransportUtilities.IsTransport(unit, harness.Evaluator), Is.True);
            Assert.That(TransportUtilities.GetCapacity(unit, harness.Evaluator), Is.EqualTo(6),
                "capacity still comes off the rule's Arg(0), now by way of the capability answer.");
        }

        [Test]
        public void APlainUnit_IsNotATransport_AndHasNoCapacity()
        {
            var harness = new TestRuleHarness();
            IUnit unit = harness.BuildUnit("P1", 3);

            Assert.That(TransportUtilities.IsTransport(unit, harness.Evaluator), Is.False);
            Assert.That(TransportUtilities.GetCapacity(unit, harness.Evaluator), Is.EqualTo(0));
        }

        [Test]
        public void TwoConferringRules_DescribeOneHold_SoTheLargestCapacityWins()
        {
            TestRuleHarness harness = Harness(
                Confers("Small Hold", new Effect.EnableTransport(new ValueSource.Literal(2))),
                Confers("Big Hold", new Effect.EnableTransport(new ValueSource.Literal(5))));
            IUnit unit = harness.BuildUnit("P1", 1, "Small Hold", "Big Hold");

            Assert.That(TransportUtilities.GetCapacity(unit, harness.Evaluator), Is.EqualTo(5),
                "two transport rules describe the same hold twice; they do not add a second hold.");
        }

        // --- re-deployment ---

        [Test]
        public void ARuleOtherThanReDeployment_CanConferARedeploy()
        {
            TestRuleHarness harness = Harness(
                Confers("Forward Observer", new Effect.EnableReDeployment()));
            IUnit unit = harness.BuildUnit("P1", 1, "Forward Observer");

            Assert.That(CapabilityRuleQueries.CanReDeploy(unit, harness.Evaluator), Is.True);
        }

        [Test]
        public void CoreReDeployment_StillConfersIt_AndAPlainUnitDoesNot()
        {
            var harness = new TestRuleHarness();
            IUnit redeployer = harness.BuildUnit("P1", 1);
            harness.AttachRule(redeployer, CoreRuleCatalog.ReDeployment);

            Assert.That(CapabilityRuleQueries.CanReDeploy(redeployer, harness.Evaluator), Is.True);
            Assert.That(CapabilityRuleQueries.CanReDeploy(harness.BuildUnit("P2", 1), harness.Evaluator),
                Is.False);
        }

        // --- live state ---

        [Test]
        public void ACapabilityCanBeGatedOnLiveState()
        {
            // The thing an identity check cannot express at all. A Shaken unit's hold is shut here; the
            // corpus shape this serves is the casting-support family's "only if this unit isn't Shaken".
            TestRuleHarness harness = Harness(
                Confers("Fragile Hold", new Effect.EnableTransport(new ValueSource.Literal(3)),
                    new Condition.Not(new Condition.TokenPresent(TokenType.Shaken))));
            IUnit unit = harness.BuildUnit("P1", 1, "Fragile Hold");

            Assert.That(TransportUtilities.IsTransport(unit, harness.Evaluator), Is.True);

            harness.SeedToken(unit, TokenType.Shaken);

            Assert.That(TransportUtilities.IsTransport(unit, harness.Evaluator), Is.False,
                "the capability is re-answered on every ask, so live state gates it.");
            Assert.That(TransportUtilities.GetCapacity(unit, harness.Evaluator), Is.EqualTo(0));
        }

        [Test]
        public void CapabilitiesDoNotBleedIntoEachOther()
        {
            TestRuleHarness harness = Harness(
                Confers("Bio-Gullet", new Effect.EnableTransport(new ValueSource.Literal(4))));
            IUnit unit = harness.BuildUnit("P1", 1, "Bio-Gullet");

            Assert.That(CapabilityRuleQueries.CanCast(unit, harness.Evaluator), Is.False);
            Assert.That(CapabilityRuleQueries.CanReDeploy(unit, harness.Evaluator), Is.False);
        }

        // --- menu-action routing is by EFFECT, not by rule name ---

        [Test]
        public async Task ChooseAction_RoutesByTheAbilitysEffect_NotItsRuleName()
        {
            // ChooseActionStage used to route Disembark/Embark/Teleport to their stages by matching the
            // rule NAME - even though each already carries a marker Effect that exists for exactly that
            // purpose. A differently-named rule offering Effect.Teleport must route the same way.
            var requester = new RecordingActionRequester("Warp Step");
            var ctx = new TriggeredMoveTestContext(_store, requester);

            var model = new ModelData(0.5f, new List<Weapon>(), new Position(10f, 10f), _store);
            var unit = new UnitData(_player, "Warp Cult", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>>
                {
                    _store.GetDataBinding<ModelData>(_store.Create(model)),
                });
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            binding.GetValue().AttachRuleDefinition(new ResolvedRule("Warp Step",
                new SpecialRuleDefinition("Warp Step",
                    Array.Empty<HookEntry>(),
                    new[]
                    {
                        new ActivatedAbility(EHookID.Activation_OnActionChoice,
                            new Cost.OncePerActivation(),
                            new TargetSelector(0f, 1, 1, ETargetAffinity.Self, false),
                            new Effect.Teleport(),
                            new Condition.Always()),
                    })));
            _store.Create(new ArmyData(_player, new List<DataBinding<UnitData>> { binding }));

            var unitCtx = new UnitActionContext(ctx, binding);
            unitCtx.Reset(binding);

            bool routed = false;
            var stage = new ChooseActionStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.ToTeleport.Bind("ToTeleport");
            stage.ToTeleport.OnWillActivate += _ => routed = true;
            await stage.Enter(unitCtx);

            Assert.That(requester.OfferedOptions, Contains.Item("Warp Step"));
            Assert.That(routed, Is.True,
                "the effect is the discriminator; the rule's name is only its label.");
            Assert.That(unitCtx.PendingCustomAction?.RuleName, Is.EqualTo("Warp Step"));
        }

        private static TestRuleHarness Harness(params SpecialRuleDefinition[] definitions)
        {
            var harness = new TestRuleHarness();
            foreach (SpecialRuleDefinition definition in definitions) harness.Register(definition);
            return harness;
        }
    }
}
