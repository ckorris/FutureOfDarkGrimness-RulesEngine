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
using FDG.Utilities;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #197 P10 (Ravage): proves Ravage(X) flows through the REAL melee
    // charge-contact stage. ResolveRavageWoundsStage fires the OnChargeContact "when", folds the emitted
    // InvokeDealAutoWounds dice, rolls X dice (each 6+ a DIRECT wound), and feeds them straight into wound
    // assignment - SKIPPING the save roll (owner ruling: Ravage wounds allow no armor save) while
    // Regeneration and Tough still apply. AllOnFaceDiceRoller(6) makes every die a 6, so each Ravage die is
    // a wound; AllOnFaceDiceRoller(1) makes every die a 1, so none are.
    [TestFixture]
    public class RavageRuleIntegrationTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
        }

        // The headline behaviour: the wounds take NO armor save. Every die is a 6, and a defense-4 model
        // saving on a rolled 6 would pass EVERY save if one were rolled - so any wound landing at all proves
        // the save stage was skipped. Ravage(3) -> 3 dice -> 3 sixes -> 3 unsaved wounds.
        [Test]
        public async Task Ravage_DealsWounds_SkippingTheArmorSave()
        {
            var requester = new CapturingWoundRequester();
            var ctx = new WoundTestContext(_store, requester, new AllOnFaceDiceRoller(6));
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            AttachRavage(attacker, x: 3);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5); // survives 3 wounds

            await RunStage(ctx, attacker, defender);

            Assert.That(requester.Captured!.TotalWoundsToAssign, Is.EqualTo(3f),
                "Ravage(3) rolls 3 sixes = 3 wounds; a rolled-6 armor save would block all if the save ran, " +
                "so 3 wounds landing proves the save stage is skipped.");
        }

        // Below the 6+ threshold nothing lands, and the stage skips cleanly (no phantom child pipeline).
        [Test]
        public async Task Ravage_RollsBelowSixPlus_NoWounds()
        {
            var ctx = new WoundTestContext(_store, new CapturingWoundRequester(), new AllOnFaceDiceRoller(1));
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            AttachRavage(attacker, x: 3);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5);

            await RunStage(ctx, attacker, defender);

            Assert.That(defender.RemainingWounds(), Is.EqualTo(5f),
                "3 dice all showing 1 are below the 6+ threshold, so Ravage deals no wounds.");
        }

        // Control: a charger with no Ravage rule leaves the stage a no-op.
        [Test]
        public async Task NoRavage_DefenderUnharmed()
        {
            var ctx = new WoundTestContext(_store, new CapturingWoundRequester(), new AllOnFaceDiceRoller(6));
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1); // no Ravage rule
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5);

            await RunStage(ctx, attacker, defender);

            Assert.That(defender.RemainingWounds(), Is.EqualTo(5f),
                "with no Ravage rule the stage skips - the defender takes no charge-contact wounds.");
        }

        // Regeneration STILL applies to the unsaveable wounds (owner ruling): the wounds reach the
        // wound-ignore sink in AssignWoundsStage. Every die is a 6, so all 3 wounds are ignored on the 5+
        // regen roll - the defender ends untouched, where without Regeneration (the test above) it took 3.
        [Test]
        public async Task Ravage_WoundsRemainSubjectToRegeneration()
        {
            var ctx = new WoundTestContext(_store, new CapturingWoundRequester(), new AllOnFaceDiceRoller(6));
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            AttachRavage(attacker, x: 3);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5);
            defender.GetValue().AttachRuleDefinition(
                new ResolvedRule("Regeneration", CoreRuleCatalog.Regeneration));

            await RunStage(ctx, attacker, defender);

            Assert.That(defender.RemainingWounds(), Is.EqualTo(5f),
                "Ravage's 3 wounds reach the regen sink and are ignored on the 5+ roll, so the defender " +
                "survives untouched - proving auto-wounds skip the save but not Regeneration.");
        }

        // Per-model scaling: X is per model carrying the rule. A unit-scoped Ravage(2) on a 3-model unit
        // rolls 2 x 3 = 6 dice, computed in Effect.DealAutoWounds (carrier count), not per-model evaluation
        // (which dedups once per unit). 6 sixes = 6 wounds.
        [Test]
        public async Task Ravage_ScalesDiceByLivingCarrierCount()
        {
            var requester = new CapturingWoundRequester();
            var ctx = new WoundTestContext(_store, requester, new AllOnFaceDiceRoller(6));
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 3); // 3 carriers of the unit-scoped rule
            AttachRavage(attacker, x: 2);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 10); // survives 6 wounds

            await RunStage(ctx, attacker, defender);

            Assert.That(requester.Captured!.TotalWoundsToAssign, Is.EqualTo(6f),
                "Ravage(2) x 3 living carriers = 6 dice = 6 wounds.");
        }

        // The #100 dice invariant: the wound count is never int-locked. Under the probabilistic roller,
        // Ravage(3) rolls 3 d6 at 6+ = 3 x 1/6 = 0.5 of a wound, and the pipeline carries the fraction all
        // the way to assignment - a rounded implementation would show 0 or 1.
        [Test]
        public async Task Ravage_ProbabilisticMode_KeepsFractionalWoundCount()
        {
            var requester = new CapturingWoundRequester();
            var ctx = new WoundTestContext(_store, requester, new ProbabilisticDiceRoller());
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            AttachRavage(attacker, x: 3);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5);

            await RunStage(ctx, attacker, defender);

            Assert.That(requester.Captured!.TotalWoundsToAssign, Is.EqualTo(0.5f).Within(0.001f),
                "3 dice at 6+ average half a wound; the fraction survives to assignment (no int-lock).");
        }

        // ── The strike-back arm (was DEFERRED; shipped 2026-07-30) ───────────────────────────────────────

        // Ravage reads "when attacking in melee", not "when charging" - so a CHARGED unit rolls it too, on
        // its strike-back. Driven through the real StrikeBackStage so the assertion covers the WIRING: the
        // stage is its starting child, and the roles are already reversed by the time it enters. The
        // striker-back's 3 sixes must land on the charger before either side swings.
        [Test]
        public async Task StrikeBack_TheChargedUnitRollsItsRavageToo()
        {
            var requester = new RavageMeleeRequester();
            var ctx = new WoundTestContext(_store, requester, new AllOnFaceDiceRoller(6));
            DataBinding<UnitData> charger = MakeUnit(modelCount: 5, withMeleeWeapon: true);  // survives 3 wounds
            DataBinding<UnitData> charged = MakeUnit(modelCount: 1, withMeleeWeapon: true);
            AttachRavage(charged, x: 3);

            await RunStrikeBack(ctx, charger, charged);

            Assert.That(requester.WoundRequests, Is.Not.Empty,
                "the striker-back's Ravage must produce a wound assignment - an empty list means the stage " +
                "never ran in the strike-back chain.");
            Assert.That(requester.WoundRequests[0].UnitReceivingWounds.GetValue(),
                Is.SameAs(charger.GetValue()),
                "Ravage resolves first in the strike-back, and its wounds go at the unit that charged.");
            Assert.That(requester.WoundRequests[0].TotalWoundsToAssign, Is.EqualTo(3f),
                "Ravage(3) x 1 living carrier = 3 dice, all sixes = 3 unsaveable wounds.");
        }

        // The seat guard, and the reason the charger cannot double-roll: inside StrikeBackStage the Actor is
        // the unit striking back, so the CHARGER's Ravage produces nothing here (it already rolled before its
        // own swings, in MeleeStage). Reading the un-reversed roles would fire it a second time and wound the
        // charged unit during its own strike-back - which is what this asserts cannot happen.
        [Test]
        public async Task StrikeBack_TheChargersRavageDoesNotFireAgain()
        {
            var requester = new RavageMeleeRequester();
            var ctx = new WoundTestContext(_store, requester, new AllOnFaceDiceRoller(6));
            // One carrier, so a wrongly-fired Ravage(3) would be 3 wounds against a 5-model unit - a real
            // assignment CHOICE, and therefore a real request. (More dice than the target has wounds wipes
            // the unit outright with nothing to assign, which would make this test vacuous.)
            DataBinding<UnitData> charger = MakeUnit(modelCount: 1, withMeleeWeapon: true);
            AttachRavage(charger, x: 3);                                   // the CHARGER carries it
            DataBinding<UnitData> charged = MakeUnit(modelCount: 5, withMeleeWeapon: true);

            await RunStrikeBack(ctx, charger, charged);

            Assert.That(requester.WoundRequests.Select(r => r.UnitReceivingWounds.GetValue()),
                Has.None.SameAs(charged.GetValue()),
                "nothing the charger owns may wound the charged unit during the charged unit's own " +
                "strike-back; a Ravage re-roll here would be the charger's second bite at one melee.");
        }

        // The strike-order swap is why the parent's copy sits AFTER DetermineStrikeOrderStage: a Counter unit
        // takes the first swing, so the Ravage that resolves before those swings is the COUNTER unit's, not
        // the charger's. Runs the real strike-order stage to perform the swap, then the real Ravage stage on
        // the same context - the order MeleeStage now wires them in.
        [Test]
        public async Task AfterACounterSwap_RavageBelongsToTheUnitAboutToSwing()
        {
            var requester = new RavageMeleeRequester();
            var ctx = new WoundTestContext(_store, requester, new AllOnFaceDiceRoller(6));
            DataBinding<UnitData> charger = MakeUnit(modelCount: 5, withMeleeWeapon: true); // survives 3 wounds
            DataBinding<UnitData> charged = MakeUnit(modelCount: 1, withMeleeWeapon: true); // one Ravage carrier
            charged.GetValue().AttachRuleDefinition(new ResolvedRule("Counter", CoreRuleCatalog.Counter));
            AttachRavage(charged, x: 3);

            var context = new CombatActionContext(ctx, charger, isMelee: true, isCharging: true);
            context.SetDefender(charged);

            var strikeOrder = new DetermineStrikeOrderStage(ctx, new NoOpLayer<ICombatActionContext>());
            strikeOrder.OnStrikeOrderDetermined.Bind("done");
            await strikeOrder.Enter(context);

            Assert.That(context.AttackingUnit.GetValue(), Is.SameAs(charged.GetValue()),
                "precondition: Counter swapped the roles, so the charged unit is about to swing.");

            var ravage = new ResolveRavageWoundsStage(ctx, new NoOpLayer<ICombatActionContext>());
            ravage.OnRavageResolved.Bind("done");
            await ravage.Enter(context);

            Assert.That(requester.WoundRequests, Is.Not.Empty,
                "the Counter unit swings first, so its Ravage is the one that resolves before those swings.");
            Assert.That(requester.WoundRequests[0].UnitReceivingWounds.GetValue(),
                Is.SameAs(charger.GetValue()),
                "and its wounds go at the charger - the stage must read the POST-swap Actor seat.");
        }

        // ── #434: OPR 3.6 Ravage - per model (#447's template), once per round, not while Fatigued ─────────────

        // "...when it's THIS MODEL's turn to strike": a joined hero's own Ravage (relocated onto its model by the #006
        // merge) fires - before #434 the stage evaluated the striker without its models and the hero's copy never did.
        [Test]
        public async Task AJoinedHerosOwnRavage_Fires_CountingTheHero()
        {
            var requester = new CapturingWoundRequester();
            var ctx = new WoundTestContext(_store, requester, new AllOnFaceDiceRoller(6));
            DataBinding<UnitData> squad = MakeUnitWithHero(hostModels: 5, hostRavage: null, heroRavage: 2);

            await RunStage(ctx, squad, MakeUnit(modelCount: 10));

            Assert.That(requester.Captured!.TotalWoundsToAssign, Is.EqualTo(2f), "1 hero x Ravage(2)");
        }

        // The host's Ravage counts the host's models; a joined hero lacking it does not inherit it.
        [Test]
        public async Task TheHostsRavage_CountsItsNativeModels_NotTheJoinedHero()
        {
            var requester = new CapturingWoundRequester();
            var ctx = new WoundTestContext(_store, requester, new AllOnFaceDiceRoller(6));
            DataBinding<UnitData> squad = MakeUnitWithHero(hostModels: 3, hostRavage: 1, heroRavage: 2);

            await RunStage(ctx, squad, MakeUnit(modelCount: 10));

            Assert.That(requester.Captured!.TotalWoundsToAssign, Is.EqualTo(5f), "3 models x Ravage(1) + 1 hero x Ravage(2)");
        }

        // A token-granted copy (an aura's literal die) counts every living model, the joined hero included.
        [Test]
        public async Task AGrantedCopy_CountsEveryLivingModel_TheJoinedHeroIncluded()
        {
            var requester = new CapturingWoundRequester();
            var resolver = new RuleResolver();
            resolver.Register(GrantedRavage);
            var ctx = new WoundTestContext(_store, requester, new AllOnFaceDiceRoller(6), ruleResolver: resolver);
            DataBinding<UnitData> squad = MakeUnitWithHero(hostModels: 3, hostRavage: null, heroRavage: null);
            squad.GetValue().Tokens.AddToken(new Token(TokenType.RuleGrant, 1, new TokenClearTrigger.ManualOnly(),
                new TokenPayload.RuleGrant(GrantedRavage.Name, ELifetime.Aura)));

            await RunStage(ctx, squad, MakeUnit(modelCount: 10));

            Assert.That(requester.Captured!.TotalWoundsToAssign, Is.EqualTo(4f), "3 host models + the hero, 1 die each");
        }

        [Test]
        public async Task OncePerRound_ASecondStrikeThisRound_RollsNoRavage()
        {
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            AttachRavage(attacker, x: 3);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 10);

            var first = new CapturingWoundRequester();
            await RunStage(new WoundTestContext(_store, first, new AllOnFaceDiceRoller(6)), attacker, defender);
            Assert.That(first.Captured!.TotalWoundsToAssign, Is.EqualTo(3f));

            var second = new CapturingWoundRequester();
            await RunStage(new WoundTestContext(_store, second, new AllOnFaceDiceRoller(6)), attacker, defender);
            Assert.That(second.Captured, Is.Null, "'Once per round' - the unit's next strike this round rolls none");

            // The round-end sweep clears the gate (RoundEnd default trigger).
            Token gate = attacker.GetValue().Tokens.GetAllTokens(TokenType.StrikeDiceUsed).Single();
            Assert.That(gate.ClearTrigger, Is.InstanceOf<TokenClearTrigger.RoundEnd>());
        }

        [Test]
        public async Task AFatiguedStriker_RollsNoRavage()
        {
            var requester = new CapturingWoundRequester();
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            AttachRavage(attacker, x: 3);
            FatigueUtilities.ApplyFatigued(attacker.GetValue());

            await RunStage(new WoundTestContext(_store, requester, new AllOnFaceDiceRoller(6)), attacker,
                MakeUnit(modelCount: 10));

            Assert.That(requester.Captured, Is.Null, "'unless this unit is Fatigued'");
            Assert.That(attacker.GetValue().Tokens.HasToken(TokenType.StrikeDiceUsed), Is.False,
                "nothing rolled, nothing spent");
        }

        // #436 (from #442's smokes): the operation names the unit being struck, not the bearer - in both
        // placements (the charger's strike, and the struck-back twin's reversed context). It read "Tribe Master's
        // Ravage(6) rolled 6 dice at Tribe Master, each 6+ a direct wound." before.
        [Test]
        public void TheRavageOperation_NamesTheStruckUnit_InBothPlacements()
        {
            DataBinding<UnitData> charger = MakeUnit(modelCount: 1);
            DataBinding<UnitData> charged = MakeUnit(modelCount: 3);
            AttachRavage(charger, 2);
            AttachRavage(charged, 1);
            var evaluator = new RuleEvaluator(new FixedDiceRoller(6));

            // The charger's strike, then the struck-back twin's reversed context (the striker is always Attacker).
            Assert.That(RavageTarget(evaluator, charger.GetValue(), charged.GetValue()), Is.SameAs(charged.GetValue()));
            Assert.That(RavageTarget(evaluator, charged.GetValue(), charger.GetValue()), Is.SameAs(charger.GetValue()));
        }

        private static IUnit RavageTarget(RuleEvaluator evaluator, IUnit striker, IUnit struck) =>
            evaluator.EvaluateAll(new Rules.Dispatch.Contexts.ChargeContactContext(striker, struck),
                    RuleParticipant.Actor(striker), RuleParticipant.Subject(struck))
                .OfType<RuleOperation.InvokeDealAutoWounds>().Single().Target;

        [Test]
        public void TheCatalogRavage_CarriesThe36TextAndTheFatigueGate()
        {
            Assert.That(CoreRuleCatalog.Ravage.Passive.Single().Condition,
                Is.EqualTo(new Condition.Not(new Condition.CountsAsFatiguedInMelee())));
            Assert.That(CoreRuleCatalog.Ravage.Description, Is.EqualTo(
                "Once per round, when it's this model's turn to strike in melee, roll X dice before attacking, " +
                "unless this unit is Fatigued. For each 6+ the target takes one wound."));
        }

        private static readonly SpecialRuleDefinition GrantedRavage = new("Ravage (Aura)",
            new[]
            {
                new HookEntry(EHookID.Melee_OnChargeContact, new Condition.Not(new Condition.CountsAsFatiguedInMelee()),
                    new Effect.DealAutoWounds(new ValueSource.Literal(1), SuccessThreshold: 6), ELifetime.ThisAttack),
            },
            System.Array.Empty<ActivatedAbility>());

        private DataBinding<UnitData> MakeUnitWithHero(int hostModels, int? hostRavage, int? heroRavage)
        {
            DataBinding<UnitData> hostBinding = MakeUnit(hostModels);
            UnitData host = hostBinding.GetValue();
            if (hostRavage is int hx) AttachRavage(hostBinding, hx);

            DataBinding<UnitData> heroBinding = MakeUnit(1);
            UnitData hero = heroBinding.GetValue();
            hero.AttachRuleDefinition(new ResolvedRule("Hero", CoreRuleCatalog.Hero));
            if (heroRavage is int rx) AttachRavage(heroBinding, rx);

            HeroJoinResolver.Apply(new List<(FDG.SaveLoad.UnitFileEntry, UnitData)>
            {
                (new FDG.SaveLoad.UnitFileEntry { Id = "host" }, host),
                (new FDG.SaveLoad.UnitFileEntry { JoinsUnitId = "host" }, hero),
            });
            Assert.That(host.JoinedHeroModelId, Is.Not.Null, "premise: the hero joined");
            return hostBinding;
        }

        private static async Task RunStrikeBack(WoundTestContext ctx, DataBinding<UnitData> charger,
            DataBinding<UnitData> charged)
        {
            var melee = new CombatActionContext(ctx, charger, isMelee: true, isCharging: true);
            melee.SetDefender(charged);
            melee.SetInRangeAttackers(charger.ModelBindings());
            melee.SetInRangeDefenders(charged.ModelBindings());

            var strikeBack = new StrikeBackStage(ctx, new NoOpLayer<ICombatActionContext>());
            strikeBack.FinishedStrikingBack.Bind("done");
            strikeBack.OnAttackerKilled.Bind("killed");
            await strikeBack.Enter(melee);
        }

        // Records every wound assignment IN ORDER (the strike-back chain runs Ravage, then the swings), and
        // answers the weapon menu so the chain gets past ChooseMeleeWeaponStage.
        private sealed class RavageMeleeRequester : IPlayerRequestByID
        {
            public List<AssignWoundsRequest> WoundRequests { get; } = new List<AssignWoundsRequest>();

            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply>
            {
                switch (request)
                {
                    case AssignWoundsRequest woundRequest:
                        WoundRequests.Add(woundRequest);
                        var result = new AssignWoundsResults(woundRequest.UnitReceivingWounds,
                            woundRequest.TotalWoundsToAssign);
                        result.AutoFill();
                        return Task.FromResult((TReply)(object)result);

                    case StringSelectionRequest menu:
                        return Task.FromResult((TReply)(object)menu.ValidOptions[0]);

                    case YesNoRequest:
                        return Task.FromResult((TReply)(object)false);

                    default:
                        throw new System.InvalidOperationException("Unexpected request type: " + request.GetType());
                }
            }
        }

        private static void AttachRavage(DataBinding<UnitData> unit, int x) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Ravage", CoreRuleCatalog.Ravage,
                new RuleArgument[] { new RuleArgument.Int(x) }));

        private async Task RunStage(WoundTestContext ctx, DataBinding<UnitData> attacker, DataBinding<UnitData> defender)
        {
            var stage = new ResolveRavageWoundsStage(ctx, new NoOpLayer<ICombatActionContext>());
            stage.OnRavageResolved.Bind("done");

            var context = new CombatActionContext(ctx, attacker, isMelee: true, isCharging: true);
            context.SetDefender(defender);

            await stage.Enter(context);
        }

        // Returns EVERY rolled die on a fixed face - so Roll(n) yields n dice there (unlike FixedDiceRoller,
        // whose TotalRolls is always 1). Needed for Ravage's multi-die pool to scale deterministically.
        private sealed class AllOnFaceDiceRoller : IDiceRoller
        {
            private readonly int _face;
            public AllOnFaceDiceRoller(int face) => _face = face;

            public IDiceResults Roll(int sideCount, float rollCount)
            {
                float[] perSide = new float[sideCount];
                perSide[_face - 1] = rollCount;
                return new DiceResults(perSide);
            }
        }

        // withMeleeWeapon: the strike-back tests drive a whole real swing chain, which needs a weapon to
        // choose; the charge-contact tests above never reach it and leave the models bare.
        private DataBinding<UnitData> MakeUnit(int modelCount, bool withMeleeWeapon = false)
        {
            var modelBindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(
                    baseRadiusInches: 0.75f,
                    weapons: withMeleeWeapon
                        ? new List<Weapon> { new Weapon("Blade", rangeInches: 0f, attacks: 1, armorPenetration: 0) }
                        : new List<Weapon>(),
                    initialPosition: new Position(0, 0),
                    gameDataStore: _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4,
                modelBindings: modelBindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
