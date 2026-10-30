using System.Collections.Generic;
using System.Linq;
using FDG.Data;
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
    // #427 - OPR 3.6 Unstoppable: "Ignores Regeneration, and ignores all negative modifiers to this weapon which
    // are not from this model's special rules." (3.5 ignored ALL negative modifiers.) The Regeneration facet is
    // pinned in WoundIgnoreRuleIntegrationTests; this file pins the modifier facet, in two layers:
    //
    //   * evaluator level (TestRuleHarness): which operations survive, per source - the defender's Stealth /
    //     Evasive / Fortified, an enemy-placed debuff grant, and the attacker's OWN penalties, which stay;
    //   * stage level (the REAL DetermineHitRollStage / RollToHitStage / ShootingForecast): the numbers a roll
    //     is actually made with, including a granted negative "to hit" token, which the evaluator never sees.
    //
    // "Negative modifiers to this weapon" is read as the two numbers a rule can lower on an attack - the to-hit
    // roll and the weapon's AP (#427 D1). Range and Defense modifiers are not touched (D2).
    [TestFixture]
    public class UnstoppableRuleIntegrationTests
    {
        private static readonly SpecialRuleDefinition BadShot = new("Bad Shot",
            new[]
            {
                new HookEntry(EHookID.Shooting_OnHitRollModifier, new Condition.Always(),
                    new Effect.RollModifier(ERollKind.Hit, -1), ELifetime.ThisAttack),
            },
            System.Array.Empty<ActivatedAbility>(), Valence: EValence.Negative,
            Description: "Gets -1 to hit when attacking.");

        // A weapon's own shoot-after-move penalty (the shape 3.5 Indirect had, #425): -1 to hit after moving.
        private static readonly SpecialRuleDefinition MovingPenalty = new("Moving Penalty",
            new[]
            {
                new HookEntry(EHookID.Shooting_OnHitRollModifier,
                    new Condition.And(new Condition.AfterMoving(), new Condition.Not(new Condition.IsMelee())),
                    new Effect.RollModifier(ERollKind.Hit, -1), ELifetime.ThisAttack),
            },
            System.Array.Empty<ActivatedAbility>(), ERuleScope.Weapon, Valence: EValence.Negative);

        // The unit-level mirror of an enemy spell's debuff (Corrode Weapons): a rule the shooter is GRANTED that
        // costs its shot 1 AP. What the grant's source is - the shooter's own rules or an enemy caster - decides
        // whether Unstoppable ignores it.
        private static readonly SpecialRuleDefinition Corroded = new("Corroded",
            new[]
            {
                new HookEntry(EHookID.Shooting_OnHitRollComplete, new Condition.Always(),
                    new Effect.ReduceArmorPenetration(1), ELifetime.ThisAttack),
            },
            System.Array.Empty<ActivatedAbility>(), Valence: EValence.Negative,
            Description: "Loses AP(1).");

        // --- evaluator level: which modifiers survive -----------------------------------------------------------

        [Test]
        public void Unstoppable_IgnoresTheTargetsToHitPenalties()
        {
            var h = Harness();
            (IUnit attacker, IUnit defender, Weapon gun) = Pair(h, defenderRules: new[] { "Stealth", "Evasive" });

            Assert.That(HitNet(HitOps(h, attacker, defender, gun, distance: 12f)), Is.EqualTo(-2),
                "control: Stealth (over 9in) and Evasive both bite a plain weapon");

            h.AttachWeaponRule(gun, CoreRuleCatalog.Unstoppable);
            Assert.That(HitNet(HitOps(h, attacker, defender, gun, distance: 12f)), Is.EqualTo(0),
                "neither is from the shooter's own rules: both ignored");
        }

        [Test]
        public void Unstoppable_KeepsThePenaltiesFromTheBearersOwnRules()
        {
            // "which are not from this model's special rules": a unit rule, a weapon's own rule, and a
            // shoot-after-move penalty are all the shooter's own, so all of them stay. (The third was 3.5 Indirect's
            // -1, dropped by Core Rules v3.6.0 - #425 - so a weapon-scoped stand-in carries the same shape.)
            var h = Harness();
            (IUnit attacker, IUnit defender, Weapon gun) = Pair(h, attackerRules: new[] { "Bad Shot" },
                defenderRules: new[] { "Stealth" });
            h.AttachWeaponRule(gun, WeaponRules36Definitions.Imprecise);
            h.AttachWeaponRule(gun, MovingPenalty);
            h.AttachWeaponRule(gun, CoreRuleCatalog.Unstoppable);

            IReadOnlyList<RuleOperation> ops = HitOps(h, attacker, defender, gun, distance: 12f, moved: true);

            Assert.That(HitNet(ops), Is.EqualTo(-3),
                "Bad Shot -1, Imprecise -1, the moving penalty -1 stay; the target's Stealth -1 is ignored");
        }

        [Test]
        public void Unstoppable_KeepsEveryPositiveModifier()
        {
            var h = Harness();
            (IUnit attacker, IUnit defender, Weapon gun) = Pair(h, defenderRules: new[] { "Stealth" });
            h.AttachWeaponRule(gun, CoreRuleCatalog.Precise);
            h.AttachWeaponRule(gun, CoreRuleCatalog.Unstoppable);

            Assert.That(HitNet(HitOps(h, attacker, defender, gun, distance: 12f)), Is.EqualTo(1),
                "Precise's +1 is a bonus, not a negative modifier: it stays while Stealth's -1 goes");
        }

        [Test]
        public void Unstoppable_IgnoresTheTargetsApReduction()
        {
            var h = Harness();
            (IUnit attacker, IUnit defender, Weapon gun) = Pair(h, defenderRules: new[] { "Fortified" });

            Assert.That(ApReduction(CompleteOps(h, attacker, defender, gun)), Is.EqualTo(1),
                "control: Fortified takes 1 AP off a plain weapon");

            h.AttachWeaponRule(gun, CoreRuleCatalog.Unstoppable);
            Assert.That(ApReduction(CompleteOps(h, attacker, defender, gun)), Is.EqualTo(0),
                "an AP reduction is a negative modifier to this weapon, and the defender's own rule is not the bearer's");
        }

        [Test]
        public void Unstoppable_IgnoresAGrantAnEnemyPlaced_ButNotOneTheBearerPlacedOnItself()
        {
            // The grant is the same rule either way; what differs is who PLACED the token (Token.OwnerUnitID).
            var h = Harness();
            h.Register(Corroded);
            (IUnit attacker, IUnit defender, Weapon gun) = Pair(h);
            h.AttachWeaponRule(gun, CoreRuleCatalog.Unstoppable);

            GrantTo(attacker, "Corroded", placedBy: defender.ID);
            Assert.That(ApReduction(CompleteOps(h, attacker, defender, gun)), Is.EqualTo(0),
                "an enemy caster's debuff is 'not from this model's special rules'");

            attacker.Tokens.RemoveTokens(TokenType.RuleGrant);
            GrantTo(attacker, "Corroded", placedBy: null);
            Assert.That(ApReduction(CompleteOps(h, attacker, defender, gun)), Is.EqualTo(1),
                "a self-placed grant is the model's own, so it still bites");
        }

        [Test]
        public void WithoutUnstoppable_EveryModifierStillFolds()
        {
            // The control the whole file leans on: an enemy-placed grant and a defender penalty both count.
            var h = Harness();
            h.Register(Corroded);
            (IUnit attacker, IUnit defender, Weapon gun) = Pair(h, defenderRules: new[] { "Stealth", "Fortified" });
            GrantTo(attacker, "Corroded", placedBy: defender.ID);

            Assert.That(HitNet(HitOps(h, attacker, defender, gun, distance: 12f)), Is.EqualTo(-1));
            Assert.That(ApReduction(CompleteOps(h, attacker, defender, gun)), Is.EqualTo(2),
                "Fortified 1 + the placed Corroded 1");
        }

        [Test]
        public void Unstoppable_OnlyCoversItsOwnWeapon()
        {
            // "negative modifiers to THIS weapon": the rule rides the weapon, so a second weapon without it is
            // still penalised by the same target.
            var h = Harness();
            (IUnit attacker, IUnit defender, Weapon gun) = Pair(h, defenderRules: new[] { "Stealth" });
            h.AttachWeaponRule(gun, CoreRuleCatalog.Unstoppable);
            var other = new Weapon("Pistol", 12f, 1, 0);

            Assert.That(HitNet(HitOps(h, attacker, defender, gun, distance: 12f)), Is.EqualTo(0));
            Assert.That(HitNet(HitOps(h, attacker, defender, other, distance: 12f)), Is.EqualTo(-1));
        }

        [Test]
        public void TheCombatKindVariants_IgnoreOnlyInTheirOwnKind()
        {
            var h = Harness();
            (IUnit attacker, IUnit defender, Weapon gun) = Pair(h, defenderRules: new[] { "Evasive" });
            h.AttachWeaponRule(gun, CoreRuleCatalog.UnstoppableWhenShooting);
            Assert.That(HitNet(HitOps(h, attacker, defender, gun, distance: 12f, melee: false)), Is.EqualTo(0),
                "'Unstoppable when shooting' ignores it when shooting");
            Assert.That(HitNet(HitOps(h, attacker, defender, gun, distance: 1f, melee: true)), Is.EqualTo(-1),
                "...and not in melee");

            var h2 = Harness();
            (IUnit a2, IUnit d2, Weapon blade) = Pair(h2, defenderRules: new[] { "Evasive" });
            h2.AttachWeaponRule(blade, CoreRuleCatalog.UnstoppableInMelee);
            Assert.That(HitNet(HitOps(h2, a2, d2, blade, distance: 1f, melee: true)), Is.EqualTo(0),
                "'Unstoppable in melee' ignores it in melee");
            Assert.That(HitNet(HitOps(h2, a2, d2, blade, distance: 12f, melee: false)), Is.EqualTo(-1),
                "...and not when shooting");
        }

        [Test]
        public void Unstoppable_StillSuppressesRegeneration()
        {
            // The first facet is untouched: the same rule still carries IgnoreRule("Regeneration").
            Assert.That(CoreRuleCatalog.Unstoppable.Passive.Any(e =>
                e.HookID == EHookID.Shooting_OnSaveRollComplete
                && e.Effect is Effect.IgnoreRule { RuleName: "Regeneration" }), Is.True);
            Assert.That(CoreRuleCatalog.Unstoppable.Description,
                Is.EqualTo("Ignores Regeneration, and ignores all negative modifiers to this weapon which are not " +
                           "from this model's special rules."), "the 3.6 wording");
        }

        // --- the log says what was ignored ----------------------------------------------------------------------

        [Test]
        public void ALiveEvaluation_NarratesEachIgnoredModifier_AndNothingWhenNothingWasIgnored()
        {
            var log = new CapturingLog();
            var h = Harness(log);
            (IUnit attacker, IUnit defender, Weapon gun) = Pair(h, defenderRules: new[] { "Stealth" });
            h.AttachWeaponRule(gun, CoreRuleCatalog.Unstoppable);

            h.EvaluateAll(new HitRollModifierContext(attacker, defender, 12f), Actor(attacker, gun), Subject(defender));
            Assert.That(log.Lines, Has.Some.Contains("Unstoppable ignored").And.Contains("Stealth"),
                "the player can see why the -1 never applied");

            log.Lines.Clear();
            h.EvaluateAll(new HitRollModifierContext(attacker, defender, 5f), Actor(attacker, gun), Subject(defender));
            Assert.That(log.Lines, Is.Empty,
                "within 9in Stealth is silent, so there is nothing to ignore - and the bare marker is never narrated");
        }

        // --- stage level: the numbers a roll is made with -------------------------------------------------------

        [Test]
        public async Task DetermineHitRollStage_UnstoppableWeaponHitsOnTheUnpenalisedNumber()
        {
            using var world = new StageWorld();
            DataBinding<UnitData> stealthy = world.Defender(13f, new ResolvedRule("Stealth", CoreRuleCatalog.Stealth));

            Assert.That((await world.HitRoll(world.Weapon(), stealthy, 13f)).HitRollNeeded, Is.EqualTo(5),
                "control: Quality 4 with Stealth's -1");
            Assert.That((await world.HitRoll(world.Weapon(CoreRuleCatalog.Unstoppable), stealthy, 13f)).HitRollNeeded,
                Is.EqualTo(4), "Unstoppable ignores it");
            Assert.That((await world.HitRoll(world.Weapon(CoreRuleCatalog.Unstoppable, WeaponRules36Definitions.Imprecise),
                stealthy, 13f)).HitRollNeeded, Is.EqualTo(5),
                "but its own Imprecise -1 stays, so the Unstoppable weapon is still penalised by itself");
        }

        [Test]
        public async Task DetermineHitRollStage_AnEnemyPlacedHitDebuffToken_IsIgnored_ASelfPlacedOneIsNot()
        {
            using var world = new StageWorld();
            DataBinding<UnitData> defender = world.Defender(10f);
            Weapon unstoppable = world.Weapon(CoreRuleCatalog.Unstoppable);

            DataBinding<UnitData> debuffed = world.Attacker();
            debuffed.GetValue().Tokens.AddToken(HitGrant(-1, placedBy: defender.GetValue().ID));
            Assert.That((await world.HitRoll(unstoppable, defender, 10f, debuffed)).HitRollNeeded, Is.EqualTo(4),
                "an enemy spell's 'next shot -1 to hit' is not from this model's special rules");
            Assert.That(debuffed.GetValue().Tokens.GetAllTokens(RollModifierTokens.TypeFor(ERollKind.Hit)), Is.Empty,
                "and the one-shot grant is still spent by this attack - it was the 'next time'");

            DataBinding<UnitData> plain = world.Attacker();
            plain.GetValue().Tokens.AddToken(HitGrant(-1, placedBy: defender.GetValue().ID));
            Assert.That((await world.HitRoll(world.Weapon(), defender, 10f, plain)).HitRollNeeded, Is.EqualTo(5),
                "control: a weapon without Unstoppable is penalised by the same token");

            DataBinding<UnitData> selfPenalised = world.Attacker();
            selfPenalised.GetValue().Tokens.AddToken(HitGrant(-1, placedBy: null));
            Assert.That((await world.HitRoll(unstoppable, defender, 10f, selfPenalised)).HitRollNeeded, Is.EqualTo(5),
                "a grant the bearer placed on itself is its own");
        }

        [Test]
        public async Task RollToHitStage_UnstoppableWeaponCarriesNoApReduction()
        {
            using var world = new StageWorld();
            DataBinding<UnitData> fortified = world.Defender(10f, new ResolvedRule("Fortified", CoreRuleCatalog.Fortified));

            Assert.That((await world.Hits(world.Weapon(), fortified, 10f)).ArmorPenetrationReduction, Is.EqualTo(1),
                "control");
            Assert.That((await world.Hits(world.Weapon(CoreRuleCatalog.Unstoppable), fortified, 10f))
                .ArmorPenetrationReduction, Is.EqualTo(0), "Fortified's AP-1 is a negative modifier to the weapon");
        }

        [Test]
        public void ShootingForecast_AgreesWithTheStage_IncludingTheGrantedToken()
        {
            using var world = new StageWorld();
            DataBinding<UnitData> defender = world.Defender(13f, new ResolvedRule("Stealth", CoreRuleCatalog.Stealth));
            Weapon gun = world.Weapon(CoreRuleCatalog.Unstoppable);
            DataBinding<UnitData> attacker = world.Attacker(gun);
            attacker.GetValue().Tokens.AddToken(HitGrant(-1, placedBy: defender.GetValue().ID));

            var forecast = ShootingForecast.Compute(world.Ctx, attacker, gun, defender,
                hasCover: false, ignoresCover: false, attackerMoved: false);

            Assert.That(forecast.HitRollNeeded, Is.EqualTo(4),
                "Stealth and the enemy-placed token are both ignored in the pre-roll number too");

            Weapon plainGun = world.Weapon();
            DataBinding<UnitData> plainAttacker = world.Attacker(plainGun);
            plainAttacker.GetValue().Tokens.AddToken(HitGrant(-1, placedBy: defender.GetValue().ID));
            Assert.That(ShootingForecast.Compute(world.Ctx, plainAttacker, plainGun, defender,
                hasCover: false, ignoresCover: false, attackerMoved: false).HitRollNeeded, Is.EqualTo(6),
                "control: Quality 4, Stealth -1 and the token -1");
        }

        // --- helpers --------------------------------------------------------------------------------------------

        private static TestRuleHarness Harness(ITextOutput? log = null)
        {
            var h = new TestRuleHarness(log: log);
            foreach (SpecialRuleDefinition rule in new[]
                     {
                         CoreRuleCatalog.Stealth, CoreRuleCatalog.Evasive, CoreRuleCatalog.Fortified,
                         CoreRuleCatalog.Indirect, CoreRuleCatalog.Precise, BadShot, MovingPenalty,
                     })
            {
                h.Register(rule);
            }

            return h;
        }

        private static (IUnit Attacker, IUnit Defender, Weapon Gun) Pair(TestRuleHarness h,
            string[]? attackerRules = null, string[]? defenderRules = null)
        {
            IUnit attacker = h.BuildUnit("P1", 1, attackerRules ?? System.Array.Empty<string>());
            IUnit defender = h.BuildUnit("P2", 1, defenderRules ?? System.Array.Empty<string>());
            return (attacker, defender, new Weapon("Cannon", 24f, 1, 0));
        }

        private static RuleParticipant Actor(IUnit unit, Weapon gun) => RuleParticipant.Actor(unit, gun, unit.Models);

        private static RuleParticipant Subject(IUnit unit) => RuleParticipant.Subject(unit, models: unit.Models);

        private static IReadOnlyList<RuleOperation> HitOps(TestRuleHarness h, IUnit attacker, IUnit defender,
            Weapon gun, float distance, bool melee = false, bool moved = false) =>
            h.EvaluateAll(new HitRollModifierContext(attacker, defender, distance, AttackerMoved: moved,
                IsMelee: melee), Actor(attacker, gun), Subject(defender));

        private static IReadOnlyList<RuleOperation> CompleteOps(TestRuleHarness h, IUnit attacker, IUnit defender,
            Weapon gun, bool melee = false) =>
            h.EvaluateAll(new HitRollCompleteContext(attacker, defender, TestDice.Faces(4), DistanceInches: 12f,
                IsMelee: melee), Actor(attacker, gun), Subject(defender));

        private static int HitNet(IReadOnlyList<RuleOperation> ops)
        {
            var sink = new RollModifierSink();
            sink.ApplyFrom(ops);
            return sink.Net(ERollKind.Hit);
        }

        private static int ApReduction(IReadOnlyList<RuleOperation> ops) =>
            ops.OfType<RuleOperation.ReduceArmorPenetration>().Sum(op => op.Amount);

        private static void GrantTo(IUnit unit, string ruleName, UnitID? placedBy) =>
            unit.Tokens.AddToken(new Token(TokenType.RuleGrant, 1, new TokenClearTrigger.ManualOnly(),
                Payload: new TokenPayload.RuleGrant(ruleName, ELifetime.ThisAttack), OwnerUnitID: placedBy));

        private static Token HitGrant(int delta, UnitID? placedBy) =>
            new Token(RollModifierTokens.TypeFor(ERollKind.Hit), 1, new TokenClearTrigger.FirstTrigger(),
                Payload: new TokenPayload.StatModifier(delta), OwnerUnitID: placedBy);

        private sealed class CapturingLog : ITextOutput
        {
            public List<string> Lines { get; } = new();

            public void Log(string message, TextColor? color = null) => Lines.Add(message);

            public void LogDebug(string message, TextColor? color = null) { }
        }

        // The REAL-stage fixture (the WeaponRules36ApRuleIntegrationTests setup): Quality 4 / Defense 4 units,
        // a 24in AP(0) weapon, every die a 6.
        private sealed class StageWorld : System.IDisposable
        {
            private const float BaseRadius = 0.5f;
            private readonly GameDataStore _store = GameDataStore.GameDataStoreBuilder.GetDefault();

            public WoundTestContext Ctx { get; }

            public StageWorld() =>
                Ctx = new WoundTestContext(_store, new CapturingWoundRequester(), new FixedDiceRoller(6));

            public void Dispose() { }

            public Weapon Weapon(params SpecialRuleDefinition[] rules)
            {
                var weapon = new Weapon("Test gun", rangeInches: 24f, attacks: 1, armorPenetration: 0);
                foreach (SpecialRuleDefinition rule in rules)
                {
                    weapon.AttachRuleDefinition(new ResolvedRule(rule.Name, rule));
                }

                return weapon;
            }

            public DataBinding<UnitData> Attacker(Weapon? carried = null)
            {
                var weapons = new List<Weapon>();
                if (carried != null) weapons.Add(carried);
                return Make("Shooter", z: 0f, weapons);
            }

            public DataBinding<UnitData> Defender(float edgeDistance, params ResolvedRule[] rules)
            {
                DataBinding<UnitData> defender = Make("Target", z: edgeDistance + 2f * BaseRadius, new List<Weapon>());
                foreach (ResolvedRule rule in rules) defender.GetValue().AttachRuleDefinition(rule);
                return defender;
            }

            private DataBinding<UnitData> Make(string name, float z, List<Weapon> weapons)
            {
                var model = new ModelData(BaseRadius, weapons, new Position(0f, z), _store);
                var bindings = new List<DataBinding<ModelData>> { _store.GetDataBinding<ModelData>(_store.Create(model)) };
                var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), name, quality: 4, defense: 4,
                    modelBindings: bindings);
                return _store.GetDataBinding<UnitData>(_store.Create(unit));
            }

            public async Task<DetermineHitRollResults> HitRoll(Weapon weapon, DataBinding<UnitData> defender,
                float edgeDistance, DataBinding<UnitData>? attacker = null)
            {
                attacker ??= Attacker();
                var stage = new DetermineHitRollStage<ICombatMetadata>(Ctx, new NoOpLayer<ICombatMetadata>());
                stage.NextStage.Bind("done");
                var metadata = new CombatMetadata(Ctx, attacker, defender, weapon, weaponCount: 1);
                await stage.Enter(metadata);

                Assert.That(metadata.QueryForResult(out DetermineHitRollResults result), Is.True);
                return result;
            }

            public async Task<RollToHitResults> Hits(Weapon weapon, DataBinding<UnitData> defender, float edgeDistance)
            {
                var stage = new RollToHitStage<ICombatMetadata>(Ctx, new NoOpLayer<ICombatMetadata>());
                stage.NextStage.Bind("done");
                var metadata = new CombatMetadata(Ctx, Attacker(), defender, weapon, weaponCount: 1);
                metadata.AddResult(new DetermineHitRollResults(4, 1));
                await stage.Enter(metadata);

                Assert.That(metadata.QueryForResult(out RollToHitResults result), Is.True);
                return result;
            }
        }
    }
}
