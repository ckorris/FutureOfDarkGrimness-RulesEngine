using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;
using FDG.Tests.RulesHarness;
using NUnit.Framework;

namespace FDG.Tests
{
    // Regenerative Strength. #197 P12 (3.5): "Place one marker on this model when it ignores a wound. When in melee,
    // pick one of its weapons to get +X attacks, where X is the number of markers on it."
    // #434 (OPR 3.6): "Place one token on this model when it ignores a wound. Once per round, when it's this model's
    // turn to strike in melee, roll as many dice as tokens on it before attacking, unless this unit is Fatigued. For
    // each 4+ the target takes one wound."
    //
    // The producer is unchanged: the marker's value is ROLL-DERIVED (wounds reaching the ignore fold are fractional
    // under the probabilistic roller), so the count rides a TokenPayload.Magnitude (owner-signed 2026-07-31). The 3.5
    // consumer - stage code that offered +X attacks on one weapon - is gone; the 3.6 consumer is rule data: a
    // DealAutoWounds at Melee_OnChargeContact whose pool is the token total (ValueSource.TokenTotal, PerModel false),
    // rolled at 4+ by ResolveRavageWoundsStage beside Ravage, with Ravage's "once per round" gate and the Fatigue
    // condition.
    [TestFixture]
    public class RegenerativeStrengthRuleIntegrationTests
    {
        private const float Tolerance = 0.0001f;

        // Mirrors the shipped GdfRuleSupplement.json entry exactly; RegenerativeStrengthShippedDataTests
        // (app side) pins that the authored data still has this shape.
        internal static SpecialRuleDefinition RegenerativeStrengthDefinition() => new("Regenerative Strength",
            new[]
            {
                new HookEntry(EHookID.Lifecycle_OnWoundIgnored, new Condition.Always(),
                    new Effect.GrantIgnoredWoundMarker(TokenType.RegenerativeStrengthMarker,
                        new TokenClearTrigger.ManualOnly()),
                    ELifetime.UntilEndOfGame, ERuleSeat.Subject),
                new HookEntry(EHookID.Melee_OnChargeContact,
                    new Condition.Not(new Condition.CountsAsFatiguedInMelee()),
                    new Effect.DealAutoWounds(new ValueSource.TokenTotal(TokenType.RegenerativeStrengthMarker),
                        SuccessThreshold: 4, PerModel: false),
                    ELifetime.ThisAttack),
            },
            Array.Empty<ActivatedAbility>());

        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
        }

        // ---------- Producer: the marker is placed, and it is worth what was ignored ----------

        [Test]
        public async Task IgnoringWounds_PlacesAMarkerWorthTheWoundsIgnored()
        {
            // 3 failed saves, Regeneration 5+: the probabilistic roller spreads 3 dice as 0.5 per face, so
            // faces 5 and 6 ignore exactly 1.0 wound. A whole number here on purpose - the fractional case
            // is the next test, and this one would pass even for a rounding implementation.
            (DataBinding<UnitData> attacker, DataBinding<UnitData> defender) = MakeCombatants();
            AttachRegeneration(defender);
            AttachRegenerativeStrength(defender);

            await RunAssignWounds(attacker, defender, failedSaves: 3);

            Assert.That(Markers(defender), Is.EqualTo(1f).Within(Tolerance),
                "one marker per ignored wound: 3 dice at 5+ ignore 1.0");
        }

        [Test]
        public async Task TheMarkerIsFractional_WhenTheIgnoreRollIs()
        {
            // THE test for this slice. One failed save at Regeneration 5+ ignores 1/3 of a wound, so the
            // marker is worth 0.333. Any implementation that stores the count in Token.Count - or rounds
            // anywhere on the way - reports 0 or 1 here and fails.
            (DataBinding<UnitData> attacker, DataBinding<UnitData> defender) = MakeCombatants();
            AttachRegeneration(defender);
            AttachRegenerativeStrength(defender);

            await RunAssignWounds(attacker, defender, failedSaves: 1);

            Assert.That(Markers(defender), Is.EqualTo(1f / 3f).Within(Tolerance),
                "a third of a wound ignored is a third of a marker - never rounded");
        }

