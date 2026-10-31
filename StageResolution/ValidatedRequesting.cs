namespace FDG.StageResolution
{
    /// <summary>
    /// #458: issues a request whose reply the engine validates, and survives a reply that fails validation
    /// instead of ending the game. The client-side gates (GUI Done buttons, CLI prompts, AI planners) are
    /// meant to submit only legal replies, but they are separate code: when one drifts from the
    /// authoritative check the stage used to throw <see cref="RequestResponseInvalidException"/>, which
    /// ends the game for everyone (a v0.3.0 consolidation did exactly that).
    ///
    /// Instead: log why the reply was rejected and ask again with a fresh request, up to
    /// <see cref="MaxAttempts"/> times. A deterministic resolver (the AI, an EOF-driven CLI) will just
    /// repeat itself, so the attempts are bounded; after the last one the caller's
    /// <paramref name="fallback"/> is used. Callers pass a fallback that is a no-op for the game state
    /// (the unit holds where it stands), so the fallback can never itself be an illegal move.
    /// </summary>
    public static class ValidatedRequesting
    {
        public const int MaxAttempts = 3;

        /// <param name="buildRequest">Builds a fresh request per attempt (each carries its own TaskID).</param>
        /// <param name="whyInvalid">Null when the reply is acceptable; otherwise the reasons, ASCII. Never
        /// called with a null reply (that is rejected up front).</param>
        /// <param name="fallback">The reply to use once every attempt has been rejected.</param>
        /// <param name="subject">Who is deciding, for the log ("Great Tusk-Beast Guard").</param>
        /// <param name="fallbackDescription">What the fallback does, for the log ("stays where it is").</param>
        public static async Task<TReply> RequestUntilValid<TRequest, TReply>(IGameContext gameContext,
            Func<TRequest> buildRequest, Func<TReply, string?> whyInvalid, Func<TReply> fallback,
            string subject, string fallbackDescription)
            where TRequest : IStageTaskRequest<TReply>
        {
            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                TReply reply = await gameContext.PlayerRequester.RequestDecision<TRequest, TReply>(buildRequest());

                string? reasons = reply is null ? "no answer was given" : whyInvalid(reply);
                if (reasons == null) return reply;

                gameContext.Log(attempt < MaxAttempts
                    ? $"{subject}: that move is not allowed ({reasons}). Choose again."
                    : $"{subject}: that move is not allowed ({reasons}). {subject} {fallbackDescription}.");
            }

            return fallback();
        }
    }
}
