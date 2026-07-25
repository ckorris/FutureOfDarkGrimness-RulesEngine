namespace FDG.StageResolution.Previews
{
    /// <summary>
    /// Publish side of live decision-preview sharing (#277): while a player's stage request is
    /// unresolved, their front end pushes the transient state its resolver is showing (ghost
    /// models, planned paths) so every other player watches the decision take shape instead of a
    /// frozen board. The engine transports the payload opaquely as a (type name, JSON) pair - what
    /// a payload contains and how it draws is entirely the front end's business, so new resolver
    /// visuals need no engine changes.
    ///
    /// <para>
    /// <paramref name="slot"/> keys independently-updatable parts of one player's preview
    /// (latest-wins per (player, slot) on the receive side), letting publishers separate
    /// click-cadence state from the mouse-driven stream instead of resending everything at ~10 Hz.
    /// </para>
    /// </summary>
    public interface IPreviewChannel
    {
        void PublishUpdate(PlayerID sourcePlayerID, string slot, string previewTypeName, string previewJson);

        /// <summary>Drops every slot published under this player - call when their request ends.</summary>
        void PublishClear(PlayerID sourcePlayerID);
    }
}