        [Test]
        public async Task RepeatedIgnores_SumIntoOneMarkerEntry()
        {
            // Two attacks ignoring DIFFERENT amounts - 1/3 then 1.0 - so the magnitudes must genuinely
            // merge. The differing values are load-bearing: with two EQUAL magnitudes the general
            // payload-equality branch merges them by coincidence (same payload -> Count 2, and
            // GetTokenMagnitude multiplies by Count), so an identical-fraction test passes even with the
            // magnitude branch deleted and pins nothing. Caught by mutation testing, not by review.
            (DataBinding<UnitData> attacker, DataBinding<UnitData> defender) = MakeCombatants();
            AttachRegeneration(defender);
            AttachRegenerativeStrength(defender);

            await RunAssignWounds(attacker, defender, failedSaves: 1);
            await RunAssignWounds(attacker, defender, failedSaves: 3);

            Assert.That(Markers(defender), Is.EqualTo(1f / 3f + 1f).Within(Tolerance),
                "0.333 + 1.0 = 1.333 markers");
            Assert.That(TokenEntryCount(defender), Is.EqualTo(1),
                "one running total, not one entry per ignore event");
        }

        [Test]
        public async Task AUnitWithoutTheRule_GainsNoMarker()
        {
            // Regeneration alone ignores wounds without counting them.
            (DataBinding<UnitData> attacker, DataBinding<UnitData> defender) = MakeCombatants();
            AttachRegeneration(defender);

            await RunAssignWounds(attacker, defender, failedSaves: 3);

            Assert.That(Markers(defender), Is.EqualTo(0f).Within(Tolerance));
        }

        [Test]
        public async Task WithNothingToIgnore_NoMarkerIsPlaced()
        {
            // The carrier has the rule but no wound-ignore source, so the hook never fires. Guards the
            // fire site's `ignored > 0f` condition, which is what lets IHasIgnoredWoundCount promise a
            // positive count to every rule authored at the hook.
            (DataBinding<UnitData> attacker, DataBinding<UnitData> defender) = MakeCombatants();
            AttachRegenerativeStrength(defender);

            await RunAssignWounds(attacker, defender, failedSaves: 3);

            Assert.That(Markers(defender), Is.EqualTo(0f).Within(Tolerance),
                "no wound-ignore rule means no ignored wounds means no markers");
        }

        // ---------- Consumer (3.6): before it strikes, a 4+ wound pool of as many dice as tokens ----------

        [Test]
        public async Task BeforeStriking_RollsAsManyDiceAsTokens_EachFourPlusAWound()
        {
            // 3 tokens, every die a 4: 3 unsaveable wounds before the swings.
            var requester = new CapturingWoundRequester();
            (DataBinding<UnitData> striker, DataBinding<UnitData> target) = MakeCombatants();
            AttachRegenerativeStrength(striker);
            GiveMarkers(striker, 3f);

            await RunStrikeDice(new WoundTestContext(_store, requester, new AllOnFace(4)), striker, target);

            Assert.That(requester.Captured, Is.Not.Null, "the pool produced a wound assignment");
            Assert.That(requester.Captured!.TotalWoundsToAssign, Is.EqualTo(3f));
            Assert.That(Markers(striker), Is.EqualTo(3f), "the tokens are rolled, not spent");
        }

        [Test]
        public async Task ThreesMiss()
        {
            var requester = new CapturingWoundRequester();
            (DataBinding<UnitData> striker, DataBinding<UnitData> target) = MakeCombatants();
            AttachRegenerativeStrength(striker);
            GiveMarkers(striker, 3f);

            await RunStrikeDice(new WoundTestContext(_store, requester, new AllOnFace(3)), striker, target);

            Assert.That(requester.Captured, Is.Null, "3s are below the 4+");
        }

