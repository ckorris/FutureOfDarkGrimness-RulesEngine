using System.Collections.Generic;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;

namespace FDG.Rules.Dispatch;

/// <summary>
/// #419: the activation's wound ledger - the REAL wounds each unit has taken so far in the current activation,
/// whoever dealt them. Core Rules v3.6.0, Melee Results: "Units in melee don't take Morale Tests from wounds at
/// the end of an activation. Instead, they compare the number of wounds each unit took during this activation,
/// and the unit with the highest total loses".
///
/// <para><b>Fed</b> at the two seams every landed wound passes through: <c>ApplyWoundsStage</c> (every
/// attacker-caused wound - shooting, melee swings, Impact, Ravage, spell and ability hits, strafing) and
/// <c>CasualtyPresentation.ApplyAndPresent</c> (the batched and self-inflicted ones - Dangerous Terrain,
/// transport spillout, Hazardous, No Retreat, reflect). Only wounds that LAND count: a save or a Regeneration
/// roll that stops a wound stops it here too, and a heal never subtracts (a healed wound was still taken).</para>
///
/// <para><b>Read</b> by the melee result, through <c>CombatActionContext</c>'s pre-melee snapshot: the
/// wounds a combatant had already taken this activation when its melee began (a Dangerous Terrain test on the
/// charge move, a Breath Attack, a spell). The melee's own wounds are still measured by the context's
/// remaining-wounds snapshot, so the exchange itself never depends on a seam having been fed.</para>
///
/// <para>Kept as a <see cref="TokenType.WoundsTakenThisActivation"/> magnitude on the struck unit, like
/// <see cref="CountedWounds"/>: the struck unit is usually not the activated one, and both combatants' tokens
/// are in reach of every stage. <see cref="SweepAll"/> runs table-wide when an activation starts
/// (<c>MainUnitActionStage</c>) and when it ends (<c>ReconcileEndOfActivationStage</c>), so wounds taken
/// between activations (round start/end effects, deployment) never leak into one.</para>
/// </summary>
public static class WoundsTakenThisActivation
{
    /// <summary> Records <paramref name="wounds"/> landed on <paramref name="unit"/> (merging magnitudes). </summary>
    public static void Add(IUnit unit, float wounds)
    {
        if (wounds <= 0f)
        {
            return;
        }

        unit.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.WoundsTakenThisActivation,
            payload: new TokenPayload.Magnitude(wounds)));
    }

    /// <summary> The wounds <paramref name="unit"/> has taken this activation so far. </summary>
    public static float Peek(IUnit unit) => unit.Tokens.GetTokenMagnitude(TokenType.WoundsTakenThisActivation);

    /// <summary> Clears every unit's ledger - an activation is starting or has ended. </summary>
    public static void SweepAll(IEnumerable<IUnit> units)
    {
        foreach (IUnit unit in units)
        {
            int tokens = unit.Tokens.GetTokenCount(TokenType.WoundsTakenThisActivation);
            if (tokens > 0)
            {
                unit.Tokens.RemoveTokens(TokenType.WoundsTakenThisActivation, tokens);
            }
        }
    }
}
