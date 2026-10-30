using System.Collections.Generic;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;

namespace FDG.Rules.Dispatch;

/// <summary>
/// #446: the "one Dangerous Terrain Test per activation" marker. Core Rules v3.6.0, Dangerous Terrain: "Units
/// with models moving across Dangerous terrain, or that activate in it, must roll as many dice as the number of
/// wounds it would take to fully destroy them, and for each roll of 1 the unit takes one wound." The two
/// triggers name one test, so a unit that tested because it ACTIVATED in Dangerous terrain does not test again
/// when it then moves out across it (and a unit that already tested on a move does not test on a second move in
/// the same activation). Ruled in WorkItems/446 D2 (owner review).
///
/// <para><b>Set</b> by the two trigger seams when they actually roll: <c>ActivatedInDangerousTerrainStage</c>
/// (activating in it) and <c>MovementExecutor.RollDangerousTerrain</c> (a move across it, normal or
/// out-of-band). <b>Read</b> by the same two seams, which skip the test when it is set. The forced tests - the
/// Dangerous Terrain Debuff's "must immediately take a Dangerous Terrain test" and a destroyed transport's
/// spillout - are separate rules that demand a test outright: they neither read nor set it.</para>
///
/// <para>Kept as a <see cref="TokenType.DangerousTerrainTestedThisActivation"/> flag on the unit and swept
/// table-wide when an activation starts (<c>MainUnitActionStage</c>) and when it ends
/// (<c>ReconcileEndOfActivationStage</c>) - the <see cref="WoundsTakenThisActivation"/> shape - so a unit moved
/// during ANOTHER unit's activation (a post-melee Harassing move) carries it only until that activation ends,
/// and never into its own.</para>
/// </summary>
public static class DangerousTerrainTestedThisActivation
{
    /// <summary> Whether <paramref name="unit"/> has already taken its test in the current activation. </summary>
    public static bool Has(IUnit unit) => unit.Tokens.HasToken(TokenType.DangerousTerrainTestedThisActivation);

    /// <summary> Records that <paramref name="unit"/> has taken its test this activation (idempotent). </summary>
    public static void Mark(IUnit unit)
    {
        if (Has(unit))
        {
            return;
        }

        unit.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.DangerousTerrainTestedThisActivation));
    }

    /// <summary> Clears every unit's marker - an activation is starting or has ended. </summary>
    public static void SweepAll(IEnumerable<IUnit> units)
    {
        foreach (IUnit unit in units)
        {
            int tokens = unit.Tokens.GetTokenCount(TokenType.DangerousTerrainTestedThisActivation);
            if (tokens > 0)
            {
                unit.Tokens.RemoveTokens(TokenType.DangerousTerrainTestedThisActivation, tokens);
            }
        }
    }
}