        [Test]
        public async Task ProbabilisticMode_TheFractionalTokenTotalRoundsToWholeDice()
        {
            // 2.6 tokens -> 3 dice at 4+ -> 1.5 expected wounds.
            var requester = new CapturingWoundRequester();
            (DataBinding<UnitData> striker, DataBinding<UnitData> target) = MakeCombatants();
            AttachRegenerativeStrength(striker);
            GiveMarkers(striker, 2.6f);

            await RunStrikeDice(NewContext(requester), striker, target);

            Assert.That(requester.Captured!.TotalWoundsToAssign, Is.EqualTo(1.5f).Within(Tolerance));
        }

        [Test]
        public async Task WithoutTokens_NothingIsRolled_AndTheRoundsUseIsNotSpent()
        {
            var requester = new CapturingWoundRequester();
            (DataBinding<UnitData> striker, DataBinding<UnitData> target) = MakeCombatants();
            AttachRegenerativeStrength(striker);

            await RunStrikeDice(new WoundTestContext(_store, requester, new AllOnFace(6)), striker, target);

            Assert.That(requester.Captured, Is.Null);
            Assert.That(striker.GetValue().Tokens.HasToken(TokenType.StrikeDiceUsed), Is.False,
                "no dice, no use: a later strike this round with tokens may still roll");
        }

        [Test]
        public async Task OncePerRound_ASecondStrikeThisRound_RollsNothing()
        {
            (DataBinding<UnitData> striker, DataBinding<UnitData> target) = MakeCombatants();
            AttachRegenerativeStrength(striker);
            GiveMarkers(striker, 2f);

            var first = new CapturingWoundRequester();
            await RunStrikeDice(new WoundTestContext(_store, first, new AllOnFace(6)), striker, target);
            Assert.That(first.Captured!.TotalWoundsToAssign, Is.EqualTo(2f));

            var second = new CapturingWoundRequester();
            await RunStrikeDice(new WoundTestContext(_store, second, new AllOnFace(6)), striker, target);
            Assert.That(second.Captured, Is.Null, "'once per round'");
        }

        [Test]
        public async Task WhileFatigued_NothingIsRolled()
        {
            var requester = new CapturingWoundRequester();
            (DataBinding<UnitData> striker, DataBinding<UnitData> target) = MakeCombatants();
            AttachRegenerativeStrength(striker);
            GiveMarkers(striker, 3f);
            FatigueUtilities.ApplyFatigued(striker.GetValue());

            await RunStrikeDice(new WoundTestContext(_store, requester, new AllOnFace(6)), striker, target);

            Assert.That(requester.Captured, Is.Null, "'unless this unit is Fatigued'");
        }

