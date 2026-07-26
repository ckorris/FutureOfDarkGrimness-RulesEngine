using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using NUnit.Framework;

namespace FDG.Tests
{
    // #166a — negative tests pinning RuleFireLint's detection power. The catalog/supplement lint
    // suites only ever see healthy rules once bugs are fixed, so without these a regression in the
    // lint itself (e.g. a context variant lost in a refactor) would silently green everything. Each
    // case is a minimal rule exhibiting one defect class the lint exists to catch.
    [TestFixture]
    public class RuleFireLintSelfTests
    {
        private static readonly List<ActivatedAbility> NoAbilities = new();
        private static readonly List<HookEntry> NoEntries = new();

        private static SpecialRuleDefinition Passive(string name, HookEntry entry) =>
            new(name, new List<HookEntry> { entry }, NoAbilities);

        [Test]
        public void CleanRule_PassesLint()
        {
            // A Furious clone: natural-6 extra hit in melee, on a live hook with a live context.
            var rule = Passive("Lint Furious", new HookEntry(EHookID.Shooting_OnHitRollComplete,
                new Condition.And(new Condition.UnmodifiedRollEquals(6), new Condition.IsMelee()),
                new Effect.AddExtraHit(OnRollValue: 6),
                ELifetime.ThisAttack));

            Assert.That(RuleFireLint.Check(rule), Is.Empty);
        }

        [Test]
        public void MarkerRule_WithNoEntries_IsFlagged()
        {
            var rule = new SpecialRuleDefinition("Lint Marker", NoEntries, NoAbilities);

            Assert.That(RuleFireLint.Check(rule), Has.Some.Contains("no passive entries"));
        }

        [Test]
        public void PassiveEntry_OnHookWithNoContext_IsFlagged()
        {
            // Morale_OnShakenCleared has no context type — no stage can ever fire an entry there.
            // (Round_OnRoundEnd, the previous example, gained RoundEndContext in #100 #13.)
            var rule = Passive("Lint Dead Hook", new HookEntry(EHookID.Morale_OnShakenCleared,
                new Condition.Always(),
                new Effect.RollModifier(ERollKind.Hit, Delta: 1),
                ELifetime.ThisRound));

            Assert.That(RuleFireLint.Check(rule), Has.Some.Contains("no context type"));
        }

        [Test]
        public void PassiveEntry_WhoseConditionNeedsAMissingCapability_IsFlagged()
        {
            // IsMelee reads IHasCombatKind, which RoundStartContext does not provide — the condition
            // can never pass at that hook. The capability check reports the violation, and the fire
            // check reports the throw CapabilityCondition raises when evaluated against that context.
            var rule = Passive("Lint Wrong Hook", new HookEntry(EHookID.Round_OnRoundStart,
                new Condition.IsMelee(),
                new Effect.RollModifier(ERollKind.Hit, Delta: 1),
                ELifetime.ThisRound));

            IReadOnlyList<string> problems = RuleFireLint.Check(rule);
            Assert.That(problems, Has.Some.Contains("IHasCombatKind"));
            Assert.That(problems, Has.Some.Contains("condition threw"));
        }

        [Test]
        public void PassiveEntry_ProducingAnOperationNoStageReads_IsFlagged()
        {
            // The exact shape of the shipped Changebound / Machine-Fog bug (#197): a defensive -1 to hit,
            // emitted at Shooting_OnHitRollComplete. The condition is fine and the effect produces a real
            // operation, so every other check passes — but the hit dice are already rolled by then, and only
            // Save deltas fold from that hook. Core Stealth, the identical shape, sits at the modifier hook.
            var rule = Passive("Lint Deaf Hook", new HookEntry(EHookID.Shooting_OnHitRollComplete,
                new Condition.AllModelsHaveThisRule(),   // RuleValidator requires this of Subject-seat rules
                new Effect.RollModifier(ERollKind.Hit, Delta: -1),
                ELifetime.ThisAttack,
                ERuleSeat.Subject));

            IReadOnlyList<string> problems = RuleFireLint.Check(rule);

            Assert.That(problems, Has.Some.Contains("no stage at this hook reads what it produces"));
            Assert.That(problems, Has.Some.Contains("ApplyRollModifier(Hit)"),
                "The report must name the roll kind - ApplyRollModifier(Save) at the same hook IS read.");
        }

