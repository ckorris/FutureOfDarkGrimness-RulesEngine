namespace FDG.Presentation.Messages
{
    /// <summary>
    /// Carries one presentation beat from the host to a client. The beat is polymorphic, so the
    /// bus serializer must preserve its concrete type (<c>TypeNameHandling.Auto</c>), as with the
    /// other bus messages.
    /// </summary>
    public record PresentBeatMessage(PresentationBeat Beat);
}
