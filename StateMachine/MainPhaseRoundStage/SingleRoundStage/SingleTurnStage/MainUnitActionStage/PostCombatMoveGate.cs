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

        public static async Task OfferIfAvailable(IGameContext gameContext, IUnit unit,
            IReadOnlyList<RuleOperation> operations)
        {
            // No post-combat-move rule on this unit → nothing produced → nothing to gate.
            if (operations.Count == 0)
            {
                return;
            }

            // Budget already spent this round → don't even offer the move again.
            if (unit.Tokens.HasToken(TokenType.PostCombatMoveUsed))
            {
                return;
            }

            List<Position> before = SnapshotPositions(unit);

            // Any token ops a future post-combat rule emits are applied separately; OperationExecutor
            // runs the imperative move (which raises the optional movement request to the unit's owner).
            OperationApplier.ApplyTokenOperations(operations);
            await OperationExecutor.Execute(operations, new GameOperationServices(gameContext));

            if (Moved(unit, before))
            {
                unit.Tokens.AddToken(new Token(TokenType.PostCombatMoveUsed, 1, new TokenClearTrigger.RoundEnd()));
                gameContext.Log($"{unit.Name} made its post-combat move — spent for this round.");
            }
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
