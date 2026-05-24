using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using NUnit.Framework;

namespace FDG.Tests
{
    [TestFixture]
    public class TokenContainerTests
    {
        // ----------------------------------------------------------------------
        // AddToken — new entries
        // ----------------------------------------------------------------------

        [Test]
        public void AddToken_NewType_FiresOnTokenAdded_AndStores()
        {
            var c = new TokenContainer();
            Token? fired = null;
            c.OnTokenAdded += t => fired = t;

            var token = MakeToken(TokenType.Shaken, count: 1);
            c.AddToken(token);

            Assert.That(c.HasToken(TokenType.Shaken), Is.True);
            Assert.That(c.GetTokenCount(TokenType.Shaken), Is.EqualTo(1));
            Assert.That(fired, Is.EqualTo(token));
        }

        [Test]
        public void AddToken_SameTypeSameOwner_StacksCount_FiresCountChanged()
        {
            var c = new TokenContainer();
            c.AddToken(MakeToken(TokenType.SpellTokens, count: 2));

            Token? countChanged = null;
            int addedFireCount = 0;
            c.OnTokenAdded += _ => addedFireCount++;
            c.OnTokenCountChanged += t => countChanged = t;

            c.AddToken(MakeToken(TokenType.SpellTokens, count: 3));

            Assert.That(c.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(5));
            Assert.That(countChanged, Is.Not.Null);
            Assert.That(countChanged!.Count, Is.EqualTo(5));
            Assert.That(addedFireCount, Is.Zero, "stacking onto an existing entry must not fire OnTokenAdded");
            Assert.That(c.GetAllTokens(TokenType.SpellTokens).Count(), Is.EqualTo(1),
                "stacking must merge into the existing entry, not create a second one");
        }

        [Test]
        public void AddToken_SameTypeDifferentOwner_KeepsSeparateEntries_FiresAdded()
        {
            var ownerA = new UnitID(Guid.NewGuid());
            var ownerB = new UnitID(Guid.NewGuid());
            var c = new TokenContainer();

            int addedFireCount = 0;
            c.OnTokenAdded += _ => addedFireCount++;

            c.AddToken(MakeToken(TokenType.SpellTokens, count: 1, owner: ownerA));
            c.AddToken(MakeToken(TokenType.SpellTokens, count: 1, owner: ownerB));

            Assert.That(addedFireCount, Is.EqualTo(2), "different owners must be separate entries, each firing OnTokenAdded");
            Assert.That(c.GetAllTokens(TokenType.SpellTokens).Count(), Is.EqualTo(2));
            Assert.That(c.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(2),
                "GetTokenCount sums across owners");
        }

        [Test]
        public void AddToken_NonPositiveCount_IsNoOp()
        {
            var c = new TokenContainer();
            c.AddToken(MakeToken(TokenType.Shaken, count: 0));

            Assert.That(c.HasToken(TokenType.Shaken), Is.False);
            Assert.That(c.GetAllTokens().Count(), Is.Zero);
        }

        // ----------------------------------------------------------------------
        // RemoveTokens
        // ----------------------------------------------------------------------

        [Test]
        public void RemoveTokens_PartialOnSingleEntry_FiresCountChanged_ReturnsRequested()
        {
            var c = new TokenContainer();
            c.AddToken(MakeToken(TokenType.SpellTokens, count: 5));

            Token? countChanged = null;
            int removedFireCount = 0;
            c.OnTokenCountChanged += t => countChanged = t;
            c.OnTokenRemoved += _ => removedFireCount++;

            int actually = c.RemoveTokens(TokenType.SpellTokens, count: 2);

            Assert.That(actually, Is.EqualTo(2));
            Assert.That(c.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(3));
            Assert.That(countChanged!.Count, Is.EqualTo(3));
            Assert.That(removedFireCount, Is.Zero, "partial removal must not fire OnTokenRemoved");
        }

        [Test]
        public void RemoveTokens_FullEntryDrain_FiresRemoved_RemovesEntry()
        {
            var c = new TokenContainer();
            c.AddToken(MakeToken(TokenType.SpellTokens, count: 3));

            Token? removed = null;
            int countChangedFireCount = 0;
            c.OnTokenRemoved += t => removed = t;
            c.OnTokenCountChanged += _ => countChangedFireCount++;

            int actually = c.RemoveTokens(TokenType.SpellTokens, count: 3);

            Assert.That(actually, Is.EqualTo(3));
            Assert.That(c.HasToken(TokenType.SpellTokens), Is.False);
            Assert.That(removed, Is.Not.Null);
            Assert.That(countChangedFireCount, Is.Zero, "fully draining an entry must not also fire OnTokenCountChanged");
        }

        [Test]
        public void RemoveTokens_MissingType_ReturnsZero_FiresNoEvent()
        {
            var c = new TokenContainer();
            int events = 0;
            c.OnTokenAdded += _ => events++;
            c.OnTokenRemoved += _ => events++;
            c.OnTokenCountChanged += _ => events++;

            int actually = c.RemoveTokens(TokenType.Shaken, count: 1);

            Assert.That(actually, Is.Zero);
            Assert.That(events, Is.Zero);
        }

