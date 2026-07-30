using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Utilities;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #197 P16, the one-shot extra-attack primitive, and the two corpus
    // rules that need it:
    //   Takedown Strike - "Once per game, when it's this model's turn to attack in melee, it may make one
    //                      attack at Quality 2+ with AP(2), Deadly(3), and Takedown."
    //   Takedown Shot   - "Once per game, when this model shoots, it may make one extra attack against the
    //                      target at Quality 2+ with AP(2), Deadly(3), and Takedown."
    //
    // The two differ only in combat kind, so both are authored at Combat_OnAttackWindow and separated by
    // Condition.IsMelee. Nothing in either rider needed new vocabulary: "at Quality 2+" IS Reliable, and
    // Deadly(X) / Takedown are themselves - so what this pins is that the authored PROFILE reaches a real
    // attack and that every rider on it folds. Each is asserted against a control that removes just that
    // rider, because a fold that silently drops one is the #197 failure mode (a rule that validates, lints,
    // and does nothing).
    //
    // ProbabilisticDiceRoller makes the chain deterministic and fractional. The bearer is Quality 5 on
    // purpose: an already-good shooter cannot tell a Quality floor from no floor at all.
    [TestFixture]
    public class ExtraAttackRuleIntegrationTests
    {
        private static readonly TokenType StrikeUsed = new("AbilityUsed:Takedown Strike");

        private GameDataStore _store = null!;
        private PlayerID _attackerPlayer;
        private PlayerID _defenderPlayer;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _attackerPlayer = new PlayerID(System.Guid.NewGuid());
            _defenderPlayer = new PlayerID(System.Guid.NewGuid());
        }

        // ── Dispatch: the offer, the combat-kind split, and the once-per-game gate ───────────────────────

        [Test]
        public void Dispatch_TheMeleeVariant_IsOfferedInMeleeAndNotWhenShooting()
        {
            var ctx = MakeContext(new ExtraAttackRequester());
            DataBinding<UnitData> attacker = MakeUnit(_attackerPlayer, "Assassin", 1, TakedownStrike());
            DataBinding<UnitData> defender = MakeUnit(_defenderPlayer, "Squad", 3);

            Assert.That(Offers(ctx, attacker, defender, isMelee: true).Count, Is.EqualTo(1),
                "'when it's this model's turn to attack in melee' - offered for a swing");
            Assert.That(Offers(ctx, attacker, defender, isMelee: false), Is.Empty,
                "and not for a shot: Condition.IsMelee is the only thing separating the two rules");
        }

        [Test]
        public void Dispatch_TheShootingVariant_IsOfferedWhenShootingAndNotInMelee()
        {
            var ctx = MakeContext(new ExtraAttackRequester());
            DataBinding<UnitData> attacker = MakeUnit(_attackerPlayer, "Sniper", 1, TakedownShot());
            DataBinding<UnitData> defender = MakeUnit(_defenderPlayer, "Squad", 3);

            Assert.That(Offers(ctx, attacker, defender, isMelee: false).Count, Is.EqualTo(1),
                "'when this model shoots' - offered for a shot");
            Assert.That(Offers(ctx, attacker, defender, isMelee: true), Is.Empty,
                "and not for a swing");
        }

        [Test]
        public void Dispatch_ResolveEmitsTheAuthoredProfile_AndTheOncePerGameMarker()
        {
            var ctx = MakeContext(new ExtraAttackRequester());
            DataBinding<UnitData> attacker = MakeUnit(_attackerPlayer, "Assassin", 1, TakedownStrike());
            DataBinding<UnitData> defender = MakeUnit(_defenderPlayer, "Squad", 3);

            AbilityOffer offer = Offers(ctx, attacker, defender, isMelee: true)[0];
            IReadOnlyList<RuleOperation> ops = ctx.RuleEvaluator.ResolveAbility(offer,
                new[] { (IUnit)defender.GetValue() });

            RuleOperation.InvokeExtraAttack extra = ops.OfType<RuleOperation.InvokeExtraAttack>().Single();
            Assert.That(extra.Target, Is.SameAs(defender.GetValue()));
            Assert.That(extra.Attacks, Is.EqualTo(1), "'one attack'");
            Assert.That(extra.ArmorPenetration, Is.EqualTo(2));
            Assert.That(extra.WithRules, Is.EqualTo(new[] { "Reliable", "Deadly(3)", "Takedown" }),
                "the riders travel as weapon-rule names, arguments included");
            Assert.That(ops.OfType<RuleOperation.GrantTokenToUnit>()
                .Any(op => op.TokenToGrant.Type == StrikeUsed), Is.True,
                "the once-per-game cost marker is queued alongside the effect");
        }

        [Test]
        public void Dispatch_OncePerGame_NotOfferedAfterMarkerPresent()
        {
            var ctx = MakeContext(new ExtraAttackRequester());
            DataBinding<UnitData> attacker = MakeUnit(_attackerPlayer, "Assassin", 1, TakedownStrike());
            DataBinding<UnitData> defender = MakeUnit(_defenderPlayer, "Squad", 3);

            attacker.GetValue().Tokens.AddToken(new Token(StrikeUsed, 1, new TokenClearTrigger.ManualOnly()));

            Assert.That(Offers(ctx, attacker, defender, isMelee: true), Is.Empty,
                "'once per game' - with the used-marker present the gate is closed for the whole game");
        }

        // ── The stage: the profile becomes a real attack, and every rider folds ──────────────────────────

        [Test]
        public async Task Stage_TheProfileIsRolledAtQualityTwo_NotTheBearersOwnQuality()
        {
            var requester = new ExtraAttackRequester();
            // No Takedown: with it the wounds bypass the assign-wounds request, and the arithmetic is what
            // this test is about. Takedown gets its own test below.
            await RunStrike(requester, Rule("Takedown Strike", isMelee: true,
                armorPenetration: 2, withRules: new[] { "Reliable" }), defenderWoundsPerModel: 1);

            Assert.That(requester.WoundRequest, Is.Not.Null, "accepting resolves the extra attack into wounds");
            Assert.That(requester.WoundRequest!.TotalWoundsToAssign, Is.EqualTo(25f / 36f).Within(0.0001f),
                "1 attack x 5/6 to hit at the Quality-2 floor x 5/6 to fail a 6+ save");
        }

        [Test]
        public async Task Stage_WithoutTheQualityFloor_TheSameProfileHitsFarLess()
        {
            var requester = new ExtraAttackRequester();
            await RunStrike(requester, Rule("Takedown Strike", isMelee: true,
                armorPenetration: 2, withRules: System.Array.Empty<string>()), defenderWoundsPerModel: 1);

            Assert.That(requester.WoundRequest!.TotalWoundsToAssign, Is.EqualTo(5f / 18f).Within(0.0001f),
                "the control: a Quality-5 bearer hits on 2/6 without Reliable, so the floor is load-bearing " +
                "and not an artefact of the fixture");
        }

        [Test]
        public async Task Stage_TheProfilesArmorPenetrationApplies()
        {
            var requester = new ExtraAttackRequester();
            await RunStrike(requester, Rule("Takedown Strike", isMelee: true,
                armorPenetration: 0, withRules: new[] { "Reliable" }), defenderWoundsPerModel: 1);

            Assert.That(requester.WoundRequest!.TotalWoundsToAssign, Is.EqualTo(5f / 12f).Within(0.0001f),
                "the control for AP(2): at AP(0) the Defense-4 save stays 4+, so 3/6 of the hits wound " +
                "instead of 5/6");
        }

        // Deadly(X) is wasted on single-wound models by design (ConfineToClumps), so the multiplier is only
        // visible against a Tough defender - which is exactly the target an assassin is bought for.
        [Test]
        public async Task Stage_DeadlyMultipliesTheWounds_AgainstAToughDefender()
        {
            var requester = new ExtraAttackRequester();
            await RunStrike(requester, Rule("Takedown Strike", isMelee: true,
                armorPenetration: 2, withRules: new[] { "Reliable", "Deadly(3)" }), defenderWoundsPerModel: 3);

            Assert.That(requester.WoundRequest!.TotalWoundsToAssign, Is.EqualTo(25f / 12f).Within(0.0001f),
                "Deadly(3) turns each failed save into a 3-wound clump: 25/36 x 3");
        }

        [Test]
        public async Task Stage_WithoutDeadly_TheSameProfileDealsAThirdAsMuch()
        {
            var requester = new ExtraAttackRequester();
            await RunStrike(requester, Rule("Takedown Strike", isMelee: true,
                armorPenetration: 2, withRules: new[] { "Reliable" }), defenderWoundsPerModel: 3);

            Assert.That(requester.WoundRequest!.TotalWoundsToAssign, Is.EqualTo(25f / 36f).Within(0.0001f),
                "the control for Deadly(3)");
        }

        // The point of the rule, and the facet that needed the melee gate in BuildTargetListStage lifted:
        // the strike picks its victim out of the enemy unit instead of spreading.
        [Test]
        public async Task Stage_TakedownInMelee_ConfinesTheStrikeToThePickedModel()
        {
            DataBinding<UnitData> defender = MakeUnit(_defenderPlayer, "Squad", 5);
            var requester = new ExtraAttackRequester { Pick = defender.ModelBindings()[3] };

            await RunStrike(requester, TakedownStrike(), defenderWoundsPerModel: 1, defender: defender);

            Assert.That(requester.PickAsked, Is.True,
                "Takedown on the profile must reach BuildTargetListStage IN MELEE - the gate that used to " +
                "skip this hook for a swing is what made the rider inert");
            Assert.That(defender.ModelBindings()[3].GetValue().GetIsAlive(), Is.False,
                "the picked model takes the strike");
            Assert.That(defender.ModelBindings().Count(m => m.GetValue().GetIsAlive()), Is.EqualTo(4),
                "and only it - no carry-over to the rest of the unit ('a unit of [1]')");
        }

        [Test]
        public async Task Stage_Declining_DealsNothingAndSpendsNothing()
        {
            var requester = new ExtraAttackRequester { Accept = false };
            DataBinding<UnitData> attacker = await RunStrike(requester, TakedownStrike(),
                defenderWoundsPerModel: 1);

            Assert.That(requester.PickAsked, Is.False, "a declined strike makes no attack");
            Assert.That(requester.WoundRequest, Is.Null);
            Assert.That(attacker.GetValue().Tokens.HasToken(StrikeUsed), Is.False,
                "the cost is emitted only after the yes, so declining leaves the once-per-game unspent");
        }

        [Test]
        public async Task Stage_ABearerWithoutTheRule_IsNeverAsked()
        {
            var requester = new ExtraAttackRequester();
            DataBinding<UnitData> attacker = MakeUnit(_attackerPlayer, "Grunts", 1, rule: null);
            DataBinding<UnitData> defender = MakeUnit(_defenderPlayer, "Squad", 5);

            await RunStage(MakeContext(requester), attacker, defender, isMelee: true);

            Assert.That(requester.YesNoAsked, Is.False,
                "the window costs an ordinary melee nothing - no prompt, no dice");
        }

        // The shooting variant runs the same chain in the shoot direction. Asserted through the same wound
        // total, because a stage wired into the wrong chain would still pass a dispatch-only test.
        [Test]
        public async Task Stage_TheShootingVariant_ResolvesInTheShootDirection()
        {
            var requester = new ExtraAttackRequester();
            var ctx = MakeContext(requester);
            DataBinding<UnitData> attacker = MakeUnit(_attackerPlayer, "Sniper", 1,
                Rule("Takedown Shot", isMelee: false, armorPenetration: 2, withRules: new[] { "Reliable" }));
            DataBinding<UnitData> defender = MakeUnit(_defenderPlayer, "Squad", 5);

            await RunStage(ctx, attacker, defender, isMelee: false);

            Assert.That(requester.WoundRequest!.TotalWoundsToAssign, Is.EqualTo(25f / 36f).Within(0.0001f),
                "same profile, same arithmetic, offered because the window reports IsMelee false");
        }

        // ── Wiring: the window is open in all three chains ───────────────────────────────────────────────

        // The strike-back instance is the reason the hook is not Melee_OnChargeContact. Driven through the
        // real StrikeBackStage so the assertion covers the wiring, not just the stage in isolation - the
        // roles are reversed by the time it enters, and reading the wrong end of the melee would offer the
        // ability to the charger a second time instead of to the unit whose turn to attack it now is.
        [Test]
        public async Task Wiring_TheStrikerBackGetsItsWindowToo()
        {
            var requester = new ExtraAttackRequester();
            var ctx = MakeContext(requester);
            // The charger has no extra attack; the CHARGED unit does.
            DataBinding<UnitData> charger = MakeUnit(_attackerPlayer, "Chargers", 1, rule: null);
            DataBinding<UnitData> charged = MakeUnit(_defenderPlayer, "Assassin", 1,
                Rule("Takedown Strike", isMelee: true, armorPenetration: 2, withRules: new[] { "Reliable" }),
                weaponName: "Blade");

            var melee = new CombatActionContext(ctx, charger, isMelee: true);
            melee.SetDefender(charged);
            melee.SetInRangeAttackers(charger.ModelBindings());
            melee.SetInRangeDefenders(charged.ModelBindings());

            var strikeBack = new StrikeBackStage(ctx, new NoOpLayer<ICombatActionContext>());
            strikeBack.FinishedStrikingBack.Bind("done");
            strikeBack.OnAttackerKilled.Bind("killed");
            await strikeBack.Enter(melee);

            Assert.That(requester.YesNoAsked, Is.True,
                "'when it's this model's turn to attack in melee' is true of a unit that was charged");
            Assert.That(requester.ExtraAttackTargetName, Is.EqualTo("Chargers"),
                "and its extra attack goes at the charger, not back at itself - the reversed roles must be " +
                "read from the strike-back context");
        }

        // ── Regression guard for the lifted melee gate ───────────────────────────────────────────────────

        // Lifting BuildTargetListStage's shooting-only gate must not change an ordinary swing. A melee weapon
        // WITHOUT Takedown fires the newly-reached hook, produces no TargetIndividualModel, and spreads.
        [Test]
        public async Task LiftedGate_AnOrdinarySwing_StillSpreadsItsWounds()
        {
            var requester = new ExtraAttackRequester();
            var ctx = MakeContext(requester);
            DataBinding<UnitData> attacker = MakeUnit(_attackerPlayer, "Brawlers", 1, rule: null);
            DataBinding<UnitData> defender = MakeUnit(_defenderPlayer, "Squad", 3);

            var weapon = new Weapon("Fists", rangeInches: 0f, attacks: 1, armorPenetration: 0);
            var metadata = new CombatMetadata(ctx, attacker, defender, weapon, weaponCount: 1, isMelee: true);

            var stage = new BuildTargetListStage<ICombatMetadata>(ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");
            await stage.Enter(metadata);

            Assert.That(requester.PickAsked, Is.False, "no Takedown, no pick");
            Assert.That(metadata.QueryForResult(out IndividualTargetResult _), Is.False,
                "and no individual target stashed, so wound allocation is unchanged for every melee that " +
                "does not carry the rider");
        }

        // ── Fixture ──────────────────────────────────────────────────────────────────────────────────────

        /// <summary> The shipped Takedown Strike profile: A1, AP(2), Reliable + Deadly(3) + Takedown. </summary>
        private static SpecialRuleDefinition TakedownStrike() => Rule("Takedown Strike", isMelee: true,
            armorPenetration: 2, withRules: new[] { "Reliable", "Deadly(3)", "Takedown" });

        private static SpecialRuleDefinition TakedownShot() => Rule("Takedown Shot", isMelee: false,
            armorPenetration: 2, withRules: new[] { "Reliable", "Deadly(3)", "Takedown" });

        // Mirrors the supplement authoring (FdgRaylib/Assets/Books/GdfRuleSupplement.json); the app-side
        // ExtraAttackShippedDataTests pins that the shipped JSON really is this shape.
        private static SpecialRuleDefinition Rule(string name, bool isMelee, int armorPenetration,
            IReadOnlyList<string> withRules) => new SpecialRuleDefinition(name,
                System.Array.Empty<HookEntry>(),
                new[]
                {
                    new ActivatedAbility(EHookID.Combat_OnAttackWindow, new Cost.OncePerGame(),
                        new TargetSelector(0f, 1, 1, ETargetAffinity.Foe, false),
                        new Effect.ExtraAttack(name, Attacks: 1, ArmorPenetration: armorPenetration,
                            WithRules: withRules),
                        isMelee ? new Condition.IsMelee() : new Condition.Not(new Condition.IsMelee())),
                },
                ERuleScope.Unit);

        private IReadOnlyList<AbilityOffer> Offers(WoundTestContext ctx, DataBinding<UnitData> attacker,
            DataBinding<UnitData> defender, bool isMelee) => ctx.RuleEvaluator.GatherOffers(
                new AttackWindowContext(attacker.GetValue(), defender.GetValue(), isMelee));

        private WoundTestContext MakeContext(ExtraAttackRequester requester)
        {
            var resolver = new RuleResolver();
            resolver.Register(CoreRuleCatalog.Reliable);
            resolver.Register(CoreRuleCatalog.Deadly);
            resolver.Register(CoreRuleCatalog.Takedown);
            return new WoundTestContext(_store, requester, new ProbabilisticDiceRoller(),
                ruleResolver: resolver);
        }

        private async Task<DataBinding<UnitData>> RunStrike(ExtraAttackRequester requester,
            SpecialRuleDefinition rule, int defenderWoundsPerModel, DataBinding<UnitData>? defender = null)
        {
            WoundTestContext ctx = MakeContext(requester);
            DataBinding<UnitData> attacker = MakeUnit(_attackerPlayer, "Assassin", 1, rule, weaponName: "Blade");
            defender ??= MakeUnit(_defenderPlayer, "Squad", 5);
            if (defenderWoundsPerModel > 1)
            {
                foreach (IModel model in defender.GetValue().Models) model.SetMaxWounds(defenderWoundsPerModel);
            }

            await RunStage(ctx, attacker, defender, isMelee: true);
            return attacker;
        }

        private static async Task RunStage(WoundTestContext ctx, DataBinding<UnitData> attacker,
            DataBinding<UnitData> defender, bool isMelee)
        {
            var combat = new CombatActionContext(ctx, attacker, isMelee: isMelee);
            combat.SetDefender(defender);

            ResolveExtraAttackStage stage = isMelee
                ? new ResolveMeleeExtraAttackStage(ctx, new NoOpLayer<ICombatActionContext>())
                : new ResolveRangedExtraAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            stage.OnExtraAttackResolved.Bind("done");
            await stage.Enter(combat);
        }

        private DataBinding<UnitData> MakeUnit(PlayerID player, string name, int modelCount,
            SpecialRuleDefinition? rule = null, string weaponName = "Blade")
        {
            var modelBindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                var weapons = new List<Weapon>
                {
                    new Weapon(weaponName, rangeInches: 0f, attacks: 1, armorPenetration: 0),
                };
                var model = new ModelData(0.5f, weapons, new Position(i, 0f), _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            // Quality 5: bad enough that the Quality-2 floor is unmistakable when it applies.
            var unit = new UnitData(player, name, quality: 5, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            if (rule != null)
            {
                binding.GetValue().AttachRuleDefinition(new ResolvedRule(rule.Name, rule));
            }

            _store.Create(new ArmyData(player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }
    }

    // Answers the extra attack's yes/no and its Takedown model pick, and captures + auto-resolves the
    // AssignWoundsRequest so the child chain completes (the StrafeRequester shape).
    internal sealed class ExtraAttackRequester : IPlayerRequestByID
    {
        public bool Accept { get; init; } = true;

        /// <summary> Which model a Takedown pick takes; the first living one when unset. </summary>
        public DataBinding<ModelData>? Pick { get; init; }

        public AssignWoundsRequest? WoundRequest { get; private set; }
        public bool YesNoAsked { get; private set; }
        public bool PickAsked { get; private set; }

        /// <summary> The unit named in the yes/no - i.e. the unit the extra attack was offered against. </summary>
        public string? ExtraAttackTargetName { get; private set; }

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            switch (request)
            {
                case YesNoRequest yesNo:
                    YesNoAsked = true;
                    ExtraAttackTargetName = TargetNameFrom(yesNo.QuestionText);
                    return Task.FromResult((TReply)(object)Accept);

                case SelectionRequest<ModelData> selection:
                    PickAsked = true;
                    DataBinding<ModelData> pick = Pick ?? selection.ValidOptions[0].Option;
                    return Task.FromResult((TReply)(object)pick);

                // The melee-weapon menu, reached only by the tests that drive a whole real chain past the
                // extra-attack window (the strike-back wiring test). Take the first weapon and carry on.
                case StringSelectionRequest menu:
                    return Task.FromResult((TReply)(object)menu.ValidOptions[0]);

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

        // "... one extra attack in melee against Chargers?" -> "Chargers".
        private static string? TargetNameFrom(string question)
        {
            int against = question.LastIndexOf("against ", System.StringComparison.Ordinal);
            if (against < 0) return null;
            return question.Substring(against + "against ".Length).TrimEnd('?');
        }
    }
}
