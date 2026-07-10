using FDG.Data;
using FDG.Rules.Definitions;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Tactician
{
    /// <summary>
    /// The macro-action intent families of Appendix A v2 (confirmed by Chris 2026-07-09).
    /// M11 MoveToCast and M12 DeliverCargo arrive with A3c's second sub-slice (they need the
    /// casting and transport queries); mid-game MoveToEmbark was cut at confirmation.
    /// </summary>
    public enum EMacroIntent
    {
        Hold,               // M1
        AdvanceOnObjective, // M2
        RushObjective,      // M3
        EngageAtRange,      // M4
        ChargeToContact,    // M5
        FallBack,           // M6
        SeekCoverFrom,      // M7
        Block,              // M8
        Escort,             // M9
        Concentrate,        // M10
    }

    /// <summary>M4's range bands. SafeShooting is the kite band: inside our reach, outside theirs.</summary>
    public enum ERangeBand { MaxRange, HalfRange, SafeShooting }

    /// <summary>
    /// Explicit feasibility (Appendix A generator rule): search must never waste rollouts on
    /// infeasible branches, and G3 fallbacks must be attributable.
    /// </summary>
    public enum EFeasibility
    {
        /// <summary>The move reaches the intent's goal this activation.</summary>
        Reachable,
        /// <summary>The move makes progress but the goal is beyond this activation's budget.</summary>
        BudgetClipped,
        /// <summary>No progress possible (no route, or the intent's precondition failed).</summary>
        Blocked,
    }

    /// <summary>
    /// One goal-directed candidate decision for a unit's activation (#191 A3c): an intent, an
    /// ENGINE-VALID move that pursues it (built through the MovementPlanner ladder - plan G3), and
    /// the metadata scoring/search key on. <see cref="Rationale"/> is the G12 explainability line.
    /// </summary>
    public sealed record MacroAction(
        EMacroIntent Intent,
        string Rationale,
        EActionType ActionType,
        List<ModelMoveEntry> Move,
        EFeasibility Feasibility,
        Position ProjectedCentroid,
        IUnit? TargetEnemy = null,
        IObjective? TargetObjective = null,
        IUnit? TargetAlly = null,
        ERangeBand? Band = null);
}
