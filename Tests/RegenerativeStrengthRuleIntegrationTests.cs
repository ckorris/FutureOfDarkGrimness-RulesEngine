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
    // #197 P12 Regenerative Strength - "Place one marker on this model when it ignores a wound. When in
    // melee, pick one of its weapons to get +X attacks, where X is the number of markers on it."
    //
    // The slice's whole difficulty is that the marker's value is ROLL-DERIVED. Wounds reaching the ignore
    // fold are already fractional under the probabilistic roller, and the ignore roll spreads them across
    // faces again, so "how many wounds did it ignore" has no integer answer. Rather than round it (which
    // int-locks a roll-derived value - the one thing the dice invariant forbids), the count rides a
    // TokenPayload.Magnitude and is added to an attack count that was always a float. Owner-signed
    // 2026-07-31, reversing the 2026-07-22 deferral once it turned out the "separate decisive roll"
    // option did not exist: you cannot roll a fractional number of decisive dice without rounding first.
    //
    // Both hooks involved were declared-but-unwired (the Breath Attack shape). This slice lights
    // Lifecycle_OnWoundIgnored - context, capability, effect and fire site. Shooting_OnPreHitRollCount is
    // deliberately LEFT dormant: the read side prompts and is gated to one weapon per melee, neither of
    // which an effect can express, so it lives in stage code beside TargetMarkerSpend. See
    // RegenerativeStrengthAttacks.
    [TestFixture]
    public class RegenerativeStrengthRuleIntegrationTests
    {
        private const float Tolerance = 0.0001f;

        // Mirrors the shipped GdfRuleSupplement.json entry exactly; RegenerativeStrengthShippedDataTests
        // (app side) pins that the authored data still has this shape.
        private static SpecialRuleDefinition RegenerativeStrengthDefinition() => new("Regenerative Strength",
            new[]
            {
                new HookEntry(EHookID.Lifecycle_OnWoundIgnored, new Condition.Always(),
                    new Effect.GrantIgnoredWoundMarker(TokenType.RegenerativeStrengthMarker,
                        new TokenClearTrigger.ManualOnly()),
                    ELifetime.UntilEndOfGame, ERuleSeat.Subject),
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

        // ---------- Consumer: the markers buy attacks, in melee, on one weapon ----------

        [Test]
        public async Task InMelee_AcceptedMarkersAddAttacksToTheVolley()
        {
            // The read side, through the REAL DetermineHitRollStage: a 3-attack weapon plus 1.5 markers
            // rolls 4.5 attacks. Fractional all the way through - attackCount has always been a float,
            // which is what made the consumption side free once the producer could express a fraction.
            (DataBinding<UnitData> attacker, DataBinding<UnitData> defender) = MakeCombatants();
            GiveMarkers(attacker, 1.5f);

            DetermineHitRollResults results = await RunDetermineHitRoll(attacker, defender,
                new CannedYesNoRequester(accept: true), isMelee: true, weaponAttacks: 3);

            Assert.That(results.AttackCount, Is.EqualTo(4.5f).Within(Tolerance),
                "3 base attacks + 1.5 markers");
        }

        [Test]
        public async Task Declining_AddsNothing_AndKeepsTheMarkers()
        {
            // Markers are never consumed by the offer - the rule says "+X attacks where X is the number of
            // markers on it", not "spend them". A player saving the bonus for a later weapon in the same
            // swing loop must still have it.
            (DataBinding<UnitData> attacker, DataBinding<UnitData> defender) = MakeCombatants();
            GiveMarkers(attacker, 2f);

            DetermineHitRollResults results = await RunDetermineHitRoll(attacker, defender,
                new CannedYesNoRequester(accept: false), isMelee: true, weaponAttacks: 3);

            Assert.That(results.AttackCount, Is.EqualTo(3f).Within(Tolerance), "declined - base attacks only");
            Assert.That(Markers(attacker), Is.EqualTo(2f).Within(Tolerance), "and the markers are still there");
        }

        [Test]
        public async Task Shooting_IsNeverOffered()
        {
            // "When in melee" - a counting requester proves the prompt is not merely answered "no" but
            // never issued, so a shooting unit with markers is not nagged every volley.
            (DataBinding<UnitData> attacker, DataBinding<UnitData> defender) = MakeCombatants();
            GiveMarkers(attacker, 2f);
            var requester = new CountingYesNoRequester(accept: true);

            DetermineHitRollResults results = await RunDetermineHitRoll(attacker, defender, requester,
                isMelee: false, weaponAttacks: 3);

            Assert.That(results.AttackCount, Is.EqualTo(3f).Within(Tolerance), "no melee, no bonus");
            Assert.That(requester.Asked, Is.Zero, "and no prompt was issued at all");
        }

        [Test]
        public async Task WithoutMarkers_NoPromptIsIssued()
        {
            (DataBinding<UnitData> attacker, DataBinding<UnitData> defender) = MakeCombatants();
            var requester = new CountingYesNoRequester(accept: true);

            DetermineHitRollResults results = await RunDetermineHitRoll(attacker, defender, requester,
                isMelee: true, weaponAttacks: 3);

            Assert.That(results.AttackCount, Is.EqualTo(3f).Within(Tolerance));
            Assert.That(requester.Asked, Is.Zero, "nothing to offer, so no question");
        }

        [Test]
        public async Task OnlyOneWeaponPerMelee_GetsTheBonus()
        {
            // "Pick ONE of its weapons." The melee swing loop re-enters DetermineHitRollStage per weapon,
            // so the second volley of the same melee must be neither boosted nor even offered.
            (DataBinding<UnitData> attacker, DataBinding<UnitData> defender) = MakeCombatants();
            GiveMarkers(attacker, 2f);
            var requester = new CountingYesNoRequester(accept: true);
            var context = NewContext(requester);

            DetermineHitRollResults first = await RunDetermineHitRoll(context, attacker, defender,
                isMelee: true, weaponAttacks: 3);
            DetermineHitRollResults second = await RunDetermineHitRoll(context, attacker, defender,
                isMelee: true, weaponAttacks: 3);

            Assert.That(first.AttackCount, Is.EqualTo(5f).Within(Tolerance), "first weapon takes the bonus");
            Assert.That(second.AttackCount, Is.EqualTo(3f).Within(Tolerance), "the second gets nothing");
            Assert.That(requester.Asked, Is.EqualTo(1), "and is not asked again this melee");
        }

        [Test]
        public async Task AfterTheMelee_TheBonusIsAvailableAgain()
        {
            // The gate is per MELEE, not per game or per activation - run through the real PostMeleeStage,
            // whose Melee_OnPostMelee sweep is the only thing that clears a CustomHook token of that hook.
            // Delete the sweep and this test fails while every other test here still passes.
            (DataBinding<UnitData> attacker, DataBinding<UnitData> defender) = MakeCombatants();
            GiveMarkers(attacker, 2f);
            var requester = new CountingYesNoRequester(accept: true);
            var context = NewContext(requester);

            await RunDetermineHitRoll(context, attacker, defender, isMelee: true, weaponAttacks: 3);
            Assert.That(attacker.GetValue().Tokens.HasToken(TokenType.RegenerativeStrengthSpent), Is.True,
                "the gate is stamped by the accepted offer");

            await RunPostMelee(context, attacker, defender);

            Assert.That(attacker.GetValue().Tokens.HasToken(TokenType.RegenerativeStrengthSpent), Is.False,
                "the post-melee sweep clears it");

            DetermineHitRollResults nextMelee = await RunDetermineHitRoll(context, attacker, defender,
                isMelee: true, weaponAttacks: 3);
            Assert.That(nextMelee.AttackCount, Is.EqualTo(5f).Within(Tolerance),
                "the next melee may cash the markers again");
        }

        [Test]
        public async Task TheStrikerBacksGate_IsClearedToo_NotJustTheAttackedUnit()
        {
            // Why the sweep visits BOTH combatants: a strike-back happens inside the ENEMY's activation, so
            // a gate stamped there would never be reached by the end-of-activation sweep (which only ever
            // visits the activated unit) and would suppress the bearer's own next melee.
            (DataBinding<UnitData> attacker, DataBinding<UnitData> defender) = MakeCombatants();
            GiveMarkers(defender, 2f);
            var context = NewContext(new CannedYesNoRequester(accept: true));

            // The defender swings back: roles reversed at the volley level.
            await RunDetermineHitRoll(context, defender, attacker, isMelee: true, weaponAttacks: 3);
            Assert.That(defender.GetValue().Tokens.HasToken(TokenType.RegenerativeStrengthSpent), Is.True);

            await RunPostMelee(context, attacker, defender);

            Assert.That(defender.GetValue().Tokens.HasToken(TokenType.RegenerativeStrengthSpent), Is.False,
                "the striker-back's gate clears with the melee it belongs to");
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

        private Task<DetermineHitRollResults> RunDetermineHitRoll(DataBinding<UnitData> attacker,
            DataBinding<UnitData> defender, IPlayerRequestByID requester, bool isMelee, int weaponAttacks)
            => RunDetermineHitRoll(NewContext(requester), attacker, defender, isMelee, weaponAttacks);

        private static async Task<DetermineHitRollResults> RunDetermineHitRoll(WoundTestContext context,
            DataBinding<UnitData> attacker, DataBinding<UnitData> defender, bool isMelee, int weaponAttacks)
        {
            var stage = new DetermineHitRollStage<ICombatMetadata>(context, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");

            var weapon = new Weapon("Claws", rangeInches: 0f, attacks: weaponAttacks, armorPenetration: 0);
            var metadata = new CombatMetadata(context, attacker, defender, weapon, weaponCount: 1,
                attackerMoved: false, isMelee: isMelee);
            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out DetermineHitRollResults result), Is.True,
                "the stage must store a DetermineHitRollResults");
            return result;
        }

        private static async Task RunPostMelee(WoundTestContext context, DataBinding<UnitData> attacker,
            DataBinding<UnitData> defender)
        {
            var combatContext = new CombatActionContext(context, attacker, isMelee: true, isCharging: true);
            combatContext.SetDefender(defender);

            var stage = new PostMeleeStage(context, new NoOpLayer<ICombatActionContext>());
            stage.ToFinished.Bind("done");
            await stage.Enter(combatContext);
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

        // Answers yes/no like CannedYesNoRequester but counts the questions, so a test can assert that a
        // prompt was never ISSUED - the difference between "offered and declined" and "correctly skipped".
        private sealed class CountingYesNoRequester : IPlayerRequestByID
        {
            private readonly bool _accept;

            public CountingYesNoRequester(bool accept) => _accept = accept;

            public int Asked { get; private set; }

            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply>
            {
                if (request is YesNoRequest)
                {
                    Asked++;
                    return Task.FromResult((TReply)(object)_accept);
                }

                throw new InvalidOperationException("Unexpected request type: " + request.GetType());
            }
        }
    }
}
