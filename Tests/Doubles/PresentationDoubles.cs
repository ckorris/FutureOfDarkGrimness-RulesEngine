using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FDG.Presentation;

namespace FDG.Tests
{
    /// <summary>
    /// Deterministic stand-in for <see cref="IPresentationClock"/>: records every nominal
    /// duration it was asked to wait (in order) and completes immediately, so pacing/ordering
    /// can be asserted without real time or a real <c>Task.Delay</c>.
    /// </summary>
    internal class FakePresentationClock : IPresentationClock
    {
        public float Scale { get; set; } = 1f;

        public readonly List<TimeSpan> Waits = new();

        public TimeSpan TotalNominal
        {
            get
            {
                TimeSpan sum = TimeSpan.Zero;
                foreach (var w in Waits) sum += w;
                return sum;
            }
        }

        public Task Wait(TimeSpan nominalDuration, CancellationToken ct = default)
        {
            Waits.Add(nominalDuration);
            return ct.IsCancellationRequested ? Task.FromCanceled(ct) : Task.CompletedTask;
        }
    }

    /// <summary>Records every beat handed to it, in order.</summary>
    internal class RecordingPresentationSink : IPresentationSink
    {
        public readonly List<PresentationBeat> Beats = new();

        public void OnBeat(PresentationBeat beat) => Beats.Add(beat);
    }

    /// <summary>Minimal concrete beat for contract tests.</summary>
    internal sealed class TestBeat : PresentationBeat
    {
        private readonly TimeSpan _duration;
        private readonly string? _text;

        public TestBeat(TimeSpan duration, string? text = null)
        {
            _duration = duration;
            _text = text;
        }

        public override TimeSpan NominalDuration => _duration;
        public override string? Text => _text;
    }
}
