using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;

namespace FDG.Stages
{
    /// <summary>
    /// Enforces the "once per round" limit on the post-combat-move family (Harassing / Hit & Run /
    /// Guerrilla and the Shooter/Fighter variants). Those rules fire on EVERY shoot action and EVERY
    /// resolved melee, but the corpus grants the move only once per round — so both
    /// <see cref="PostShootStage"/> and <see cref="PostMeleeStage"/> route their hook operations through
    /// here instead of running them directly.
    ///
    /// The budget is one shared <see cref="TokenType.PostCombatMoveUsed"/> marker per unit (RoundEnd
    /// clear, swept by the round-end pass like Fatigue) — so a unit moves at most once after combat per
    /// round, whether the trigger was shooting or melee. The marker is set only when the unit ACTUALLY
    /// repositions: the move is optional, and a player who declines (submits a zero move) keeps the
    /// round's budget. Movement is detected by comparing model positions before/after — self-contained
    /// here, so it needs no plumbing through the operation-execution seam.
    /// </summary>
    internal static class PostCombatMoveGate
    {
        private const float MovedEpsilonInches = 0.001f;

        // Returns whether the unit ACTUALLY repositioned - the same fact the marker below records. #381:
        // the callers hand this to RetreatingStrikePostCombatStage (via ICombatActionContext.
        // PostCombatMover) so the move-end strike hook fires only after a real post-combat move.
        public static async Task<bool> OfferIfAvailable(IGameContext gameContext, IUnit unit,
            IReadOnlyList<RuleOperation> operations)
        {
            // Coalesce the family's move ops into ONE move at the largest budget. A unit that produces
            // several post-combat moves at this hook — its base rule's 3" plus a Boost's 6", or two stacked
            // family rules — moves ONCE, at the max; "Boost moves 6" instead of 3"" falls out of the max().
            // Without this each op would raise its own movement request.
            List<RuleOperation.InvokeTriggeredMove> moves =
                operations.OfType<RuleOperation.InvokeTriggeredMove>().ToList();

            // No post-combat move produced (no family rule, or only token ops) → apply any token ops, stop.
            if (moves.Count == 0)
            {
                OperationApplier.ApplyTokenOperations(operations);
                return false;
            }

            // #391: Shaken units must remain idle - no Active Special Rules, and the rulebook calls out
            // repositioning rules like Harassing explicitly. Gated here, the family's one chokepoint, so
            // the base rules, the Boosts and any supplement variant are all covered without a def-side
            // condition on each. Bites exactly when a unit becomes Shaken from LOSING the melee it would
            // move after (morale resolves before this window). Budget kept - the unit did not move.
            if (unit.Tokens.HasToken(TokenType.Shaken))
            {
                OperationApplier.ApplyTokenOperations(operations);
                gameContext.LogDebug($"{unit.Name} is Shaken - post-combat move not offered.");
                return false;
            }

            // Budget already spent this round → don't even offer the move again. Any token ops sharing
            // the hook still apply — a non-move rule's markers must not vanish just because the move
            // budget is gone (ApplyTokenOperations ignores the move ops themselves, so nothing double-fires).
            if (unit.Tokens.HasToken(TokenType.PostCombatMoveUsed))
            {
                OperationApplier.ApplyTokenOperations(operations);
                return false;
            }

            RuleOperation.InvokeTriggeredMove bestMove = moves
                .OrderByDescending(move => move.MaxInches).First();

            List<Position> before = SnapshotPositions(unit);

            // Any token ops a future post-combat rule emits are applied separately; OperationExecutor runs
            // the single chosen move (which raises the optional movement request to the unit's owner).
            OperationApplier.ApplyTokenOperations(operations);
            await OperationExecutor.Execute(new RuleOperation[] { bestMove }, new GameOperationServices(gameContext));

            if (Moved(unit, before))
            {
                unit.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.PostCombatMoveUsed));
                gameContext.Log($"{unit.Name} made its post-combat move - spent for this round.");
                return true;
            }

            return false;
        }

        private static List<Position> SnapshotPositions(IUnit unit)
        {
            return unit.Models.Select(model => model.Position).ToList();
        }

        // True if any model's horizontal position changed beyond the float-noise epsilon — i.e. the
        // player took the optional move rather than declining it.
        private static bool Moved(IUnit unit, List<Position> before)
        {
            List<IModel> models = unit.Models;
            for (int i = 0; i < models.Count && i < before.Count; i++)
            {
                Position now = models[i].Position;
                if (Math.Abs(now.x - before[i].x) > MovedEpsilonInches
                    || Math.Abs(now.z - before[i].z) > MovedEpsilonInches)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
