using System;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using NUnit.Framework;

namespace FDG.Tests
{
    // #151 Step 1: the engine-side token display metadata + resolution. Covers the catalog (single source
    // of truth + Create factory) and TokenDisplay (valence/identity resolution + Describe synthesis). No
    // rendering — this is the semantic layer the GUI consumes.
    [TestFixture]
    public class TokenDisplayTests
    {
        // --- Valence resolution -----------------------------------------------------------------------

        [Test]
        public void Valence_FixedTypes_ComeFromCatalog()
        {
            Assert.That(TokenDisplay.ResolveValence(Shaken(), null), Is.EqualTo(EValence.Negative));
            Assert.That(TokenDisplay.ResolveValence(Fatigued(), null), Is.EqualTo(EValence.Negative));
            Assert.That(TokenDisplay.ResolveValence(SpellTokens(3), null), Is.EqualTo(EValence.Positive));
            Assert.That(TokenDisplay.ResolveValence(ArrivedFromReserve(), null), Is.EqualTo(EValence.Neutral));
        }

        [Test]
        public void Valence_RollModifiers_FromPayloadSign()
        {
            Assert.That(TokenDisplay.ResolveValence(HitMod(1), null), Is.EqualTo(EValence.Positive));
            Assert.That(TokenDisplay.ResolveValence(HitMod(-1), null), Is.EqualTo(EValence.Negative));
            Assert.That(TokenDisplay.ResolveValence(SaveMod(-2), null), Is.EqualTo(EValence.Negative));
            Assert.That(TokenDisplay.ResolveValence(HitMod(0), null), Is.EqualTo(EValence.Neutral));
        }

        [Test]
        public void Valence_RuleGrant_FromGrantedRuleValence()
        {
            RuleResolver rules = ResolverWith(("Regeneration", EValence.Positive), ("Curse", EValence.Negative));

            Assert.That(TokenDisplay.ResolveValence(Grant("Regeneration"), rules), Is.EqualTo(EValence.Positive));
            Assert.That(TokenDisplay.ResolveValence(Grant("Curse"), rules), Is.EqualTo(EValence.Negative));
        }

        [Test]
        public void Valence_RuleGrant_FallsBackToNeutral_WhenRuleUnresolvable()
        {
            RuleResolver rules = ResolverWith(("Regeneration", EValence.Positive));

            // No resolver supplied, and a name the resolver doesn't know — both fall back to Neutral.
            Assert.That(TokenDisplay.ResolveValence(Grant("Regeneration"), null), Is.EqualTo(EValence.Neutral));
            Assert.That(TokenDisplay.ResolveValence(Grant("Unknown"), rules), Is.EqualTo(EValence.Neutral));
        }

        [Test]
        public void Valence_Mark_IsAlwaysNegative_RegardlessOfGrantedRule()
        {
            // A Mark carries a RuleGrant payload to a rule that is POSITIVE for the attacker, but being
            // marked is bad for the bearer — so the Mark type's fixed Negative valence must win over the
            // payload's granted-rule valence. (Bearer-relative valence.)
            RuleResolver rules = ResolverWith(("Hit Bonus", EValence.Positive));

            Assert.That(TokenDisplay.ResolveValence(Mark("Hit Bonus"), rules), Is.EqualTo(EValence.Negative));
        }

        // --- Prominence / visibility ------------------------------------------------------------------

        [Test]
        public void Prominence_ClassifiesEachFamily()
        {
            Assert.That(TokenDisplay.Resolve(Shaken(), null).Prominence, Is.EqualTo(ETokenProminence.FirstClass));
            Assert.That(TokenDisplay.Resolve(SpellTokens(2), null).Prominence, Is.EqualTo(ETokenProminence.FirstClass));
            Assert.That(TokenDisplay.Resolve(ArrivedFromReserve(), null).Prominence, Is.EqualTo(ETokenProminence.Invisible));
            Assert.That(TokenDisplay.Resolve(Grant("Regeneration"), null).Prominence, Is.EqualTo(ETokenProminence.Normal));
        }

        [Test]
        public void Prominence_AbilityUsedMarkers_AreInvisibleByPrefix()
        {
            var used = new Token(new TokenType("AbilityUsed:Furious"), 1, new TokenClearTrigger.ActivationEnd());
            Assert.That(TokenDisplay.Resolve(used, null).Prominence, Is.EqualTo(ETokenProminence.Invisible));
        }

