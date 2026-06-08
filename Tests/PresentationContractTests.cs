using System;
using System.Threading;
using System.Threading.Tasks;
using FDG.Presentation;
using NUnit.Framework;

namespace FDG.Tests
{
    [TestFixture]
    public class PresentationContractTests
    {
        // ---- LocalPresenter: emit-then-pace ----

        [Test]
        public async Task Present_EmitsBeatToSink_ThenWaitsNominalDuration()
        {
            var sink = new RecordingPresentationSink();
            var clock = new FakePresentationClock();
            var presenter = new LocalPresenter(sink, clock);

            var beat = new TestBeat(TimeSpan.FromMilliseconds(250));
            await presenter.Present(beat);

            Assert.That(sink.Beats, Has.Count.EqualTo(1), "beat should reach the sink");
            Assert.That(sink.Beats[0], Is.SameAs(beat));
            Assert.That(clock.Waits, Is.EqualTo(new[] { TimeSpan.FromMilliseconds(250) }),
                "presenter should pace by the beat's nominal duration");
        }

        [Test]
        public async Task Present_MultipleBeats_PreservesOrderToSinkAndClock()
        {
            var sink = new RecordingPresentationSink();
            var clock = new FakePresentationClock();
            var presenter = new LocalPresenter(sink, clock);

            var a = new TestBeat(TimeSpan.FromMilliseconds(100));
            var b = new TestBeat(TimeSpan.FromMilliseconds(200));
            var c = new TestBeat(TimeSpan.FromMilliseconds(300));

            await presenter.Present(a);
            await presenter.Present(b);
            await presenter.Present(c);

            Assert.That(sink.Beats, Is.EqualTo(new[] { a, b, c }));
            Assert.That(clock.Waits, Is.EqualTo(new[]
            {
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromMilliseconds(200),
                TimeSpan.FromMilliseconds(300),
            }));
            Assert.That(clock.TotalNominal, Is.EqualTo(TimeSpan.FromMilliseconds(600)));
        }

        [Test]
        public async Task Present_NullSink_StillPaces()
        {
            var clock = new FakePresentationClock();
            var presenter = new LocalPresenter(sink: null, clock);

            await presenter.Present(new TestBeat(TimeSpan.FromMilliseconds(120)));

            Assert.That(clock.Waits, Is.EqualTo(new[] { TimeSpan.FromMilliseconds(120) }),
                "a missing renderer must not stop the engine from pacing");
        }

        // ---- InstantPresentationClock: headless / test degrade ----

        [Test]
        public void InstantClock_WaitCompletesSynchronously_RegardlessOfDuration()
        {
            var clock = new InstantPresentationClock();

            Task t = clock.Wait(TimeSpan.FromHours(1));

            Assert.That(clock.Scale, Is.EqualTo(0f));
            Assert.That(t.IsCompletedSuccessfully, "instant clock must never actually wait");
        }

        [Test]
        public void InstantClock_AlreadyCancelled_ReturnsCancelled()
        {
            var clock = new InstantPresentationClock();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Task t = clock.Wait(TimeSpan.FromSeconds(1), cts.Token);

            Assert.That(t.IsCanceled);
        }

        // ---- RealtimePresentationClock: scaling edges (no real-time assertions) ----

        [Test]
        public void RealtimeClock_ZeroScale_WaitIsInstant()
        {
            var clock = new RealtimePresentationClock(scale: 0f);

            Task t = clock.Wait(TimeSpan.FromSeconds(5));

            Assert.That(t.IsCompletedSuccessfully);
        }

        [Test]
        public void RealtimeClock_NegativeScale_ClampedToZero()
        {
            var clock = new RealtimePresentationClock(scale: -3f);

            Assert.That(clock.Scale, Is.EqualTo(0f));
            Assert.That(clock.Wait(TimeSpan.FromSeconds(5)).IsCompletedSuccessfully);
        }

        [Test]
        public void RealtimeClock_ZeroDuration_IsInstant_EvenAtRealtimeScale()
        {
            var clock = new RealtimePresentationClock(scale: 1f);

            Assert.That(clock.Wait(TimeSpan.Zero).IsCompletedSuccessfully);
        }

        // ---- PresentationBeat: text projection is opt-in, sibling to logs ----

        [Test]
        public void Beat_TextDefaultsToNull_AndIsCarriedWhenProvided()
        {
            Assert.That(new TestBeat(TimeSpan.Zero).Text, Is.Null);
            Assert.That(new TestBeat(TimeSpan.Zero, "Squad advances.").Text, Is.EqualTo("Squad advances."));
        }
    }
}
