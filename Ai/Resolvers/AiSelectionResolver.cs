using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;

namespace FDG.Ai.Resolvers
{
    public class AiSelectionResolver<T> : IStageResolver<SelectionRequest<T>, DataBinding<T>>
    {
        public Task<DataBinding<T>> Resolve(SelectionRequest<T> request)
        {
            // #335: the solo AI never loads a transport. Riding is a PLAN - who gets carried, where they
            // get out, and what they do on arrival - and this AI has none: its cargo rides until the
            // transport dies, which is the same gap the Tactician's A5-5 disembark timing had to be written
            // by hand to cover. Until an AI can make that plan, being carried is worse than walking - and
            // the fallback below was embarking every eligible unit purely because "Embark into X" sorted
            // first. (#191 A5-10, owner's reversal 2026-08-15: the TACTICIAN now answers this prompt itself
            // - it has the drop-off plan - so this decline governs solo, Gunline, and fallback modes only.)
            //
            // Matched on the shared DEPLOY_NORMALLY_CHOICE constant, exactly like AiStringSelectionResolver's
            // Ambush hold decline: same prompt family, same reason. No other cancellable selection carries
            // that label, so a melee-defender pick is untouched - the AI must never cancel one of those,
            // since the stage re-prompts and it would loop.
            if (request.AllowCancel
                && request.CancelLabel == ChooseUnitToDeployStage.DEPLOY_NORMALLY_CHOICE)
            {
                return Task.FromResult<DataBinding<T>>(null!);
            }

            if (request.ValidOptions.Count == 0)
                throw new InvalidOperationException($"AI received a {nameof(SelectionRequest<T>)} with no valid options.");

            return Task.FromResult(request.ValidOptions[0].Option);
        }
    }
}
