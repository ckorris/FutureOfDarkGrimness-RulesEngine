namespace FDG.StageResolution.Previews
{
    /// <summary>One player's preview payload for one slot, exactly as published (#277).</summary>
    public readonly record struct PreviewEntry(PlayerID SourcePlayerID, string Slot,
        string PreviewTypeName, string PreviewJson);

    /// <summary>
    /// Receive side of live decision-preview sharing (#277): the latest preview state of every
    /// OTHER player (local players are filtered out - their previews render live through their own
    /// resolver overlay). Pull-model like the rest of the render path: a drawer polls
    /// <see cref="Version"/> each frame and re-reads <see cref="GetSnapshot"/> only when it moved,
    /// so an idle feed costs one lock per frame and no allocation.
    /// </summary>
    public interface IPreviewFeed
    {
        /// <summary>Monotonic change counter - bumped on every accepted update, clear, or expiry.</summary>
        int Version { get; }

        /// <summary>All current entries, latest-wins per (player, slot). Never null.</summary>
        IReadOnlyList<PreviewEntry> GetSnapshot();
    }
}