        [Test]
        public void PassiveEntry_ProducingAConsumedOperation_AtTheSameHook_IsClean()
        {
            // The other half of the pair: Shielded's +1 to defense, same hook and seat, and read. Pins that
            // the consumption map keys on the operation's roll kind rather than its type.
            var rule = Passive("Lint Heard Hook", new HookEntry(EHookID.Shooting_OnHitRollComplete,
                new Condition.AllModelsHaveThisRule(),   // RuleValidator requires this of Subject-seat rules
                new Effect.RollModifier(ERollKind.Save, Delta: 1),
                ELifetime.ThisAttack,
                ERuleSeat.Subject));

            Assert.That(RuleFireLint.Check(rule), Is.Empty);
        }

        [Test]
        public void PassiveEntry_ProducingBothAReadAndAnIgnoredOperation_IsNotFlagged()
        {
            // A rule whose entry produces several operations is fine as long as SOMETHING reads one of them:
            // Rending emits a per-hit AP split at the hit-complete hook, and suppression ops ride along.
            // Only an entry whose ENTIRE output is ignored is a no-op.
            var rule = Passive("Lint Partly Heard", new HookEntry(EHookID.Shooting_OnSaveRollComplete,
                new Condition.Always(),
                new Effect.IgnoreRule("Regeneration"),
                ELifetime.ThisAttack));

            Assert.That(RuleFireLint.Check(rule), Is.Empty,
                "SuppressRule is resolved by the evaluator itself, so it is read wherever it is emitted.");
        }

        [Test]
        public void Ability_AtAHookNoStagePolls_IsFlagged()
        {
            var rule = new SpecialRuleDefinition("Lint Unpolled", NoEntries, new List<ActivatedAbility>
            {
                new(EHookID.Casting_OnSpellResolved, new Cost.OncePerGame(), AnySelector(),
                    new Effect.Heal(new DiceExpression.D3()), new Condition.Always()),
            });

            Assert.That(RuleFireLint.Check(rule), Has.Some.Contains("no engine stage gathers"));
        }

        [Test]
        public void Ability_WhoseOpsTheStageDrops_IsFlagged()
        {
            // The Breath Attack failure mode, reconstructed: InvokeReactivate is only executed by
            // DeterminePlayerTurnStage; offered at pre-attack it would be applied by nothing and
            // silently dropped — exactly how BUG-1 shipped (InvokeDealHits at pre-attack, pre-fix).
            var rule = new SpecialRuleDefinition("Lint Dropped Op", NoEntries, new List<ActivatedAbility>
            {
                new(EHookID.Activation_OnBeforeAttackAction, new Cost.OncePerActivation(), AnySelector(),
                    new Effect.Reactivate(), new Condition.Always()),
            });

            Assert.That(RuleFireLint.Check(rule), Has.Some.Contains("silently dropped"));
        }

        [Test]
        public void Ability_SameDroppedOpAtItsProperHook_PassesLint()
        {
            // The counterpart: the identical effect at the hook whose stage executes it is clean.
            var rule = new SpecialRuleDefinition("Lint Proper Hook", NoEntries, new List<ActivatedAbility>
            {
                new(EHookID.Activation_OnNextActivatorRequested, new Cost.OncePerGame(), AnySelector(),
                    new Effect.Reactivate(), new Condition.Always()),
            });

            Assert.That(RuleFireLint.Check(rule), Is.Empty);
        }

        [Test]
        public void CompositionGates_AreSatisfiedFromTheConditionTree()
        {
            // UnitHasRule + TokenPresent gates must be pre-satisfied by the lint, not reported as
            // unfireable — this pins the condition-satisfier that Harassing Boost / Disembark rely on.
            var rule = Passive("Lint Gated", new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                new Condition.And(new Condition.UnitHasRule("Some Base Rule"),
                    new Condition.TokenPresent(TokenType.SpellTokens)),
                new Effect.MovementBonus(EActionType.Advance, DistanceInches: 2f),
                ELifetime.ThisActivation));

            Assert.That(RuleFireLint.Check(rule), Is.Empty);
        }

        private static TargetSelector AnySelector() => new(RangeInches: 6f, MinCount: 1, MaxCount: 1,
            TargetAffinity: ETargetAffinity.Friend, RequireLineOfSight: false);
    }
}
