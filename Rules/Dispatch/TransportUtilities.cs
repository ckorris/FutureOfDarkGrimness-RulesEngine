using FDG.Rules.Foundation;
using FDG.Rules.Tokens;

namespace FDG.Rules.Dispatch;

/// <summary>
/// Pure-logic helpers for the Transport(X) core rule (#035). Transport is a first-class engine
/// subsystem under a thin <see cref="CoreRuleCatalog.Transport"/> marker; this is the foundation layer
/// the stage-level verbs (deploy-time loading, embark/disembark, mid-combat spillout) build on.
///
/// Two design commitments live here (decided with the user — see <c>WorkItems/035-transport.md</c>):
/// <list type="bullet">
///   <item><b>Occupancy is token-derived, not stored.</b> A transport keeps no occupant list; each
///   occupant carries a cross-unit <see cref="TokenType.EmbarkedIn"/> token whose <c>OwnerUnitID</c> is
///   the transport. A transport's load is a query over those tokens, and its capacity is the rule's
///   <c>Arg(0)</c>. So there is no bespoke state field on <c>UnitData</c>, and save/load is free.</item>
///   <item><b>Embarked units are off-table.</b> Occupant models stay at origin (off-battlefield), so the
///   inside↔outside targeting / activation / objective exclusions fall out of the existing
///   <see cref="IUnitExtensions.GetIsOnBattlefield"/> filter for free. Where a rule genuinely needs an
///   embarked unit's position (Caster proximity, #033), <see cref="GetEffectivePosition"/> resolves it
///   to the transport's — an opt-in seam, so generic reads are untouched.</item>
/// </list>
///
/// Methods take an explicit <c>allUnits</c> enumerable rather than an <c>ITableState</c> so the logic
/// is decoupled and unit-testable; the stage callers pass the live unit set.
/// </summary>
public static class TransportUtilities
{
    /// <summary> Canonical name of the Transport core rule (its <see cref="CoreRuleCatalog.Transport"/> identity). </summary>
    public const string TransportRuleName = "Transport";

    /// <summary> A Hero is transportable only up to this Tough value. </summary>
    public const int HeroToughCap = 6;

    /// <summary> A non-Hero is transportable only up to this Tough value. </summary>
    public const int NonHeroToughCap = 3;

    /// <summary> Spaces a multi-wound (Tough ≥ 2) model occupies. </summary>
    public const int LargeModelSpaceCost = 3;

    /// <summary> Spaces a standard (Tough ≤ 1) model occupies. </summary>
    public const int StandardModelSpaceCost = 1;

    // --- Identity & capacity ---------------------------------------------------------------------

    /// <summary>
    /// True if <paramref name="unit"/> can carry friendly units. Asks the rule graph for the CAPABILITY
    /// rather than testing for the Transport rule by identity, so a second rule conferring a hold needs no
    /// change here — see <see cref="CapabilityRuleQueries"/>.
    /// </summary>
    public static bool IsTransport(IUnit unit, RuleEvaluator evaluator) =>
        CapabilityRuleQueries.CanTransport(unit, evaluator);

    /// <summary>
    /// The transport's capacity in spaces. Returns 0 for a unit that is not a transport, so it doubles as
    /// the capability test where a caller wants both.
    /// </summary>
    public static int GetCapacity(IUnit transport, RuleEvaluator evaluator) =>
        CapabilityRuleQueries.TransportCapacity(transport, evaluator);

    // --- Space cost ------------------------------------------------------------------------------

    /// <summary>
    /// Spaces a single model occupies. A Hero (within its Tough(6) ride cap) always occupies 1 — per the
    /// Transport(X) rule's worked example, "Regular models and Heroes with Tough(3) or Tough(6) occupy 1
    /// space" (a unit of 10 regular models with a Tough(3) Hero occupies 11 spaces, not 13). A non-Hero
    /// occupies 1 if Tough ≤ 1, otherwise 3. <paramref name="isHero"/> is the caller's per-model hero
    /// determination; <see cref="GetUnitSpaceCost"/> derives it from the unit via <see cref="IsHeroModel"/>.
    /// </summary>
    public static int GetModelSpaceCost(IModel model, bool isHero) =>
        isHero ? StandardModelSpaceCost
               : (model.TotalWounds <= 1f ? StandardModelSpaceCost : LargeModelSpaceCost);