        [Test]
        public async Task TheVolley_GetsNoExtraAttacks_Any_More()
        {
            // The 3.5 consumer (+X attacks on one melee weapon) is gone: the attack count is the weapon's.
            (DataBinding<UnitData> striker, DataBinding<UnitData> target) = MakeCombatants();
            AttachRegenerativeStrength(striker);
            GiveMarkers(striker, 3f);
            var context = NewContext(new CapturingWoundRequester());

            var stage = new DetermineHitRollStage<ICombatMetadata>(context, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");
            var metadata = new CombatMetadata(context, striker, target,
                new Weapon("Claws", rangeInches: 0f, attacks: 3, armorPenetration: 0), weaponCount: 1,
                attackerMoved: false, isMelee: true);
            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out DetermineHitRollResults result), Is.True);
            Assert.That(result.AttackCount, Is.EqualTo(3f));
        }

        [Test]
        public void TheStrikePool_RoundTripsThroughJson_AndOldDealAutoWoundsDataStaysPerModel()
        {
            Effect pool = new Effect.DealAutoWounds(new ValueSource.TokenTotal(TokenType.RegenerativeStrengthMarker),
                SuccessThreshold: 4, PerModel: false);
            string json = System.Text.Json.JsonSerializer.Serialize(pool, FDG.Rules.Serialization.RuleJson.Options);
            Assert.That(json, Does.Contain("tokenTotal"));
            Assert.That(System.Text.Json.JsonSerializer.Deserialize<Effect>(json, FDG.Rules.Serialization.RuleJson.Options),
                Is.EqualTo(pool));

            // Ravage data written before #434 has no perModel field: it still multiplies by the carriers.
            string legacy = "{\"kind\":\"dealAutoWounds\",\"diceCountPerModel\":{\"kind\":\"arg\",\"index\":0}," +
                "\"successThreshold\":6}";
            Assert.That(System.Text.Json.JsonSerializer.Deserialize<Effect>(legacy, FDG.Rules.Serialization.RuleJson.Options),
                Is.EqualTo(new Effect.DealAutoWounds(new ValueSource.Arg(0), 6, PerModel: true)));
        }

        // ---------- Token layer ----------

        [Test]
        public void MagnitudeTokens_MergeWhileOtherPayloadsStaySeparate()
        {
            // The magnitude branch is the ONE place payload equality is deliberately not the stacking key.
            // Asserted alongside a RuleGrant pair so the exception stays an exception: distinct grants must
            // still stay distinct, which is what the general branch is for.
            var container = new TokenContainer();
            container.AddToken(new Token(TokenType.RegenerativeStrengthMarker, 1,
                new TokenClearTrigger.ManualOnly(), new TokenPayload.Magnitude(0.25f)));
            container.AddToken(new Token(TokenType.RegenerativeStrengthMarker, 1,
                new TokenClearTrigger.ManualOnly(), new TokenPayload.Magnitude(0.5f)));

            container.AddToken(new Token(TokenType.RuleGrant, 1, new TokenClearTrigger.ManualOnly(),
                new TokenPayload.RuleGrant("Furious", ELifetime.ThisAttack)));
            container.AddToken(new Token(TokenType.RuleGrant, 1, new TokenClearTrigger.ManualOnly(),
                new TokenPayload.RuleGrant("Relentless", ELifetime.ThisAttack)));

            Assert.That(container.GetTokenMagnitude(TokenType.RegenerativeStrengthMarker),
                Is.EqualTo(0.75f).Within(Tolerance));
            Assert.That(container.GetTokenCount(TokenType.RuleGrant), Is.EqualTo(2),
                "two different granted rules remain two tokens");
        }

        [Test]
        public void GetTokenMagnitude_IgnoresTokensWithoutAMagnitudePayload()
        {
            // A caller asking the wrong type gets 0, not a silently-wrong integer count.
            var container = new TokenContainer();
            container.AddToken(new Token(TokenType.Shaken, 1, new TokenClearTrigger.ManualOnly()));

            Assert.That(container.GetTokenMagnitude(TokenType.Shaken), Is.EqualTo(0f).Within(Tolerance));
            Assert.That(container.GetTokenCount(TokenType.Shaken), Is.EqualTo(1));
        }

        [Test]
        public void TheEffectThrows_AtAHookThatCannotAnswer()
        {
            // Same contract as GrantTokenToKiller: authoring the effect at a context without the capability
            // is a data bug, and BookRuleSupplement.ValidateAll catches it before play. Pinned so the
            // throw is not quietly softened into a silent no-op later.
            var effect = new Effect.GrantIgnoredWoundMarker(TokenType.RegenerativeStrengthMarker,
                new TokenClearTrigger.ManualOnly());
            (DataBinding<UnitData> attacker, DataBinding<UnitData> defender) = MakeCombatants();

            Assert.Throws<InvalidOperationException>(() => effect.Apply(
                new RuleInvocation(new Rules.Dispatch.Contexts.PreApplyWoundContext(
                        attacker.GetValue(), defender.GetValue()),
                    defender.GetValue(), Array.Empty<RuleArgument>()),
                new List<RuleOperation>()));
        }

        // ---------- helpers ----------

        private WoundTestContext NewContext(IPlayerRequestByID requester) =>
            new(_store, requester, new ProbabilisticDiceRoller());

        private static float Markers(DataBinding<UnitData> unit) =>
            unit.GetValue().Tokens.GetTokenMagnitude(TokenType.RegenerativeStrengthMarker);

        private static int TokenEntryCount(DataBinding<UnitData> unit)
        {
            int count = 0;
            foreach (Token _ in unit.GetValue().Tokens.GetAllTokens(TokenType.RegenerativeStrengthMarker))
            {
                count++;
            }
            return count;
        }

        private static void GiveMarkers(DataBinding<UnitData> unit, float magnitude) =>
            unit.GetValue().Tokens.AddToken(new Token(TokenType.RegenerativeStrengthMarker, 1,
                new TokenClearTrigger.ManualOnly(), new TokenPayload.Magnitude(magnitude)));

        private static void AttachRegeneration(DataBinding<UnitData> unit) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Regeneration",
                CoreRuleCatalog.Regeneration, Array.Empty<RuleArgument>()));

        private static void AttachRegenerativeStrength(DataBinding<UnitData> unit) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Regenerative Strength",
                RegenerativeStrengthDefinition(), Array.Empty<RuleArgument>()));

        private async Task RunAssignWounds(DataBinding<UnitData> attacker, DataBinding<UnitData> defender,
            int failedSaves)
        {
            // The defender is deliberately roomy (10 models): the wound total must stay sub-lethal so the
            // stage reaches its allocation branch normally rather than short-circuiting on a wipe.
            var context = NewContext(new CapturingWoundRequester());
            var stage = new AssignWoundsStage<ICombatMetadata>(context, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");

            var weapon = new Weapon("Test", rangeInches: 48f, attacks: 1, armorPenetration: 0);
            var metadata = new CombatMetadata(context, attacker, defender, weapon, weaponCount: 1,
                attackerMoved: false, isMelee: false);

            var failedList = new List<FailedSaveInfo>();
            for (int i = 0; i < failedSaves; i++)
            {
                failedList.Add(new FailedSaveInfo(TestDice.Faces(1), new PendingSaveRolls(TestDice.Faces(1), 4)));
            }
            metadata.AddResult(new RollToSaveResults(new List<SuccessfulSaveInfo>(), failedList));

            await stage.Enter(metadata);
        }

        private static async Task RunStrikeDice(WoundTestContext context, DataBinding<UnitData> striker,
            DataBinding<UnitData> target)
        {
            var combat = new CombatActionContext(context, striker, isMelee: true, isCharging: true);
            combat.SetDefender(target);
            var stage = new ResolveRavageWoundsStage(context, new NoOpLayer<ICombatActionContext>());
            stage.OnRavageResolved.Bind("done");
            await stage.Enter(combat);
        }

        private (DataBinding<UnitData>, DataBinding<UnitData>) MakeCombatants() =>
            (MakeUnit("Attacker", modelCount: 1), MakeUnit("Engine of Suffering", modelCount: 10));

        private DataBinding<UnitData> MakeUnit(string name, int modelCount)
        {
            var modelBindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(baseRadiusInches: 0.75f,
                    weapons: new List<Weapon> { new("Claws", rangeInches: 0f, attacks: 3, armorPenetration: 0) },
                    initialPosition: new Position(i, 0), gameDataStore: _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(new PlayerID(Guid.NewGuid()), name, quality: 4, defense: 4,
                modelBindings: modelBindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }

        // Every rolled die on one face, so an N-die pool is N dice there (FixedDiceRoller rolls one).
        private sealed class AllOnFace : IDiceRoller
        {
            private readonly int _face;
            public AllOnFace(int face) => _face = face;

            public IDiceResults Roll(int sideCount, float rollCount)
            {
                float[] perSide = new float[sideCount];
                perSide[_face - 1] = rollCount;
                return new DiceResults(perSide);
            }
        }
    }
}
