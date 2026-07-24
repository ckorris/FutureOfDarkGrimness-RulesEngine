using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.SaveLoad;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #197 Teleport: "once per activation, before attacking, place each model fully within 6in of its
    // position." A book-listed rule (CoreRuleCatalog.Teleport) offered at Activation_OnActionChoice, routed by
    // name from ChooseActionStage to TeleportStage, which runs a 6in placement and loops back. Layered - it
    // sets neither HasMoved nor HasAttacked. Reuses the reposition machinery (PlaceObjectsRequest's per-model
    // MaxDistanceFromStartInches), whose placement mechanics are covered by RepositionAtActivationTests.
    [TestFixture]
    public class TeleportRuleIntegrationTests
    {
        private static readonly TokenType UsedMarker = new("AbilityUsed:Teleport");

        private GameDataStore _store = null!;
        private PlayerID _player;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _player = new PlayerID(Guid.NewGuid());
        }

        // --- catalog + eval layer ---

        [Test]
        public void CatalogResolver_ResolvesTeleportByName_AtActionChoice()
        {
            RuleResolver resolver = CoreRuleCatalog.CreateResolver();

            Assert.That(resolver.TryResolve(CoreRuleCatalog.TeleportRuleName, out ResolvedRule resolved), Is.True,
                "a book that references 'Teleport' must resolve it from the catalog (it is a live rule now).");
            Assert.That(resolved.Definition.Activated.Single().TriggerHook,
                Is.EqualTo(EHookID.Activation_OnActionChoice));
            Assert.That(resolved.Definition.Activated.Single().Effect, Is.TypeOf<Effect.Teleport>());
        }

        [Test]
        public void GatherOffers_AtActionChoice_OffersTeleport()
        {
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> unit = MakeTeleportUnit(new Position(10f, 10f));

            var offers = ctx.RuleEvaluator.GatherOffers(new ActionChoiceContext(unit.GetValue()));

            Assert.That(offers.Any(o => o.RuleName == CoreRuleCatalog.TeleportRuleName), Is.True);
        }

        [Test]
        public void GatherOffers_OncePerActivationUsed_TeleportNotOfferedAgain()
        {
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> unit = MakeTeleportUnit(new Position(10f, 10f));
            unit.GetValue().Tokens.AddToken(new Token(UsedMarker, 1, new TokenClearTrigger.ActivationEnd()));

            var offers = ctx.RuleEvaluator.GatherOffers(new ActionChoiceContext(unit.GetValue()));

            Assert.That(offers.Any(o => o.RuleName == CoreRuleCatalog.TeleportRuleName), Is.False);
        }

        // --- #265 all-models gate ---

        [Test]
        public void GatherOffers_HeroWithTeleportJoinsASquadWithout_TeleportNotOffered()
        {
            // The reported bug: a hero carrying Teleport joined a squad that doesn't have it, and Teleport
            // was offered - which teleports EVERY model in the combined unit, the whole squad included.
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> unit = MakeUnitWithJoinedHero(heroHasTeleport: true, hostHasTeleport: false);

            var offers = ctx.RuleEvaluator.GatherOffers(new ActionChoiceContext(unit.GetValue()));

            Assert.That(offers.Any(o => o.RuleName == CoreRuleCatalog.TeleportRuleName), Is.False,
                "the unit teleports as a whole, so it may only do so when every model has the rule.");
        }

        [Test]
        public void GatherOffers_HeroWithoutTeleportJoinsASquadWithIt_TeleportNotOffered()
        {
            // The other direction: the hero breaks the squad's own Teleport, because the hero would be
            // carried along by a placement it has no rule entitling it to.
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> unit = MakeUnitWithJoinedHero(heroHasTeleport: false, hostHasTeleport: true);

            var offers = ctx.RuleEvaluator.GatherOffers(new ActionChoiceContext(unit.GetValue()));

            Assert.That(offers.Any(o => o.RuleName == CoreRuleCatalog.TeleportRuleName), Is.False);
        }

        [Test]
        public void GatherOffers_BothHeroAndSquadHaveTeleport_StillOffered()
        {
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> unit = MakeUnitWithJoinedHero(heroHasTeleport: true, hostHasTeleport: true);

            var offers = ctx.RuleEvaluator.GatherOffers(new ActionChoiceContext(unit.GetValue()));

            Assert.That(offers.Any(o => o.RuleName == CoreRuleCatalog.TeleportRuleName), Is.True,
                "the gate must not break the legitimate case - every model has Teleport here.");
        }

        [Test]
        public void GatherOffers_TeleportGrantedToTheUnit_StillOffered_EvenWithAHeroThatLacksIt()
        {
            // Teleport Aura says "this model AND ITS UNIT get Teleport", and it lands as a unit-level
            // RuleGrant token. AllModelsHaveThisRule counts a unit grant for EVERY living model, hero
            // included, so the gate must not kill the aura. The token is added directly here (the way
            // AllModelsRuleGateIntegrationTests grants Stealth) rather than by firing the aura's
            // Lifecycle_OnUnitCreated hook - the grant is the state under test, not how it got there.
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester(),
                ruleResolver: CoreRuleCatalog.CreateResolver());
            DataBinding<UnitData> unit = MakeUnitWithJoinedHero(heroHasTeleport: false, hostHasTeleport: false);
            unit.GetValue().Tokens.AddToken(new Token(TokenType.RuleGrant, 1, new TokenClearTrigger.ManualOnly(),
                Payload: new TokenPayload.RuleGrant(CoreRuleCatalog.TeleportRuleName, ELifetime.UntilEndOfGame)));

            var offers = ctx.RuleEvaluator.GatherOffers(new ActionChoiceContext(unit.GetValue()));

            Assert.That(offers.Any(o => o.RuleName == CoreRuleCatalog.TeleportRuleName), Is.True,
                "a unit-wide grant covers every model, so the all-models gate passes.");
        }

        // --- ChooseActionStage routing ---

        [Test]
        public async Task ChooseAction_HasTeleport_NotAttacked_SurfacesAndRoutesToTeleport()
        {
            var requester = new RecordingActionRequester("Teleport");
            var ctx = new TriggeredMoveTestContext(_store, requester);
            DataBinding<UnitData> unit = MakeTeleportUnit(new Position(10f, 10f));
            var unitCtx = NewActivation(ctx, unit);

            bool routed = false;
            var stage = new ChooseActionStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.ToTeleport.Bind("ToTeleport");
            stage.ToTeleport.OnWillActivate += _ => routed = true;
            await stage.Enter(unitCtx);

            Assert.That(requester.OfferedOptions, Contains.Item("Teleport"));
            Assert.That(routed, Is.True);
            Assert.That(unitCtx.PendingCustomAction?.RuleName, Is.EqualTo("Teleport"),
                "the chosen Teleport offer is stashed so TeleportStage can pay its cost.");
        }

        [Test]
        public async Task ChooseAction_AfterAttacking_DoesNotOfferTeleport()
        {
            var requester = new RecordingActionRequester("Pass");
            var ctx = new TriggeredMoveTestContext(_store, requester);
            DataBinding<UnitData> unit = MakeTeleportUnit(new Position(10f, 10f));
            var unitCtx = NewActivation(ctx, unit);
            unitCtx.RegisterAttackedFinished(); // Teleport is a "before attacking" ability.

            var stage = new ChooseActionStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.ToReconcileEndOfActivation.Bind("Pass");
            await stage.Enter(unitCtx);

            Assert.That(requester.OfferedOptions, Does.Not.Contain("Teleport"),
                "once the unit has attacked, Teleport is no longer offered.");
        }

        // --- TeleportStage ---

        [Test]
        public async Task TeleportStage_Accepted_PaysCost_PlacesModels_LoopsBack_AndIsLayered()
        {
            var requester = new CannedPlaceRequester(new Position(13f, 10f)); // 3in from start (10,10), within 6in
            var ctx = new TriggeredMoveTestContext(_store, requester);
            DataBinding<UnitData> unit = MakeTeleportUnit(new Position(10f, 10f));
            var unitCtx = NewActivation(ctx, unit);
            unitCtx.SetPendingCustomAction(TeleportOffer(ctx, unit));

            bool finished = false;
            var stage = new TeleportStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("OnFinished");
            stage.OnFinished.OnWillActivate += _ => finished = true;
            await stage.Enter(unitCtx);

            Assert.That(unit.GetValue().Tokens.HasToken(UsedMarker), Is.True, "once-per-activation cost paid.");
            Assert.That(unit.GetValue().Models.First().Position.x, Is.EqualTo(13f).Within(0.001f),
                "the model was placed at the teleport destination.");
            Assert.That(finished, Is.True, "loops back to Choose Action.");
            Assert.That(unitCtx.HasMoved, Is.False, "teleport is layered - it does not consume the move.");
            Assert.That(unitCtx.HasAttacked, Is.False, "teleport is layered - it does not consume the attack.");
            Assert.That(unitCtx.PendingCustomAction, Is.Null, "the pending offer is cleared.");
        }

        [Test]
        public async Task TeleportStage_PlacementRequest_Carries6InchRadius_AndAllowsCancel()
        {
            var requester = new CannedPlaceRequester(new Position(10f, 10f)); // stands still
            var ctx = new TriggeredMoveTestContext(_store, requester);
            DataBinding<UnitData> unit = MakeTeleportUnit(new Position(10f, 10f));
            var unitCtx = NewActivation(ctx, unit);
            unitCtx.SetPendingCustomAction(TeleportOffer(ctx, unit));

            var stage = new TeleportStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("OnFinished");
            await stage.Enter(unitCtx);

            Assert.That(requester.LastRequest, Is.Not.Null);
            Assert.That(requester.LastRequest!.MaxDistanceFromStartInches,
                Is.EqualTo(TeleportStage.TELEPORT_RANGE_INCHES).Within(0.001f));
            Assert.That(requester.LastRequest.AllowCancel, Is.True, "'you may' place - declining is legal.");
        }

        [Test]
        public async Task TeleportStage_Cancelled_LeavesModelsPut_DoesNotPayCost_ButLoopsBack()
        {
            var ctx = new TriggeredMoveTestContext(_store, new CancellingPlaceRequester());
            DataBinding<UnitData> unit = MakeTeleportUnit(new Position(10f, 10f));
            var unitCtx = NewActivation(ctx, unit);
            unitCtx.SetPendingCustomAction(TeleportOffer(ctx, unit));

            bool finished = false;
            var stage = new TeleportStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("OnFinished");
            stage.OnFinished.OnWillActivate += _ => finished = true;
            await stage.Enter(unitCtx);

            Assert.That(unit.GetValue().Models.First().Position.x, Is.EqualTo(10f).Within(0.001f),
                "backing out leaves the model where it was.");
            Assert.That(unit.GetValue().Tokens.HasToken(UsedMarker), Is.False,
                "cancelling did not use the teleport - the once-per-activation cost stays unspent.");
            Assert.That(finished, Is.True, "still loops back to Choose Action.");
        }

        // --- helpers ---

        private static AbilityOffer TeleportOffer(IGameContext ctx, DataBinding<UnitData> unit) =>
            ctx.RuleEvaluator.GatherOffers(new ActionChoiceContext(unit.GetValue()))
                .Single(o => o.RuleName == CoreRuleCatalog.TeleportRuleName);

        private static UnitActionContext NewActivation(IGameContext ctx, DataBinding<UnitData> unit)
        {
            var unitCtx = new UnitActionContext(ctx, unit);
            unitCtx.Reset(unit);
            return unitCtx;
        }

        private DataBinding<UnitData> MakeTeleportUnit(params Position[] positions)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            foreach (Position pos in positions)
            {
                var model = new ModelData(0.5f, new List<Weapon>(), pos, _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(_player, "Blink Squad", quality: 4, defense: 4,
                modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            binding.GetValue().AttachRuleDefinition(
                new ResolvedRule(CoreRuleCatalog.TeleportRuleName, CoreRuleCatalog.Teleport));

            _store.Create(new ArmyData(_player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        // #265 — a 3-model host with a Hero merged into it, each side optionally carrying Teleport (or the
        // hero carrying Teleport Aura, which grants it to the whole unit). Mirrors
        // AllModelsRuleGateIntegrationTests' merge harness.
        private DataBinding<UnitData> MakeUnitWithJoinedHero(bool heroHasTeleport, bool hostHasTeleport,
            SpecialRuleDefinition? heroAura = null)
        {
            UnitData host = MakeBareUnit("Blink Squad", 3);
            if (hostHasTeleport)
            {
                host.AttachRuleDefinition(new ResolvedRule(CoreRuleCatalog.TeleportRuleName, CoreRuleCatalog.Teleport));
            }

            UnitData hero = MakeBareUnit("Blink Captain", 1);
            hero.AttachRuleDefinition(new ResolvedRule("Hero", CoreRuleCatalog.Hero));
            if (heroHasTeleport)
            {
                hero.AttachRuleDefinition(new ResolvedRule(CoreRuleCatalog.TeleportRuleName, CoreRuleCatalog.Teleport));
            }
            if (heroAura != null)
            {
                hero.AttachRuleDefinition(new ResolvedRule(heroAura.Name, heroAura));
            }

            HeroJoinResolver.Apply(new List<(UnitFileEntry, UnitData)>
            {
                (new UnitFileEntry { Id = "host" }, host),
                (new UnitFileEntry { JoinsUnitId = "host" }, hero),
            });

            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(host));
            _store.Create(new ArmyData(_player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        private UnitData MakeBareUnit(string name, int modelCount)
        {
            var modelBindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon>(), new Position(10f + i, 10f), _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            return new UnitData(_player, name, quality: 4, defense: 4, modelBindings: modelBindings);
        }
    }
}