    /// <summary>
    /// Total spaces a unit occupies — the sum over its living models' space costs, with the unit's hero
    /// (if any) charged the 1-space Hero rate.
    /// </summary>
    public static int GetUnitSpaceCost(IUnit unit) =>
        unit.Models.Where(model => model.GetIsAlive())
            .Sum(model => GetModelSpaceCost(model, IsHeroModel(model, unit)));

    // --- Ride eligibility ------------------------------------------------------------------------

    /// <summary>
    /// Whether a single model is within the Tough cap to ride: Heroes up to <see cref="HeroToughCap"/>,
    /// non-Heroes up to <see cref="NonHeroToughCap"/>. Models above their cap can't be transported.
    /// </summary>
    public static bool CanModelRide(IModel model, bool isHero) =>
        model.TotalWounds <= (isHero ? HeroToughCap : NonHeroToughCap);

    /// <summary>
    /// Full eligibility check for loading <paramref name="candidate"/> into <paramref name="transport"/>:
    /// the transport is a transport, the candidate is friendly (same player), not already embarked, every
    /// model is within its ride cap, and the candidate's space cost fits the transport's remaining
    /// capacity (accounting for units already aboard). <paramref name="reason"/> describes the first
    /// failure when the result is false; empty on success.
    /// </summary>
    public static bool CanUnitEmbark(IUnit candidate, IUnit transport, IEnumerable<IUnit> allUnits,
        RuleEvaluator evaluator, out string reason)
    {
        if (!IsTransport(transport, evaluator))
        {
            reason = $"{transport.Name} is not a transport.";
            return false;
        }

        if (candidate.PlayerID != transport.PlayerID)
        {
            reason = $"{candidate.Name} cannot embark an enemy transport.";
            return false;
        }

        if (IsEmbarked(candidate))
        {
            reason = $"{candidate.Name} is already embarked.";
            return false;
        }

        foreach (IModel model in candidate.Models)
        {
            if (!model.GetIsAlive()) continue;
            if (!CanModelRide(model, IsHeroModel(model, candidate)))
            {
                reason = $"{candidate.Name} has a model too tough to be transported.";
                return false;
            }
        }

        int cost = GetUnitSpaceCost(candidate);
        int remaining = GetRemainingCapacity(transport, allUnits, evaluator);
        if (cost > remaining)
        {
            reason = $"{candidate.Name} needs {cost} space(s) but only {remaining} remain.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    // --- Occupancy (token-derived) ---------------------------------------------------------------

    /// <summary> True if <paramref name="unit"/> is currently embarked (carries an <see cref="TokenType.EmbarkedIn"/> token). </summary>
    public static bool IsEmbarked(IUnit unit) => unit.Tokens.HasToken(TokenType.EmbarkedIn);

    /// <summary> The id of the transport <paramref name="unit"/> is embarked in, or null if it is not embarked. </summary>
    public static UnitID? GetTransportId(IUnit unit) =>
        unit.Tokens.GetAllTokens(TokenType.EmbarkedIn).FirstOrDefault()?.OwnerUnitID;

    /// <summary> The units currently embarked in <paramref name="transport"/> (those whose <see cref="TokenType.EmbarkedIn"/> token is owned by it). </summary>
    public static IEnumerable<IUnit> GetOccupants(IUnit transport, IEnumerable<IUnit> allUnits) =>
        allUnits.Where(unit => GetTransportId(unit) is UnitID id && id == transport.ID);

    /// <summary> Total spaces consumed by everything currently aboard <paramref name="transport"/>. </summary>
    public static int GetOccupiedSpaces(IUnit transport, IEnumerable<IUnit> allUnits) =>
        GetOccupants(transport, allUnits).Sum(GetUnitSpaceCost);

    /// <summary> Spaces still free on <paramref name="transport"/> = capacity − occupied. </summary>
    public static int GetRemainingCapacity(IUnit transport, IEnumerable<IUnit> allUnits,
        RuleEvaluator evaluator) =>
        GetCapacity(transport, evaluator) - GetOccupiedSpaces(transport, allUnits);

    // --- State transitions (token writes) --------------------------------------------------------

    /// <summary>
    /// Loads <paramref name="unit"/> into <paramref name="transport"/> by stamping a cross-unit
    /// <see cref="TokenType.EmbarkedIn"/> token (owner = the transport). Does not move models — embarked
    /// units stay off-table; the caller sets aside / leaves them at origin.
    /// </summary>
    public static void Embark(IUnit unit, IUnit transport) =>
        unit.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.EmbarkedIn, owner: transport.ID));

