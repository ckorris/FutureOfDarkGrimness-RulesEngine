using System;
using System.Collections.Generic;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using NUnit.Framework;

namespace FDG.Tests
{
    // #305: the "Moved" token is stamped on EVERY unit that moves, but only a rule that tests it (Mobile
    // Artillery's defensive arm) makes it worth showing. TokenDefinition.VisibleOnlyWhenRead + the
    // TokenReadership walk decide that per BEARER, so the chip appears on the artillery piece and nowhere
    // else. These pin both halves: the readership walk, and the prominence it drives.
    [TestFixture]
    public class MovedTokenVisibilityTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        // ── Prominence ────────────────────────────────────────────────────────────────────────────────

        [Test]
        public void MovedToken_OnAUnitWithNoRuleReadingIt_IsInvisible()
        {
            DataBinding<UnitData> unit = MakeUnit();
            Token moved = TokenDefinitionCatalog.Create(TokenType.MovedThisRound);

            Assert.That(TokenDisplay.ResolveProminence(moved, unit.GetValue()),
                Is.EqualTo(ETokenProminence.Invisible),
                "an ordinary unit that moved has nothing to learn from the chip.");
        }

        [Test]
        public void MovedToken_OnABearerWhoseRuleReadsIt_KeepsItsProminence()
        {
            DataBinding<UnitData> unit = MakeUnit();
            unit.GetValue().AttachRuleDefinition(HoldsPositionRule());
            Token moved = TokenDefinitionCatalog.Create(TokenType.MovedThisRound);

            Assert.That(TokenDisplay.ResolveProminence(moved, unit.GetValue()),
                Is.EqualTo(ETokenProminence.Normal),
                "the chip is the only visible explanation for why this unit's bonus switched off.");
        }

        [Test]
        public void MovedToken_WithNoBearerInHand_StaysVisible()
        {
            Token moved = TokenDefinitionCatalog.Create(TokenType.MovedThisRound);

            Assert.That(TokenDisplay.ResolveProminence(moved, bearer: null),
                Is.EqualTo(ETokenProminence.Normal),
                "a caller that can't prove the token is unread must not hide it.");
        }

        [Test]
        public void ATokenWithoutTheFlag_IgnoresReadership()
        {
            // Shaken means something to the player whether or not any rule the unit carries tests it.
            DataBinding<UnitData> unit = MakeUnit();
            Token shaken = TokenDefinitionCatalog.Create(TokenType.Shaken);

            Assert.That(TokenDisplay.ResolveProminence(shaken, unit.GetValue()),
                Is.EqualTo(ETokenProminence.FirstClass));
        }

        // ── Readership walk ───────────────────────────────────────────────────────────────────────────

        [Test]
        public void Readership_FindsATokenTestNestedInsideAndNot()
        {
            // Mobile Artillery's actual shape: And(AttackedFromOverInches, Not(TokenPresent(Moved))).
            // A top-level type check would miss it, which is why the walk recurses.
            DataBinding<UnitData> unit = MakeUnit();
            unit.GetValue().AttachRuleDefinition(HoldsPositionRule());

            Assert.That(TokenReadership.IsReadByAnyRule(unit.GetValue(), TokenType.MovedThisRound), Is.True);
        }

        [Test]
        public void Readership_DoesNotMatchADifferentTokenType()
        {
            DataBinding<UnitData> unit = MakeUnit();
            unit.GetValue().AttachRuleDefinition(HoldsPositionRule());

            Assert.That(TokenReadership.IsReadByAnyRule(unit.GetValue(), TokenType.Fatigued), Is.False);
        }

        [Test]
        public void Readership_SeesAPerModelRule()
        {
            // #093 per-model rules: a champion upgrade holding the reader must light the unit's chip too.
            DataBinding<UnitData> unit = MakeUnit();
            unit.GetValue().ModelBindings[0].GetValue().AttachRuleDefinition(HoldsPositionRule());

            Assert.That(TokenReadership.IsReadByAnyRule(unit.GetValue(), TokenType.MovedThisRound), Is.True);
        }

        [Test]
        public void Readership_IgnoresARuleThatOnlyWritesTheToken()
        {
            // Stamping is not reading. If a granting effect counted, the movement stage's universal stamp
            // would make every token everywhere "read" and nothing would ever be hidden.
            DataBinding<UnitData> unit = MakeUnit();
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("StampsMoved",
                new SpecialRuleDefinition("StampsMoved",
                    new[]
                    {
                        new HookEntry(EHookID.Movement_OnMoveResolved, new Condition.Always(),
                            new Effect.GrantToken(TokenType.MovedThisRound, new ValueSource.Literal(1),
                                new TokenClearTrigger.RoundEnd()),
                            ELifetime.ThisActivation),
                    },
                    Array.Empty<ActivatedAbility>()),
                Array.Empty<RuleArgument>()));

            Assert.That(TokenReadership.IsReadByAnyRule(unit.GetValue(), TokenType.MovedThisRound), Is.False);
        }

        [Test]
        public void Readership_IsFalseForANullBearer()
        {
            Assert.That(TokenReadership.IsReadByAnyRule(null, TokenType.MovedThisRound), Is.False);
        }

        // Mobile Artillery's defensive arm, authored locally so the test doesn't depend on shipped book
        // data: "as long as this unit hasn't moved this round, enemies shooting it from over 9in get -2".
        private static ResolvedRule HoldsPositionRule() =>
            new("HoldsPosition",
                new SpecialRuleDefinition("HoldsPosition",
                    new[]
                    {
                        new HookEntry(EHookID.Shooting_OnHitRollModifier,
                            new Condition.And(
                                new Condition.AttackedFromOverInches(9f),
                                new Condition.Not(new Condition.TokenPresent(TokenType.MovedThisRound))),
                            new Effect.RollModifier(ERollKind.Hit, -2),
                            ELifetime.ThisAttack, ERuleSeat.Subject),
                    },
                    Array.Empty<ActivatedAbility>()),
                Array.Empty<RuleArgument>());

        private DataBinding<UnitData> MakeUnit()
        {
            var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
                initialPosition: new Position(0, 0), gameDataStore: _store);
            var modelBindings = new List<DataBinding<ModelData>>
            {
                _store.GetDataBinding<ModelData>(_store.Create(model)),
            };
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "Tester", quality: 4, defense: 4,
                modelBindings: modelBindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
