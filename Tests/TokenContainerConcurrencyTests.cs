using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #328 — tokens are written on the engine thread and read every frame on the render thread (the
    /// chips over each unit and their tooltips). Handing out the live list, or a lazy <c>Where</c> view
    /// over it, let the renderer enumerate mid-mutation: "Collection was modified; enumeration operation
    /// may not execute" thrown out of the draw loop, taking the window with it.
    ///
    /// <para>The first test is the real contract and is fully deterministic; the stress test is the
    /// shape the crash actually took, and fails reliably against the pre-#328 container.</para>
    /// </summary>
    [TestFixture]
    public class TokenContainerConcurrencyTests
    {
        [Test]
        public void GetAllTokens_IsASnapshot_NotALiveView()
        {
            var c = new TokenContainer();
            c.AddToken(Token(TokenType.Shaken));
            c.AddToken(Token(TokenType.MovedThisRound));

            IEnumerable<Token> view = c.GetAllTokens();

            // The mutation a rule would make while the renderer holds the result.
            c.AddToken(Token(TokenType.Fatigued));
            c.RemoveTokens(TokenType.Shaken);

            Assert.That(() => view.ToList(), Throws.Nothing,
                "enumerating after a mutation must not throw - a caller cannot defend itself here, "
                + "because even .ToList() at the call site has to enumerate the live list to copy it");
            // Contents, not just the count: a live view would show [MovedThisRound, Fatigued] here, which
            // happens to be two entries as well.
            Assert.That(view.Select(t => t.Type),
                Is.EquivalentTo(new[] { TokenType.Shaken, TokenType.MovedThisRound }),
                "it reads as of the moment it was taken");
        }

        [Test]
        public void FilteredReads_AreSnapshotsToo()
        {
            var c = new TokenContainer();
            c.AddToken(Token(TokenType.Shaken));
            var owner = new UnitID(Guid.NewGuid());
            c.AddToken(Token(TokenType.MovedThisRound, owner));

            IEnumerable<Token> byType  = c.GetAllTokens(TokenType.Shaken);
            IEnumerable<Token> byOwner = c.TokensWithOwner(owner);

            c.RemoveTokens(TokenType.Shaken);
            c.AddToken(Token(TokenType.Fatigued, owner));

            Assert.That(() => byType.ToList(), Throws.Nothing);
            Assert.That(() => byOwner.ToList(), Throws.Nothing);
            Assert.That(byType.Count(), Is.EqualTo(1));
            Assert.That(byOwner.Count(), Is.EqualTo(1));
        }

        [Test]
        public void ConcurrentReadsAndWrites_DoNotThrow()
        {
            // The crash shape: one thread mutating (a rule firing), one enumerating (the chip renderer).
            var c = new TokenContainer();
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            Exception? failure = null;

            Task writer = Task.Run(() =>
            {
                try
                {
                    while (!cts.IsCancellationRequested)
                    {
                        c.AddToken(Token(TokenType.Shaken));
                        c.AddToken(Token(TokenType.MovedThisRound));
                        c.RemoveTokens(TokenType.Shaken);
                        c.RemoveTokens(TokenType.MovedThisRound);
                    }
                }
                catch (Exception e) { failure ??= e; cts.Cancel(); }
            });

            Task reader = Task.Run(() =>
            {
                try
                {
                    while (!cts.IsCancellationRequested)
                    {
                        foreach (Token t in c.GetAllTokens()) _ = t.Count;
                        foreach (Token t in c.GetAllTokens(TokenType.Shaken)) _ = t.Count;
                        _ = c.HasToken(TokenType.MovedThisRound);
                        _ = c.GetTokenCount(TokenType.Shaken);
                        _ = c.GetTokenMagnitude(TokenType.Shaken);
                    }
                }
                catch (Exception e) { failure ??= e; cts.Cancel(); }
            });

            Task.WaitAll(writer, reader);
            Assert.That(failure, Is.Null, $"reader/writer race: {failure}");
        }

        private static Token Token(TokenType type, UnitID? owner = null) =>
            new Token(type, 1, new TokenClearTrigger.ManualOnly(), null, owner, null);
    }
}