    /// <summary> Removes <paramref name="unit"/>'s <see cref="TokenType.EmbarkedIn"/> token (disembark / spillout). </summary>
    public static void Disembark(IUnit unit) => unit.Tokens.RemoveTokens(TokenType.EmbarkedIn);

    // --- Destruction spillout & placement range --------------------------------------------------

    /// <summary>
    /// Models placed by a disembark exit or by destruction spillout must stay fully within this range of
    /// the transport ("must stay fully within 6\" of it when exiting" / "placed fully within 6\"").
    /// </summary>
    public const float MaxTransportRangeInches = 6f;

    /// <summary>
    /// Whether a proposed model position is within <see cref="MaxTransportRangeInches"/> of the transport —
    /// the "fully within 6\"" constraint shared by disembark exits and destruction-spillout placement. Pure
    /// geometry; the interactive placement request itself is stage-side.
    /// </summary>
    public static bool IsWithinTransportRange(Position modelPosition, Position transportPosition) =>
        Position.GetDistance2D(modelPosition, transportPosition) <= MaxTransportRangeInches;

    /// <summary>
    /// One occupant model the spillout dangerous-terrain test wounded, returned inside
    /// <see cref="SpilloutRollResult"/> so the stage can present it (a hurt flinch or a death animation
    /// after the batched d6 beat). This is Rules-layer data only — the beats themselves are emitted
    /// stage-side, since the Rules layer holds no presenter.
    /// <paramref name="Died"/> is the model's state after the wound (a 1 that took its last wound).
    /// </summary>
    public readonly record struct SpilloutCasualty(ModelID Model, Position Position, bool Died);

    /// <summary>
    /// The batched spillout dangerous-terrain test: the single roll (one d6 per living occupant model),
    /// how many models tested, total wounds dealt (fractional under the probabilistic roller), and the
    /// wounded models (empty in probabilistic mode, where wounds spread as an expectation). The same
    /// shape as <c>MovementExecutor.DangerousTerrainResult</c>, plus the per-model casualties the
    /// spillout presentation animates.
    /// </summary>
    public readonly struct SpilloutRollResult
    {
        public readonly IDiceResults? Roll;
        public readonly int ModelCount;
        public readonly float Wounds;
        public readonly IReadOnlyList<SpilloutCasualty> Casualties;
        public SpilloutRollResult(IDiceResults? roll, int modelCount, float wounds,
            IReadOnlyList<SpilloutCasualty> casualties)
        {
            Roll = roll; ModelCount = modelCount; Wounds = wounds; Casualties = casualties;
        }
        public bool AnyTested => ModelCount > 0 && Roll != null;
        public static SpilloutRollResult None =>
            new SpilloutRollResult(null, 0, 0f, Array.Empty<SpilloutCasualty>());
    }