        [Test]
        public void Prominence_UnknownType_DefaultsToNormalVisible()
        {
            var unknown = new Token(new TokenType("SomeCustomMarker"), 1, new TokenClearTrigger.ManualOnly());
            TokenDisplayInfo info = TokenDisplay.Resolve(unknown, null);
            Assert.That(info.Prominence, Is.EqualTo(ETokenProminence.Normal));
            Assert.That(info.Valence, Is.EqualTo(EValence.Neutral));
        }

        // --- Display identity -------------------------------------------------------------------------

        [Test]
        public void DisplayId_GrantCarriers_UseGrantedRuleName_SoTheyLookDistinct()
        {
            Assert.That(TokenDisplay.ResolveDisplayId(Shaken()), Is.EqualTo(TokenType.SHAKEN_ID));
            Assert.That(TokenDisplay.ResolveDisplayId(Grant("Regeneration")), Is.EqualTo("Regeneration"));
            Assert.That(TokenDisplay.ResolveDisplayId(Grant("Stealth")), Is.EqualTo("Stealth"));
            Assert.That(TokenDisplay.ResolveDisplayId(Mark("Hit Bonus")), Is.EqualTo("Hit Bonus"));
        }

        // --- Describe synthesis -----------------------------------------------------------------------

        [Test]
        public void DescribeName_SynthesizesPerFamily()
        {
            Assert.That(TokenDisplay.DescribeName(HitMod(1)), Is.EqualTo("+1 to Hit"));
            Assert.That(TokenDisplay.DescribeName(SaveMod(-1)), Is.EqualTo("-1 to Defense"));
            Assert.That(TokenDisplay.DescribeName(Grant("Regeneration")), Is.EqualTo("Regeneration"));
            Assert.That(TokenDisplay.DescribeName(Mark("Hit Bonus")), Is.EqualTo("Marked: Hit Bonus"));
            Assert.That(TokenDisplay.DescribeName(Shaken()), Is.EqualTo("Shaken"));
        }

        [Test]
        public void DescribeDetail_SynthesizesHoverLines()
        {
            Assert.That(TokenDisplay.DescribeDetail(HitMod(1)), Is.EqualTo("+1 to Hit rolls, this round."));
            Assert.That(TokenDisplay.DescribeDetail(Grant("Regeneration")),
                Is.EqualTo("Gains Regeneration, next time it applies."));
            Assert.That(TokenDisplay.DescribeDetail(SpellTokens(3)),
                Is.EqualTo("3 spell tokens to spend on casting this round."));
            Assert.That(TokenDisplay.DescribeDetail(SpellTokens(1)),
                Is.EqualTo("1 spell token to spend on casting this round."));
            Assert.That(TokenDisplay.DescribeDetail(Shaken()), Does.Contain("Shaken"));
        }

        [Test]
        public void DescribeLifetime_CoversTheTriggers()
        {
            Assert.That(TokenDisplay.DescribeLifetime(new TokenClearTrigger.ManualOnly()), Is.EqualTo("until removed"));
            Assert.That(TokenDisplay.DescribeLifetime(new TokenClearTrigger.RoundEnd()), Is.EqualTo("this round"));
            Assert.That(TokenDisplay.DescribeLifetime(new TokenClearTrigger.ActivationEnd()), Is.EqualTo("this activation"));
            Assert.That(TokenDisplay.DescribeLifetime(new TokenClearTrigger.FirstTrigger()), Is.EqualTo("next time it applies"));
        }

        [Test]
        public void Resolve_PopulatesEveryField()
        {
            RuleResolver rules = ResolverWith(("Regeneration", EValence.Positive));
            TokenDisplayInfo info = TokenDisplay.Resolve(Grant("Regeneration"), rules, isModelScoped: true);

            Assert.Multiple(() =>
            {
                Assert.That(info.DisplayId, Is.EqualTo("Regeneration"));
                Assert.That(info.Name, Is.EqualTo("Regeneration"));
                Assert.That(info.Description, Is.EqualTo("Gains Regeneration, next time it applies."));
                Assert.That(info.Valence, Is.EqualTo(EValence.Positive));
                Assert.That(info.Prominence, Is.EqualTo(ETokenProminence.Normal));
                Assert.That(info.Count, Is.EqualTo(1));
                Assert.That(info.IsModelScoped, Is.True);
                Assert.That(info.LifetimeText, Is.EqualTo("next time it applies"));
                Assert.That(info.ColorOverride, Is.Null);
            });
        }