        [Test]
        public void RemoveTokens_RequestMoreThanAvailable_RemovesAvailable_ReturnsActual()
        {
            var c = new TokenContainer();
            c.AddToken(MakeToken(TokenType.SpellTokens, count: 2));

            int actually = c.RemoveTokens(TokenType.SpellTokens, count: 5);

            Assert.That(actually, Is.EqualTo(2));
            Assert.That(c.HasToken(TokenType.SpellTokens), Is.False);
        }

        [Test]
        public void RemoveTokens_DrainsAcrossMultipleOwnerEntries_InInsertionOrder()
        {
            // Honours the "container is owner-agnostic for removal" choice: removing
            // across all matching entries regardless of which unit owns them.
            var ownerA = new UnitID(Guid.NewGuid());
            var ownerB = new UnitID(Guid.NewGuid());
            var c = new TokenContainer();
            c.AddToken(MakeToken(TokenType.SpellTokens, count: 2, owner: ownerA));
            c.AddToken(MakeToken(TokenType.SpellTokens, count: 3, owner: ownerB));

            int actually = c.RemoveTokens(TokenType.SpellTokens, count: 4);

            Assert.That(actually, Is.EqualTo(4));
            // Owner A entry (inserted first, count 2) fully drained;
            // owner B entry (count 3) decremented by 2, remaining 1.
            Assert.That(c.TokensWithOwner(ownerA).Count(), Is.Zero);
            Assert.That(c.TokensWithOwner(ownerB).Single().Count, Is.EqualTo(1));
        }

        [Test]
        public void RemoveTokens_NonPositiveCount_IsNoOp()
        {
            var c = new TokenContainer();
            c.AddToken(MakeToken(TokenType.Shaken, count: 1));

            int actually = c.RemoveTokens(TokenType.Shaken, count: 0);

            Assert.That(actually, Is.Zero);
            Assert.That(c.GetTokenCount(TokenType.Shaken), Is.EqualTo(1));
        }

        // ----------------------------------------------------------------------
        // Queries
        // ----------------------------------------------------------------------

        [Test]
        public void HasToken_TrueIfAnyOwnerHasIt()
        {
            var ownerA = new UnitID(Guid.NewGuid());
            var c = new TokenContainer();
            c.AddToken(MakeToken(TokenType.SpellTokens, count: 1, owner: ownerA));

            Assert.That(c.HasToken(TokenType.SpellTokens), Is.True);
        }

        [Test]
        public void GetTokenCount_MissingType_ReturnsZero()
        {
            var c = new TokenContainer();
            Assert.That(c.GetTokenCount(TokenType.Shaken), Is.Zero);
        }

        [Test]
        public void GetTokenCount_SumsAcrossOwners()
        {
            var ownerA = new UnitID(Guid.NewGuid());
            var ownerB = new UnitID(Guid.NewGuid());
            var c = new TokenContainer();
            c.AddToken(MakeToken(TokenType.SpellTokens, count: 2, owner: ownerA));
            c.AddToken(MakeToken(TokenType.SpellTokens, count: 3, owner: ownerB));

            Assert.That(c.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(5));
        }

        [Test]
        public void GetAllTokens_NoFilter_ReturnsEverything()
        {
            var c = new TokenContainer();
            c.AddToken(MakeToken(TokenType.Shaken, count: 1));
            c.AddToken(MakeToken(TokenType.SpellTokens, count: 3));

            var all = c.GetAllTokens().ToList();

            Assert.That(all, Has.Count.EqualTo(2));
        }

        [Test]
        public void GetAllTokens_WithTypeFilter_OnlyReturnsMatching()
        {
            var c = new TokenContainer();
            c.AddToken(MakeToken(TokenType.Shaken, count: 1));
            c.AddToken(MakeToken(TokenType.SpellTokens, count: 3));

            var spellOnly = c.GetAllTokens(TokenType.SpellTokens).ToList();

            Assert.That(spellOnly, Has.Count.EqualTo(1));
            Assert.That(spellOnly[0].Type, Is.EqualTo(TokenType.SpellTokens));
        }

        [Test]
        public void TokensWithOwner_FiltersByOwner()
        {
            var ownerA = new UnitID(Guid.NewGuid());
            var ownerB = new UnitID(Guid.NewGuid());
            var c = new TokenContainer();
            c.AddToken(MakeToken(TokenType.SpellTokens, count: 1, owner: ownerA));
            c.AddToken(MakeToken(TokenType.SpellTokens, count: 1, owner: ownerB));
            c.AddToken(MakeToken(TokenType.Shaken, count: 1));  // self-owned

            var ownerATokens = c.TokensWithOwner(ownerA).ToList();

            Assert.That(ownerATokens, Has.Count.EqualTo(1));
            Assert.That(ownerATokens[0].Type, Is.EqualTo(TokenType.SpellTokens));
            Assert.That(ownerATokens[0].OwnerUnitID, Is.EqualTo(ownerA));
        }

        // ----------------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------------

        /// <summary>
        /// Construct a Token with sane defaults — ManualOnly clear trigger, no
        /// payload, no debug hook origin. Tests can override fields as needed.
        /// </summary>
        private static Token MakeToken(TokenType type, int count, UnitID? owner = null) =>
            new Token(
                Type: type,
                Count: count,
                ClearTrigger: new TokenClearTrigger.ManualOnly(),
                Payload: null,
                OwnerUnitID: owner,
                CreatedAtHook: null);
    }
}