    /// <summary>
    /// The deterministic consequences a transport's destruction inflicts on one occupant once it has been
    /// placed: it is no longer embarked (the <see cref="TokenType.EmbarkedIn"/> link is cut), it becomes
    /// Shaken, and every living model takes a dangerous-terrain test (a roll of 1 deals one wound). The
    /// tests all roll together in ONE batch via <paramref name="diceRoller"/> — a single row of dice
    /// instead of a die per model — mirroring <c>MovementExecutor.ApplyDangerousTerrainEffects</c>;
    /// batching is also what lets the probabilistic roller yield the expected number of 1s, which N
    /// separate decisive rolls could not. The placement itself — the owner choosing where, within 6" of
    /// the wreck — is the stage's interactive part and is NOT done here; this is the slice of spillout
    /// that is deterministic given the dice, so it can be unit-tested ahead of the mid-combat
    /// orchestration. Returns the batch so the stage can present it (one dice beat + hurt/death beats);
    /// the return is otherwise inert (callers that only want the state change can ignore it).
    /// </summary>
    public static SpilloutRollResult ApplySpilloutEffects(IUnit occupant, IDiceRoller diceRoller,
        ERandomnessType randomness = ERandomnessType.Realistic)
    {
        Disembark(occupant);

        // Mirrors MoraleUtilities.ApplyShaken (which takes a DataBinding<UnitData> in the stage layer) —
        // inlined to avoid a Rules→Stages dependency. That includes the became-Shaken token clear
        // (#100 #13, Fortified Growth's lose-all-markers) — keep in sync with ClearShakenTriggeredTokens.
        occupant.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.Shaken));
        List<ITokenContainer> shakenClear = new List<ITokenContainer> { occupant.Tokens };
        shakenClear.AddRange(occupant.Models.Select(model => model.Tokens));
        new TokenClearService().ClearForHook(EHookID.Morale_OnShakenApplied, shakenClear);

        List<IModel> testers = occupant.Models.Where(model => model.GetIsAlive()).ToList();
        if (testers.Count == 0) return SpilloutRollResult.None;

        // One batched roll: N d6 at once. Realistic -> N concrete dice; probabilistic -> the N-die
        // distribution (expected 1s).
        IDiceResults roll = diceRoller.Roll(6, testers.Count);
        float ones = roll.At(1); // 1s are wounds (fractional under the probabilistic roller)

        List<SpilloutCasualty> casualties = new List<SpilloutCasualty>();
        float woundsDealt;
        if (randomness == ERandomnessType.Probabilistic)
        {
            // Spread the expected wounds evenly — each model carried its own 1/6 chance of a 1.
            float perModel = ones / testers.Count;
            foreach (IModel model in testers)
                model.DealWounds(perModel);
            woundsDealt = ones;
        }
        else
        {
            // Realistic: a whole number of 1s came up; deal one wound apiece to that many models.
            int wounds = (int)MathF.Round(ones);
            for (int i = 0; i < wounds && i < testers.Count; i++)
            {
                IModel model = testers[i];
                model.DealWounds(1f);
                casualties.Add(new SpilloutCasualty(model.ID, model.Position, model.GetIsDead()));
            }
            woundsDealt = wounds;
        }

        return new SpilloutRollResult(roll, testers.Count, woundsDealt, casualties);
    }

    // --- Effective position (opt-in seam) --------------------------------------------------------

    /// <summary>
    /// The position a rule that cares about an embarked unit's location should use: the transport's
    /// position when <paramref name="unit"/> is embarked, otherwise the unit's own. Null when it can't be
    /// resolved (no living model, or an embarked unit whose transport isn't in <paramref name="allUnits"/>).
    /// The default position reads (targeting, LoS, melee, objectives) do NOT use this — they read raw
    /// positions and see origin for embarked units, which is the intended exclusion. This is the opt-in
    /// seam for the first rule that needs it (Caster, #033); the token already stores the link.
    /// </summary>
    public static Position? GetEffectivePosition(IUnit unit, IEnumerable<IUnit> allUnits)
    {
        if (GetTransportId(unit) is UnitID transportId)
        {
            IUnit? transport = allUnits.FirstOrDefault(u => u.ID == transportId);
            return transport == null ? null : RepresentativePosition(transport);
        }

        return RepresentativePosition(unit);
    }

    /// <summary> A single point standing in for a unit's location: its first living model's position. Null if none. </summary>
    private static Position? RepresentativePosition(IUnit unit)
    {
        foreach (IModel model in unit.Models)
        {
            if (model.GetIsAlive()) return model.Position;
        }
        return null;
    }

    /// <summary>
    /// Whether <paramref name="model"/> is the hero of <paramref name="unit"/> — either the joined hero of
    /// a host unit (#006), or the lone model of a solo Hero unit. Drives the per-model ride cap
    /// (<see cref="HeroToughCap"/> vs <see cref="NonHeroToughCap"/>).
    /// </summary>
    private static bool IsHeroModel(IModel model, IUnit unit)
    {
        if (unit is UnitData host && host.HeroAttachment != null)
        {
            return model.ID == host.HeroAttachment.HeroModelId;
        }

        return unit.Models.Count == 1 && unit.RuleDefinitions.Any(rule => rule.Definition == CoreRuleCatalog.Hero);
    }
}
