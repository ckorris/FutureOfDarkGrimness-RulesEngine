using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Tactician.Resolvers
{
    /// <summary>
    /// #103 cast-assist policy (#191 A5): one token shifts the cast's 4+ threshold one face, so
    /// it buys 1/6 of the spell's expected effect - boosting a friendly cast or denying an enemy
    /// one alike (the solo bot always declines both). The spell is priced from the CASTING unit's
    /// eligible targets, since the already-chosen targets are not in the request (recorded
    /// approximation). Spends only while a token's buy beats its opportunity cost, capped per
    /// request so one cast never drains a whole pool.
    /// </summary>
    public class TacticianCastAssistResolver : IStageResolver<CastAssistRequest, int>
    {
        private readonly ITableState _tableState;
        private readonly RuleEvaluator _evaluator;

        public TacticianCastAssistResolver(ITableState tableState, RuleEvaluator evaluator)
        {
            _tableState = tableState;
            _evaluator = evaluator;
        }

        public Task<int> Resolve(CastAssistRequest request)
        {
            if (request.AvailableTokens <= 0) return Task.FromResult(0);

            ArmyData? army = SpellValuation.ArmyOf(_tableState, request.CastingUnit.GetValue().PlayerID);
            RuntimeSpell? spell = army == null ? null : SpellValuation.FindByName(army, request.SpellName);
            if (spell == null) return Task.FromResult(0); // unknown spell: decline, like solo

            float gross = SpellValuation.GrossCastValue(_evaluator, _tableState, request.CastingUnit, spell);
            float perToken = gross / 6f;
            if (perToken <= TacticianWeights.CastTokenValue) return Task.FromResult(0);

            return Task.FromResult(Math.Min(request.AvailableTokens, TacticianWeights.CastAssistMaxTokens));
        }
    }
}
