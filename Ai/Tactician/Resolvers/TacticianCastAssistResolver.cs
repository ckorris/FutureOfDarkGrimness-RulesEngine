using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Utilities;

namespace FDG.Ai.Tactician.Resolvers
{
    /// <summary>
    /// #103 cast-assist policy (#191 A5): one token shifts the cast's 4+ threshold one face, so
    /// it buys 1/6 of the spell's expected effect - boosting a friendly cast or denying an enemy
    /// one alike (the solo bot always declines both). The spell is priced from the CASTING unit's
    /// eligible targets, since the already-chosen targets are not in the request (recorded
    /// approximation). Spends only while a token's buy beats its opportunity cost, capped per
    /// request so one cast never drains a whole pool - and (#442) never past the clamp a token can still move.
    /// </summary>
    public class TacticianCastAssistResolver : IStageResolver<CastAssistRequest, int>
    {
        private readonly ITableState _tableState;
        private readonly RuleEvaluator _evaluator;
        // #384: the game's see-through-allies setting, for the spell pricing's sight tests.
        private readonly bool _seeThroughFriendlyUnits;

        public TacticianCastAssistResolver(ITableState tableState, RuleEvaluator evaluator,
            bool seeThroughFriendlyUnits = false)
        {
            _tableState = tableState;
            _evaluator = evaluator;
            _seeThroughFriendlyUnits = seeThroughFriendlyUnits;
        }

        public Task<int> Resolve(CastAssistRequest request)
        {
            if (request.AvailableTokens <= 0) return Task.FromResult(0);

            ArmyData? army = SpellValuation.ArmyOf(_tableState, request.CastingUnit.GetValue().PlayerID);
            RuntimeSpell? spell = army == null ? null : SpellValuation.FindByName(army, request.SpellName);
            if (spell == null) return Task.FromResult(0); // unknown spell: decline, like solo

            float gross = SpellValuation.GrossCastValue(_evaluator, _tableState, request.CastingUnit, spell,
                _seeThroughFriendlyUnits);
            float perToken = gross / 6f;
            if (perToken <= TacticianWeights.CastTokenValue) return Task.FromResult(0);

            // #442 (#429): a token only buys a face while the roll is inside the engine's 2-6 clamp - past the 2+
            // floor (a boost) or the 6+ ceiling (a hinder) it buys nothing. Measured from the caster's own threshold
            // (its passive bonus and granted tokens; the self-boost and other assists are not known here).
            int threshold = SpellValuation.CastThreshold(_evaluator, request.CastingUnit.GetValue());
            int useful = request.IsFriendly
                ? threshold - DiceUtilities.MINIMUM_SUCCESS_ROLL
                : DiceUtilities.MAXIMUM_SUCCESS_ROLL - threshold;
            if (useful <= 0) return Task.FromResult(0);

            return Task.FromResult(Math.Min(useful,
                Math.Min(request.AvailableTokens, TacticianWeights.CastAssistMaxTokens)));
        }
    }
}
