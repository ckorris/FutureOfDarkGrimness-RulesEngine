using FDG.Data;
using FDG.Players;
using FDG.Presentation;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for Strafing: "Once per activation, when this model moves through
    // enemy units, pick one of them and attack it with this weapon as if it was shooting. This weapon may
    // only be used in this way."
    //
    // #197 reworked the rule from a unit-scoped approximation (a flat 3 synthetic hits, no weapon
    // restriction) to what the corpus actually says. All 12 references sit on bomb WEAPONS, and were dead as
    // a scope mismatch until then. Three things are pinned here, in rough order of how silently they fail:
    //  - "with this weapon": the attack runs the real shooting chain with the carrying weapon, so its
    //    Attacks, AP and rules (Blast) all apply. The old flat-3-hits path looked identical in the log.
    //  - "may only be used in this way": the weapon is out of both attack pools. Every corpus strafe weapon
    //    has range 0, and IsMelee() IS "range 0", so without this a Bomber Plane swings its bombs in melee.
    //  - "pick one of them": several enemies crossed means a pick, not the first one silently.
    //
    // ProbabilisticDiceRoller makes the whole chain deterministic and fractional: at Quality 4 each attack
    // is 0.5 hits, and at Defense 4 with no AP each hit is 0.5 wounds.
    [TestFixture]
    public class StrafingRuleIntegrationTests
    {
        private static readonly TokenType UsedMarker = new("AbilityUsed:Strafing");

        private GameDataStore _store = null!;
        private PlayerID _mover;
        private PlayerID _foe;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _mover = new PlayerID(System.Guid.NewGuid());
            _foe = new PlayerID(System.Guid.NewGuid());
        }

        [Test]
        public void Geometry_DetectsCrossedEnemy_IgnoresEnemyOffPath()
        {
            var ctx = new WoundTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> mover = MakeUnit(_mover, "Bikers", Bombs(), new Position(0f, 0f));
            DataBinding<UnitData> onPath = MakeUnit(_foe, "Grunts", null, new Position(5f, 0f));
            DataBinding<UnitData> offPath = MakeUnit(_foe, "Bystanders", null, new Position(5f, 30f));

            // Move straight along z=0 from the origin through (5,0) where 'onPath' stands.
            var paths = new List<ModelMoveEntry>
            {
                new ModelMoveEntry(mover.GetValue().ModelBindings[0], new List<Position> { new Position(10f, 0f) })
            };

            List<DataBinding<UnitData>> crossed = MovementUtilities.GetEnemyUnitsMovedThrough(paths, mover, ctx);

            Assert.That(crossed, Does.Contain(onPath), "the enemy on the move line is crossed");
            Assert.That(crossed, Does.Not.Contain(offPath), "an enemy well off the line is not crossed");
        }

        [Test]
        public void Geometry_TallMoverFootprint_CrossesEnemyOffTheCentreLine()
        {
            var ctx = new WoundTestContext(_store, new NullPlayerRequester());
            // A tall 0.5"×6" strafer moving along z=0: its 3" half-height sweeps over an enemy 2" off the line —
            // which its inscribed bounding circle (r=0.25) would never reach (#150).
            DataBinding<UnitData> mover = MakeUnitWithShape(_mover, "Lancers",
                new RectangleBase(0.5f, 6f), new Position(0f, 0f));
            DataBinding<UnitData> offCentre = MakeUnit(_foe, "Grunts", null, new Position(5f, 2f));

            var paths = new List<ModelMoveEntry>
            {
                new ModelMoveEntry(mover.GetValue().ModelBindings[0], new List<Position> { new Position(10f, 0f) })
            };

            List<DataBinding<UnitData>> crossed = MovementUtilities.GetEnemyUnitsMovedThrough(paths, mover, ctx);

            Assert.That(crossed, Does.Contain(offCentre), "the tall footprint sweeps over an enemy off the centre line.");
        }

        // The offer must come off the WEAPON. Before #197 GatherOffers read only unit, per-model and granted
        // rules, so a weapon-scoped Strafing resolved, validated, linted - and was never offered.
        [Test]
        public void Dispatch_TheOfferComesFromTheWeapon_AndCarriesIt()
        {
            var ctx = new WoundTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> mover = MakeUnit(_mover, "Bikers", Bombs(), new Position(0f, 0f));
            DataBinding<UnitData> enemy = MakeUnit(_foe, "Grunts", null, new Position(5f, 0f));

            IReadOnlyList<AbilityOffer> offers = ctx.RuleEvaluator.GatherOffers(
                new MoveThroughEnemyContext(mover.GetValue()));

            Assert.That(offers.Count, Is.EqualTo(1), "Strafing is offered at the move-through hook");
            Assert.That(offers[0].Weapon?.Name, Is.EqualTo("Bombs"),
                "the offer records the carrying weapon - 'attack it with THIS weapon' has no other source");

            IReadOnlyList<RuleOperation> ops = ctx.RuleEvaluator.ResolveAbility(offers[0],
                new[] { (IUnit)enemy.GetValue() });

            RuleOperation.InvokeWeaponAttack attack = ops.OfType<RuleOperation.InvokeWeaponAttack>().Single();
            Assert.That(attack.Target, Is.SameAs(enemy.GetValue()));
            Assert.That(attack.Weapon?.Name, Is.EqualTo("Bombs"), "and threads it through to the operation");
            Assert.That(ops.OfType<RuleOperation.GrantTokenToUnit>().Any(op => op.TokenToGrant.Type == UsedMarker),
                Is.True, "the once-per-activation cost marker is queued");
        }

        // Five models carrying the same bomb are five Weapon instances. Deduped by name, or the ability is
        // offered once per carrier and the player is asked five times for one strafe.
        [Test]
        public void Dispatch_ManyCarriersOfOneWeapon_OfferOnce()
        {
            var ctx = new WoundTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> mover = MakeUnit(_mover, "Bikers", Bombs(),
                new Position(0f, 0f), new Position(0f, 1f), new Position(0f, 2f),
                new Position(0f, 3f), new Position(0f, 4f));

            IReadOnlyList<AbilityOffer> offers = ctx.RuleEvaluator.GatherOffers(
                new MoveThroughEnemyContext(mover.GetValue()));

            Assert.That(offers.Count, Is.EqualTo(1), "one weapon by name, one offer");
        }

        [Test]
        public void Dispatch_OncePerActivation_NotOfferedAfterMarkerPresent()
        {
            var ctx = new WoundTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> mover = MakeUnit(_mover, "Bikers", Bombs(), new Position(0f, 0f));

            mover.GetValue().Tokens.AddToken(new Token(UsedMarker, 1, new TokenClearTrigger.ActivationEnd()));

            IReadOnlyList<AbilityOffer> offers = ctx.RuleEvaluator.GatherOffers(
                new MoveThroughEnemyContext(mover.GetValue()));

            Assert.That(offers, Is.Empty, "with the used-marker present the once-per-activation gate is closed");
        }

        // The heart of the rework. A2 at Quality 4 is 1.0 hits; at Defense 4 with no AP that is 0.5 wounds.
        // The old rule dealt a flat 3 hits regardless of the weapon, which for this profile is 6x too many.
        [Test]
        public async Task Stage_AttacksWithTheWeaponsOwnAttacksAndQuality()
        {
            var requester = new StrafeRequester(accept: true);
            var ctx = new WoundTestContext(_store, requester, new ProbabilisticDiceRoller());

            DataBinding<UnitData> mover = MakeUnit(_mover, "Bikers", Bombs(attacks: 2), new Position(0f, 0f));
            MakeUnit(_foe, "Grunts", null, FiveInAColumn(5f));

            await RunStrafe(ctx, mover, new Position(10f, 0f));

            Assert.That(requester.WoundRequest, Is.Not.Null, "accepting resolves the strafe into wounds");
            Assert.That(requester.WoundRequest!.TotalWoundsToAssign, Is.EqualTo(0.5f).Within(0.0001f),
                "2 attacks x 0.5 to hit x 0.5 to fail the save");
        }

        [Test]
        public async Task Stage_TheAttackCountIsTheWeapons_NotAConstant()
        {
            var requester = new StrafeRequester(accept: true);
            var ctx = new WoundTestContext(_store, requester, new ProbabilisticDiceRoller());

            DataBinding<UnitData> mover = MakeUnit(_mover, "Bikers", Bombs(attacks: 1), new Position(0f, 0f));
            MakeUnit(_foe, "Grunts", null, FiveInAColumn(5f));

            await RunStrafe(ctx, mover, new Position(10f, 0f));

            Assert.That(requester.WoundRequest!.TotalWoundsToAssign, Is.EqualTo(0.25f).Within(0.0001f),
                "halving the weapon's Attacks halves the strafe - a fixed hit count could not tell them apart");
        }

        // "As if it was shooting" means the weapon's own rules fold through the shared shooting hooks. Blast
        // is the corpus's own answer: 9 of the 12 references sit on a Blast bomb.
        [Test]
        public async Task Stage_TheWeaponsRulesApply()
        {
            var requester = new StrafeRequester(accept: true);
            var ctx = new WoundTestContext(_store, requester, new ProbabilisticDiceRoller());

            Weapon bombs = Bombs(attacks: 2);
            bombs.AttachRuleDefinition(new ResolvedRule("Blast", CoreRuleCatalog.Blast,
                new RuleArgument[] { new RuleArgument.Int(3) }));
            DataBinding<UnitData> mover = MakeUnit(_mover, "Bikers", bombs, new Position(0f, 0f));
            MakeUnit(_foe, "Grunts", null, FiveInAColumn(5f));

            await RunStrafe(ctx, mover, new Position(10f, 0f));

            Assert.That(requester.WoundRequest!.TotalWoundsToAssign, Is.EqualTo(1.5f).Within(0.0001f),
                "Blast(3) triples the hits (1.0 -> 3.0), so the wounds triple too");
        }

        [Test]
        public async Task Stage_TheWeaponsArmorPenetrationApplies()
        {
            var requester = new StrafeRequester(accept: true);
            var ctx = new WoundTestContext(_store, requester, new ProbabilisticDiceRoller());

            DataBinding<UnitData> mover = MakeUnit(_mover, "Bikers",
                Bombs(attacks: 2, armorPenetration: 1), new Position(0f, 0f));
            MakeUnit(_foe, "Grunts", null, FiveInAColumn(5f));

            await RunStrafe(ctx, mover, new Position(10f, 0f));

            Assert.That(requester.WoundRequest!.TotalWoundsToAssign, Is.EqualTo(2f / 3f).Within(0.0001f),
                "AP(1) pushes the save from 4+ to 5+, so 4/6 of the 1.0 hits wound instead of 3/6");
        }

        [Test]
        public async Task Stage_Decline_NoWoundsAndNothingSpent()
        {
            var requester = new StrafeRequester(accept: false);
            var ctx = new WoundTestContext(_store, requester, new ProbabilisticDiceRoller());

            DataBinding<UnitData> mover = MakeUnit(_mover, "Bikers", Bombs(), new Position(0f, 0f));
            MakeUnit(_foe, "Grunts", null, new Position(5f, 0f));

            await RunStrafe(ctx, mover, new Position(10f, 0f));

            Assert.That(requester.WoundRequest, Is.Null, "declining resolves no attack");
            Assert.That(mover.GetValue().Tokens.HasToken(UsedMarker), Is.False,
                "the cost is emitted only after the pick, so declining spends nothing");
        }

        // "Pick one of them." Two enemies crossed, and the stage must ask which - not silently take the
        // first, which is what it did before this slice.
        [Test]
        public async Task Stage_SeveralEnemiesCrossed_ThePlayerPicksWhichOne()
        {
            var requester = new StrafeRequester(accept: true) { PickIndex = 1 };
            var ctx = new WoundTestContext(_store, requester, new ProbabilisticDiceRoller());

            DataBinding<UnitData> mover = MakeUnit(_mover, "Bikers", Bombs(attacks: 2), new Position(0f, 0f));
            MakeUnit(_foe, "Nearer", null, new Position(3f, 0f));
            MakeUnit(_foe, "Further", null, FiveInAColumn(7f));

            await RunStrafe(ctx, mover, new Position(12f, 0f));

            Assert.That(requester.SelectionAsked, Is.True, "two crossed enemies means a pick");
            Assert.That(requester.YesNoAsked, Is.False,
                "and no yes/no on top of it - a cancellable pick is not asked twice");
            Assert.That(requester.WoundRequest!.UnitReceivingWounds.GetValue().Name, Is.EqualTo("Further"),
                "the strafe hits the unit the player picked, not the first one crossed");
        }

        [Test]
        public async Task Stage_SeveralEnemiesCrossed_CancellingThePickSpendsNothing()
        {
            var requester = new StrafeRequester(accept: true) { PickIndex = null };
            var ctx = new WoundTestContext(_store, requester, new ProbabilisticDiceRoller());

            DataBinding<UnitData> mover = MakeUnit(_mover, "Bikers", Bombs(), new Position(0f, 0f));
            MakeUnit(_foe, "Nearer", null, new Position(3f, 0f));
            MakeUnit(_foe, "Further", null, new Position(7f, 0f));

            await RunStrafe(ctx, mover, new Position(12f, 0f));

            Assert.That(requester.WoundRequest, Is.Null);
            Assert.That(mover.GetValue().Tokens.HasToken(UsedMarker), Is.False,
                "backing out of the pick is declining the ability");
        }

        // Owner-signed-off 2026-07-28: "as if it was shooting" carries the shooting morale test with it,
        // unlike Impact and Crossing Attack, whose text says nothing of the kind. Observed through the beat
        // the test itself presents, not through its outcome - the decisive morale die is Rng-driven, and an
        // assertion on pass/fail would really be an assertion about the fixture's seed.
        [Test]
        public async Task Stage_AVictimLeftAtHalfStrength_TakesTheShootingMoraleTest()
        {
            var sink = new RecordingPresentationSink();
            var requester = new StrafeRequester(accept: true);
            var ctx = new WoundTestContext(_store, requester, new ProbabilisticDiceRoller(),
                new LocalPresenter(sink, new InstantPresentationClock()));

            // 5 attacks x 0.5 to hit x 5/6 to fail a 6+ save = 2.08 wounds: 2 of 3 models die, which is half
            // strength or less. A wipe would be wrong to use here - a destroyed unit takes no test.
            DataBinding<UnitData> mover = MakeUnit(_mover, "Bikers",
                Bombs(attacks: 5, armorPenetration: 3), new Position(0f, 0f));
            DataBinding<UnitData> enemy = MakeUnit(_foe, "Grunts", null,
                new Position(5f, 0f), new Position(5f, 1f), new Position(5f, 2f));

            await RunStrafe(ctx, mover, new Position(10f, 0f));

            Assert.That(enemy.GetValue().GetIsAlive(), Is.True, "the fixture must not wipe the unit");
            Assert.That(enemy.GetValue().GetIsAtHalfStrength(), Is.True);
            Assert.That(sink.Beats.Any(b => b.Text != null && b.Text.Contains("Morale Test")), Is.True,
                "bombed to half strength or less, the victim takes the shooting morale test");
        }

        [Test]
        public async Task Stage_AnUnbloodiedVictim_TakesNoMoraleTest()
        {
            var sink = new RecordingPresentationSink();
            var requester = new StrafeRequester(accept: false);
            var ctx = new WoundTestContext(_store, requester, new ProbabilisticDiceRoller(),
                new LocalPresenter(sink, new InstantPresentationClock()));

            DataBinding<UnitData> mover = MakeUnit(_mover, "Bikers", Bombs(), new Position(0f, 0f));
            MakeUnit(_foe, "Grunts", null,
                new Position(5f, 0f), new Position(5f, 1f), new Position(5f, 2f));

            await RunStrafe(ctx, mover, new Position(10f, 0f));

            Assert.That(sink.Beats.Any(b => b.Text != null && b.Text.Contains("Morale Test")), Is.False,
                "a declined strafe deals no wounds, so there is nothing to test for");
        }

        // "This weapon may only be used in this way." The failure here is completely silent: a strafe weapon
        // has range 0, IsMelee() IS "range 0", so it lands in the melee pool and gets swung in close combat.
        [Test]
        public void AStrafeWeapon_IsInNeitherAttackPool()
        {
            DataBinding<UnitData> mover = MakeUnit(_mover, "Bikers", Bombs(), new Position(0f, 0f));
            mover.GetValue().ModelBindings[0].GetValue().Weapons.Add(
                new Weapon("Claws", rangeInches: 0f, attacks: 2, armorPenetration: 0));

            IUnit unit = mover.GetValue();

            Assert.That(unit.GetMeleeWeapons().Select(w => w.Name), Is.EquivalentTo(new[] { "Claws" }),
                "the bomb is range 0 and would otherwise be swung as a melee weapon");
            Assert.That(unit.GetRangedWeapons(), Is.Empty);
            Assert.That(StrafingRules.IsStrafeOnly(Bombs()), Is.True);
            Assert.That(StrafingRules.IsStrafeOnly(new Weapon("Claws", 0f, 2, 0)), Is.False,
                "an ordinary close-combat weapon is untouched");
        }

        // The rule no longer carries a fly-over passive, because the source rule never granted one - its
        // carriers all have Aircraft or Flying. A bearer without either could never trigger the ability, so
        // the stage says so rather than leaving a weapon that quietly does nothing.
        [Test]
        public async Task Stage_ABearerThatCannotFlyOver_IsReported()
        {
            var warnings = new List<string>();
            void Capture(string message) => warnings.Add(message);
            RuleDiagnostics.OnWarning += Capture;
            try
            {
                var ctx = new WoundTestContext(_store, new StrafeRequester(accept: false),
                    new ProbabilisticDiceRoller());
                // The one unit in this fixture built WITHOUT Flying. WarnOnce keys on the unit name, so the
                // name has to be unique across the suite for the warning to reach this assertion.
                DataBinding<UnitData> mover = MakeUnit(_mover, "Grounded Bombardiers", Bombs(),
                    canFlyOver: false, new Position(0f, 0f));
                MakeUnit(_foe, "Grunts", null, new Position(5f, 0f));

                await RunStrafe(ctx, mover, new Position(10f, 0f));

                Assert.That(warnings, Has.Some.Contains("cannot move through enemy units"),
                    "a Strafing weapon on a unit that cannot fly over is a weapon that can never be used");
            }
            finally
            {
                RuleDiagnostics.OnWarning -= Capture;
            }
        }

        private static Position[] FiveInAColumn(float x) => new[]
        {
            new Position(x, 0f), new Position(x, 1f), new Position(x, 2f),
            new Position(x, 3f), new Position(x, 4f),
        };

        /// <summary> A bomb weapon in the corpus's shape: range 0, carrying Strafing. </summary>
        private static Weapon Bombs(int attacks = 1, int armorPenetration = 0)
        {
            var weapon = new Weapon("Bombs", rangeInches: 0f, attacks: attacks,
                armorPenetration: armorPenetration);
            weapon.AttachRuleDefinition(new ResolvedRule("Strafing", CoreRuleCatalog.Strafing));
            return weapon;
        }

        private static async Task RunStrafe(WoundTestContext ctx, DataBinding<UnitData> mover, Position destination)
        {
            var moveContext = new MovementActionContext(ctx, mover);
            moveContext.SubmitValidPathTemplate(new List<ModelMoveEntry>
            {
                new ModelMoveEntry(mover.GetValue().ModelBindings[0], new List<Position> { destination })
            });

            var stage = new StrafingStage(ctx, new NoOpLayer<IMovementActionContext>());
            stage.OnStrafeResolved.Bind("done");
            await stage.Enter(moveContext);
        }

        private DataBinding<UnitData> MakeUnit(PlayerID player, string name, Weapon? weapon,
            params Position[] positions) => MakeUnit(player, name, weapon, canFlyOver: true, positions);

        // A unit given a strafe weapon also gets Flying, because every corpus carrier has Flying or
        // Aircraft - Strafing itself grants no fly-over, so a strafer built without one is the exception,
        // not the default (see Stage_ABearerThatCannotFlyOver_IsReported).
        private DataBinding<UnitData> MakeUnit(PlayerID player, string name, Weapon? weapon,
            bool canFlyOver, params Position[] positions)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            foreach (Position pos in positions)
            {
                var weapons = weapon == null ? new List<Weapon>() : new List<Weapon> { weapon };
                var model = new ModelData(0.5f, weapons, pos, _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(player, name, quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            if (weapon != null && canFlyOver)
            {
                binding.GetValue().AttachRuleDefinition(new ResolvedRule("Flying", CoreRuleCatalog.Flying));
            }

            _store.Create(new ArmyData(player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        // Single-model unit with an explicit base shape (#150 shape-aware move-through geometry).
        private DataBinding<UnitData> MakeUnitWithShape(PlayerID player, string name, IBaseShape shape, Position pos)
        {
            var model = new ModelData(shape, new List<Weapon> { Bombs() }, pos, _store);
            var modelBindings = new List<DataBinding<ModelData>> { _store.GetDataBinding<ModelData>(_store.Create(model)) };
            var unit = new UnitData(player, name, quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            binding.GetValue().AttachRuleDefinition(new ResolvedRule("Flying", CoreRuleCatalog.Flying));
            _store.Create(new ArmyData(player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }
    }

    // Answers the strafe's yes/no (one enemy crossed) or its pick (several), and captures + auto-resolves the
    // AssignWoundsRequest so the stage completes (mirrors CapturingWoundRequester).
    internal sealed class StrafeRequester : IPlayerRequestByID
    {
        private readonly bool _accept;

        /// <summary> Which crossed enemy to pick, or null to back out of the pick. </summary>
        public int? PickIndex { get; init; } = 0;

        public AssignWoundsRequest? WoundRequest { get; private set; }
        public bool YesNoAsked { get; private set; }
        public bool SelectionAsked { get; private set; }

        public StrafeRequester(bool accept) => _accept = accept;

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            switch (request)
            {
                case YesNoRequest:
                    YesNoAsked = true;
                    return Task.FromResult((TReply)(object)_accept);

                case CancellableSelectionRequest<UnitData> selection:
                    SelectionAsked = true;
                    CancellableResult<DataBinding<UnitData>> reply =
                        _accept && PickIndex is int index
                            ? new Selected<DataBinding<UnitData>>(selection.ValidOptions[index].Option)
                            : new Cancelled<DataBinding<UnitData>>();
                    return Task.FromResult((TReply)(object)reply);

                case AssignWoundsRequest woundRequest:
                    WoundRequest = woundRequest;
                    var result = new AssignWoundsResults(woundRequest.UnitReceivingWounds,
                        woundRequest.TotalWoundsToAssign);
                    result.AutoFill();
                    return Task.FromResult((TReply)(object)result);

                default:
                    throw new System.InvalidOperationException("Unexpected request type: " + request.GetType());
            }
        }
    }
}
