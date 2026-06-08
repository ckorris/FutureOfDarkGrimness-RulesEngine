using System;

namespace FDG.Presentation.Beats
{
    /// <summary>
    /// A big flashing on-screen announcement (e.g. "Round 3", "Deployment", a winner). Emitted
    /// manually via <c>context.Announce(...)</c> — never automatically per stage — which also writes
    /// the same text to the regular log. The front-end flashes the text in the forefront.
    /// </summary>
    [Serializable]
    public sealed class BannerBeat : PresentationBeat
    {
        public string BannerText { get; }
        public TextColor Color { get; }

        public BannerBeat(string bannerText, TextColor color)
        {
            BannerText = bannerText;
            Color = color;
        }

        public override TimeSpan NominalDuration => PresentationDurations.Banner;

        public override string? Text => BannerText;
    }
}