        [Test]
        public void Resolve_SpellTokens_CarryBlueOverride()
        {
            Assert.That(TokenDisplay.Resolve(SpellTokens(2), null).ColorOverride, Is.EqualTo(ETokenColor.Blue));
            Assert.That(TokenDisplay.Resolve(Shaken(), null).ColorOverride, Is.Null);
        }

        // --- Catalog Create factory (single source of truth) ------------------------------------------

        [Test]
        public void Create_FixedTypes_StampTheCatalogDefaultTrigger()
        {
            // Behavior-preserving: matches the exact tokens the old scattered call sites produced.
            Assert.That(TokenDefinitionCatalog.Create(TokenType.Shaken),
                Is.EqualTo(new Token(TokenType.Shaken, 1, new TokenClearTrigger.ManualOnly())));
            Assert.That(TokenDefinitionCatalog.Create(TokenType.Fatigued),
                Is.EqualTo(new Token(TokenType.Fatigued, 1, new TokenClearTrigger.RoundEnd())));
            Assert.That(TokenDefinitionCatalog.Create(TokenType.ArrivedFromReserve),
                Is.EqualTo(new Token(TokenType.ArrivedFromReserve, 1, new TokenClearTrigger.RoundEnd())));
        }

        [Test]
        public void Create_EmbarkedIn_ForwardsOwner_KeepsManualTrigger()
        {
            var owner = new UnitID(Guid.NewGuid());
            Token tok = TokenDefinitionCatalog.Create(TokenType.EmbarkedIn, owner: owner);

            Assert.That(tok, Is.EqualTo(new Token(TokenType.EmbarkedIn, 1,
                new TokenClearTrigger.ManualOnly(), OwnerUnitID: owner)));
        }

        [Test]
        public void Create_CarrierTypeWithoutDefaultTrigger_Throws()
        {
            // Roll-modifier carriers have no fixed lifetime — their trigger is set at the grant site.
            Assert.Throws<InvalidOperationException>(() => TokenDefinitionCatalog.Create(TokenType.HitRollModifier));
        }

        [Test]
        public void Create_RespectsCountAndExplicitOverride()
        {
            Token tok = TokenDefinitionCatalog.Create(TokenType.SpellTokens, count: 4,
                clearOverride: new TokenClearTrigger.RoundEnd());

            Assert.That(tok.Count, Is.EqualTo(4));
            Assert.That(tok.ClearTrigger, Is.InstanceOf<TokenClearTrigger.RoundEnd>());
        }

        // --- Helpers ----------------------------------------------------------------------------------

        private static RuleResolver ResolverWith(params (string name, EValence valence)[] rules)
        {
            var resolver = new RuleResolver();
            foreach ((string name, EValence valence) in rules)
            {
                resolver.Register(new SpecialRuleDefinition(name,
                    Array.Empty<HookEntry>(), Array.Empty<ActivatedAbility>(), Valence: valence));
            }

            return resolver;
        }

        private static Token Shaken() => new(TokenType.Shaken, 1, new TokenClearTrigger.ManualOnly());
        private static Token Fatigued() => new(TokenType.Fatigued, 1, new TokenClearTrigger.RoundEnd());
        private static Token SpellTokens(int n) => new(TokenType.SpellTokens, n, new TokenClearTrigger.ManualOnly());
        private static Token ArrivedFromReserve() =>
            new(TokenType.ArrivedFromReserve, 1, new TokenClearTrigger.RoundEnd());

        private static Token HitMod(int delta) => new(TokenType.HitRollModifier, 1,
            new TokenClearTrigger.RoundEnd(), Payload: new TokenPayload.StatModifier(delta));

        private static Token SaveMod(int delta) => new(TokenType.SaveRollModifier, 1,
            new TokenClearTrigger.RoundEnd(), Payload: new TokenPayload.StatModifier(delta));

        private static Token Grant(string rule) => new(TokenType.RuleGrant, 1,
            new TokenClearTrigger.FirstTrigger(), Payload: new TokenPayload.RuleGrant(rule, ELifetime.NextTrigger));

        private static Token Mark(string rule) => new(TokenType.Mark, 1,
            new TokenClearTrigger.ManualOnly(), Payload: new TokenPayload.RuleGrant(rule, ELifetime.NextTrigger));
    }
}
