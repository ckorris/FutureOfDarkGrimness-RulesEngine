using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;

namespace FDG.Rules.Dispatch;

/// <summary>
/// #166a — the "every rule must fire" lint. For one <see cref="SpecialRuleDefinition"/>, proves that
/// each passive <see cref="HookEntry"/> can actually produce at least one <see cref="RuleOperation"/>
/// in some synthesizable game situation, and that each <see cref="ActivatedAbility"/> is offered at a
/// hook a production stage really polls and resolves to at least one operation that stage really
/// executes. Returns human-readable problems; empty means the rule provably does something.
///
/// This is the automated form of the Breath Attack lesson (SpecialRulesAudit BUG-1): a rule can pass
/// validation, registration, and serialization tests while being a complete no-op in play. The lint
/// closes that class for every data-driven rule at zero marginal cost — run it over
/// <see cref="CoreRuleCatalog.All"/> and over each rule supplement.
///
/// How it works, and what it deliberately does not do:
/// <list type="bullet">
///   <item>Passive entries are checked by DIRECT invocation — build the entry's hook context (a small
///         set of variants covering the capability conditions: near/far, moved, melee, charging, all
///         die faces), evaluate its <see cref="Condition"/>, apply its <see cref="Effect"/>, and
///         require an operation. Dispatch itself (<see cref="RuleEvaluator"/> seat/hook matching,
///         dedup, suppression) is generic machinery covered by the integration suite; going through it
///         here would only blur attribution when helper rules share a hook.</item>
///   <item>Composition-gated conditions are pre-satisfied from the condition tree itself:
///         <see cref="Condition.UnitHasRule"/>/<see cref="Condition.TargetHasRule"/> attach a stub
///         rule of that name, <see cref="Condition.TokenPresent"/> seeds the tokens. Only
///         positive-polarity leaves are satisfied — under <see cref="Condition.Not"/> the default
///         empty state already satisfies the leaf.</item>
///   <item>Activated abilities go through the real <see cref="RuleEvaluator.GatherOffers"/> (so an
///         ability a stage could never offer is caught), and their effect operations are checked
///         against <see cref="IsOpHandledAtAbilityHook"/> — the hand-maintained map of what the five
///         offering stages actually execute. If a stage learns a new operation type, update the map;
///         the drift direction is a loud false failure, never a silent false pass.</item>
///   <item>NOT covered: whether a PASSIVE entry's operations are consumed by the stages at its hook
///         (the sink/query wiring), and whether the rule's numbers match the rulebook. Deferred —
///         see WorkItems/166.</item>
/// </list>
/// </summary>
public static class RuleFireLint
{
    private const float FarInches = 1000f;
    private const float NearInches = 0.5f;

    // #197 misc (Grounded family): a cover piece over the lint bearers' origin (their models sit at
    // Position()), so the terrain-proximity condition ("most models within 1in of terrain") has a hit
    // context that satisfies it and the Grounded rules prove fireable.
    private static readonly IReadOnlyList<ITerrain> OriginTerrain =
        new ITerrain[] { new TerrainData(ETerrainType.Cover, new CircularZone(new Float2(0f, 0f), 5f)) };

    // #197 (P15): the hit/save context variants cover every Unpredictable branch so both arms of an
    // Unpredictable rule (HitBonus at hook 72, ApBonus at hook 73) find a context that satisfies them.
    private static readonly EUnpredictableBranch[] UnpredictableBranches =
        { EUnpredictableBranch.None, EUnpredictableBranch.HitBonus, EUnpredictableBranch.ApBonus };
    private const int GenericArgumentValue = 3;
    private const int LintModelsPerUnit = 3;

    private static readonly HookContextCatalog ContextCatalog = new();
    private static readonly RuleValidator Validator = new(ContextCatalog);
    private static readonly IDiceRoller Dice = new ProbabilisticDiceRoller();

    /// <summary>
    /// Lints one rule definition. Empty result = every passive entry fired and every activated
    /// ability was offered and produced stage-executable operations. Rules that legitimately fail
    /// (engine-marker rules like Hero/Transport, stage-enacted marker effects like Disembark) belong
    /// on the calling test's explicit allowlist, with a reason — the allowlist IS the documented
    /// not-covered ledger.
    /// </summary>
    public static IReadOnlyList<string> Check(SpecialRuleDefinition definition)
    {
        var problems = new List<string>();

        foreach (RuleViolation violation in Validator.Validate(definition))
        {
            problems.Add($"at {violation.Hook}: {violation.Describe()}.");
        }

        if (definition.Passive.Count == 0 && definition.Activated.Count == 0)
        {
            problems.Add("defines no passive entries and no activated abilities - if engine code " +
                "consumes it as a marker, allowlist it with a pointer to that code.");
        }

        for (int i = 0; i < definition.Passive.Count; i++)
        {
            CheckPassiveEntry(definition, i, definition.Passive[i], problems);
        }

        for (int i = 0; i < definition.Activated.Count; i++)
        {
            CheckAbility(definition, i, definition.Activated[i], problems);
        }

        return problems;
    }

    private static void CheckPassiveEntry(SpecialRuleDefinition definition, int index, HookEntry entry,
        List<string> problems)
    {
        string label = $"passive entry {index} at {entry.HookID} ({entry.Seat})";

        if (!ContextCatalog.TryGetContextType(entry.HookID, out _))
        {
            problems.Add($"{label}: no context type exists for this hook, so no engine stage can " +
                "ever fire it.");
            return;
        }

        LintWorld world = LintWorld.Build(definition);
        IReadOnlyList<RuleArgument> args = ArgumentsFor(definition);
        SatisfyCondition(entry.Condition, world, positive: true);

        bool conditionEverPassed = false;
        string? applyError = null;

        foreach (IHookContext context in ContextVariants(entry.HookID, entry.Seat, world))
        {
            var invocation = new RuleInvocation(context, world.Bearer, args,
                DiceRoller: Dice, Weapon: world.Weapon, Definition: definition);

            bool passed;
            try
            {
                passed = entry.Condition.Evaluate(invocation);
            }
            catch (Exception e)
            {
                applyError = $"condition threw {e.GetType().Name}: {e.Message}";
                continue;
            }

            if (!passed)
            {
                continue;
            }

            conditionEverPassed = true;

            var produced = new List<RuleOperation>();
            try
            {
                entry.Effect.Apply(invocation, produced);
            }
            catch (Exception e)
            {
                applyError = $"effect threw {e.GetType().Name}: {e.Message}";
                continue;
            }

            if (produced.Count > 0)
            {
                // Producing an operation is not the same as having one READ. A rollModifier(Hit) emitted at
                // Shooting_OnHitRollComplete is discarded — the dice are already rolled, and only Save deltas
                // fold from that hook. Two shipped rules did exactly that and did nothing in play (#197).
                List<RuleOperation> ignored = produced
                    .Where(op => !IsOpConsumedAtPassiveHook(entry.HookID, op))
                    .ToList();

                if (ignored.Count == produced.Count)
                {
                    problems.Add($"{label}: fires, but no stage at this hook reads what it produces " +
                        $"[{string.Join(", ", ignored.Select(DescribeOp))}] - the entry is a no-op in play. " +
                        "Either move it to the hook that consumes the operation, or extend " +
                        "IsOpConsumedAtPassiveHook if a stage has learned to read it.");
                }

                return; // fired
            }
        }

        if (applyError != null)
        {
            problems.Add($"{label}: {applyError}");
        }
        else if (!conditionEverPassed)
        {
            problems.Add($"{label}: condition ({Describe(entry.Condition)}) never passed in any " +
                "synthesized context - the entry cannot fire in the situations this hook covers.");
        }
        else
        {
            problems.Add($"{label}: condition passed but the effect ({entry.Effect.GetType().Name}) " +
                "produced no operations - a no-op entry.");
        }
    }

    private static void CheckAbility(SpecialRuleDefinition definition, int index, ActivatedAbility ability,
        List<string> problems)
    {
        string label = $"activated ability {index} at {ability.TriggerHook}";

        if (!AbilityOfferingHooks.Contains(ability.TriggerHook))
        {
            problems.Add($"{label}: no engine stage gathers ability offers at this hook (offering " +
                $"hooks: {string.Join(", ", AbilityOfferingHooks)}) - the ability can never be used.");
            return;
        }

        LintWorld world = LintWorld.Build(definition);
        world.Bearer.AttachRuleDefinition(
            new ResolvedRule(definition.Name, definition, ArgumentsFor(definition)));
        SeedCost(ability.Cost, world.Bearer);
        SatisfyCondition(ability.AvailableWhen, world, positive: true);

        var evaluator = new RuleEvaluator(Dice);
        bool offered = ContextVariants(ability.TriggerHook, ERuleSeat.Actor, world)
            .Any(context => evaluator.GatherOffers(context).Any(o => ReferenceEquals(o.Ability, ability)));

        if (!offered)
        {
            problems.Add($"{label}: never offered by GatherOffers in any synthesized context " +
                "(acting unit missing from the context, unaffordable cost, or an availability " +
                "condition the lint cannot satisfy).");
            return;
        }

        // The effect ops only (mirrors RuleEvaluator.ResolveAbility's per-target apply) — cost ops
        // are always applier-handled and would mask a no-op effect.
        var effectOps = new List<RuleOperation>();
        try
        {
            ability.Effect.Apply(new RuleInvocation(Hook: null, world.Bearer,
                ArgumentsFor(definition), Target: world.Other, DiceRoller: Dice), effectOps);
        }
        catch (Exception e)
        {
            problems.Add($"{label}: effect threw {e.GetType().Name}: {e.Message}");
            return;
        }

        if (effectOps.Count == 0)
        {
            problems.Add($"{label}: effect ({ability.Effect.GetType().Name}) produced no operations - " +
                "if a dedicated stage enacts it, allowlist the rule with a pointer to that stage.");
        }
        else if (!effectOps.Any(op => IsOpHandledAtAbilityHook(ability.TriggerHook, op)))
        {
            problems.Add($"{label}: none of the produced operations " +
                $"[{string.Join(", ", effectOps.Select(o => o.GetType().Name))}] are executed by the " +
                "stage that offers abilities at this hook - they would be silently dropped " +
                "(the Breath Attack failure mode).");
        }
    }

    /// <summary>
    /// The hooks whose stages call <see cref="RuleEvaluator.GatherOffers"/>. An ability authored at any
    /// other hook is dead data. Sources: ChooseActionStage, PreAttackStage, StrafingStage,
    /// DeterminePlayerTurnStage, DeployUnitStage, ActivationStartStage — update BOTH this set and
    /// <see cref="IsOpHandledAtAbilityHook"/> when a stage gains an offer site.
    /// </summary>
    private static readonly IReadOnlyList<EHookID> AbilityOfferingHooks = new[]
    {
        EHookID.Activation_OnActivationStart,
        EHookID.Activation_OnActionChoice,
        EHookID.Activation_OnPreAttack,
        EHookID.Activation_OnNextActivatorRequested,
        EHookID.Movement_OnMoveThroughEnemy,
        EHookID.Deployment_OnUnitDeployed,
    };

    /// <summary>
    /// Whether the stage offering abilities at <paramref name="hook"/> actually executes
    /// <paramref name="op"/>. Every offering stage runs <see cref="OperationApplier"/> (token grants /
    /// consumes / heal) and <see cref="OperationExecutor"/> (any <see cref="ExecutableOperation"/>);
    /// <see cref="RuleOperation.InvokeDealHits"/> only resolves on the pre-attack and strafing child
    /// pipelines, and <see cref="RuleOperation.InvokeReactivate"/> only in DeterminePlayerTurnStage.
    /// </summary>
    /// <summary>
    /// Whether the stage that fires <paramref name="hook"/> actually READS <paramref name="op"/>. The
    /// passive-side twin of <see cref="IsOpHandledAtAbilityHook"/>, and the same kind of hand-maintained map:
    /// when a stage learns to consume a new operation, extend this. The drift direction is a loud false
    /// failure, never a silent false pass — an unmapped (hook, op) pair reports as unconsumed.
    ///
    /// <para>This closes the gap that let <c>Changebound</c> and <c>Machine-Fog</c> ship as complete no-ops
    /// (#197): both emitted a <see cref="RuleOperation.ApplyRollModifier"/> for <see cref="ERollKind.Hit"/> at
    /// <see cref="EHookID.Shooting_OnHitRollComplete"/>, where the hit dice have already been rolled and only
    /// <see cref="ERollKind.Save"/> deltas fold onward. Both the validator and the rest of this lint passed
    /// them: the condition was well-formed and the effect produced an operation. Nothing checked that anyone
    /// was listening. Note the map keys on the operation's <i>payload</i> where that decides consumption
    /// (roll kind), not just its type.</para>
    ///
    /// Deliberately NOT covered, and still on WorkItems/166: whether the consumed value is used *correctly*
    /// (a Save delta folded with the wrong sign is invisible here), and whether several rules' operations
    /// compose to the right total — <c>FdgRaylib.Tests/BoostRuleCompositionTests</c> covers that for the
    /// rules it names.
    /// </summary>
    private static bool IsOpConsumedAtPassiveHook(EHookID hook, RuleOperation op)
    {
        // Suppression (Effect.IgnoreRule -> SuppressRule) is resolved by the evaluator's own first pass, not
        // by any stage, so it is read wherever it is emitted.
        if (op is RuleOperation.SuppressRule)
        {
            return true;
        }

        return hook switch
        {
            // CapabilityRuleQueries: the capability question. These ops are not applied by anything - their
            // presence in the queue IS the answer - so they are "consumed" wherever they are emitted. Any
            // op that is NOT a capability answer is still a no-op here, which is the point: this hook is a
            // question, and emitting (say) a token grant in reply to it does nothing.
            EHookID.Lifecycle_OnCapabilityQuery => op is RuleOperation.EnableCasting
                or RuleOperation.EnableTransport or RuleOperation.EnableReDeployment
                or RuleOperation.EnableSpellLending or RuleOperation.EnableSpellRelay,

            // DetermineHitRollStage: shifts the hit threshold and floors Quality. It never reads a Save delta.
            EHookID.Shooting_OnHitRollModifier =>
                op is RuleOperation.ApplyRollModifier { Roll: ERollKind.Hit } or RuleOperation.QualityFloor,

            // RollToHitStage: per-hit AP split, extra hits, hit multiplier, whole-attack Save delta, and the
            // defender's AP reduction. The hit roll is already made, so a Hit delta here is discarded.
            EHookID.Shooting_OnHitRollComplete =>
                op is RuleOperation.ApplyRollModifier { Roll: ERollKind.Save }
                    or RuleOperation.InsertExtraHits
                    or RuleOperation.MultiplyHits
                    or RuleOperation.ApplyPerHitSaveModifier
                    or RuleOperation.ReduceArmorPenetration,

            // SightRuleQueries reads the attacker's weapon-sight flags off CoverIgnoreContext.
            EHookID.Shooting_OnSaveRollModifier =>
                op is RuleOperation.IgnoreCover or RuleOperation.IgnoreLineOfSight,

            // AssignWoundsStage: the save reroll, injected wounds, and the wound-ignore threshold.
            EHookID.Shooting_OnSaveRollComplete =>
                op is RuleOperation.ApplyReroll { Roll: ERollKind.Save }
                    or RuleOperation.InsertExtraWounds
                    or RuleOperation.IgnoreWound,

            EHookID.Shooting_OnPreApplyWound => op is RuleOperation.MultiplyWounds,
            EHookID.Shooting_OnRangeCheck => op is RuleOperation.ApplyRangeModifier,
            EHookID.Shooting_OnShootTargetsSelected => op is RuleOperation.TargetIndividualModel,
            EHookID.Shooting_OnPostShoot => op is RuleOperation.InvokeTriggeredMove,

            // UnitDestructionNotifier applies token ops and runs executables at the death choke point.
            EHookID.Shooting_OnUnitDestroyed => IsTokenOrExecutable(op),

            EHookID.Movement_OnMoveActionDeclared or EHookID.Movement_OnChargeDeclared =>
                op is RuleOperation.ApplyMovementBonus,
            EHookID.Movement_OnMoveThroughTerrain =>
                op is RuleOperation.IgnoreTerrainEffects or RuleOperation.CountAsInTerrain,
            EHookID.Movement_OnMoveThroughEnemy => op is RuleOperation.IgnoreEnemyMovementBlock,

            EHookID.Morale_OnPreMoraleTest => op is RuleOperation.ApplyRollModifier { Roll: ERollKind.Morale },
            EHookID.Morale_OnMoraleTestComplete => op is RuleOperation.ApplyReroll { Roll: ERollKind.Morale },

            // ReduceImpactDicePerModel folds into the same ChargeImpactHits sink, as a negative dice count.
            // #197 P10: Ravage queues InvokeDealAutoWounds here, rolled by ResolveRavageWoundsStage.
            EHookID.Melee_OnChargeContact =>
                op is RuleOperation.ChargeImpactHits or RuleOperation.InvokeDealAutoWounds,
            EHookID.Melee_OnCounterTrigger => op is RuleOperation.StrikeFirst,
            EHookID.Melee_OnMeleeResolution => op is RuleOperation.ExtraMeleeWoundCount,
            EHookID.Melee_OnPostMelee => op is RuleOperation.InvokeTriggeredMove,

            EHookID.Activation_OnActionChoice => op is RuleOperation.RestrictActions,

            // UnitCreationRules applies the token grants (auras) and folds SetMaxWounds (Tough).
            EHookID.Lifecycle_OnUnitCreated =>
                op is RuleOperation.SetMaxWounds || IsTokenOrExecutable(op),

            EHookID.Deployment_OnPreDeploymentSelect => op is RuleOperation.DeferDeployment,

            // StartOfRoundExtraActionStage applies token ops and runs executables for every living unit.
            EHookID.Round_OnRoundStart => IsTokenOrExecutable(op),

            // ReconcileObjectivesStage fires round-end rules for every living unit before the token
            // sweep (#100 #13, Fortified Growth's end-of-round marker).
            EHookID.Round_OnRoundEnd => IsTokenOrExecutable(op),

            // ActivationStartStage applies token ops, runs executables, and folds RepositionModels into one
            // placement. (Before #197's reposition slice nothing evaluated passive entries here at all, which
            // made this arm a false pass — the map has to track what a stage really does, not what it could.)
            EHookID.Activation_OnActivationStart =>
                op is RuleOperation.RepositionModels || IsTokenOrExecutable(op),

            // Activation_OnEndOfActivation carries token lifecycle only.
            EHookID.Activation_OnEndOfActivation => IsTokenOrExecutable(op),

            _ => false,
        };
    }

    private static bool IsOpTokenWork(RuleOperation op) =>
        op is RuleOperation.GrantTokenToUnit or RuleOperation.GrantTokenToModel
            or RuleOperation.ConsumeTokensFromUnit or RuleOperation.ConsumeTokensFromModel
            or RuleOperation.InvokeHeal;

    private static bool IsTokenOrExecutable(RuleOperation op) => IsOpTokenWork(op) || op is ExecutableOperation;

    private static string DescribeOp(RuleOperation op) => op switch
    {
        RuleOperation.ApplyRollModifier m => $"{nameof(RuleOperation.ApplyRollModifier)}({m.Roll})",
        RuleOperation.ApplyReroll r => $"{nameof(RuleOperation.ApplyReroll)}({r.Roll})",
        _ => op.GetType().Name,
    };

    private static bool IsOpHandledAtAbilityHook(EHookID hook, RuleOperation op) => op switch
    {
        RuleOperation.GrantTokenToUnit or RuleOperation.GrantTokenToModel
            or RuleOperation.ConsumeTokensFromUnit or RuleOperation.ConsumeTokensFromModel
            or RuleOperation.InvokeHeal => true,
        ExecutableOperation => true,
        RuleOperation.InvokeDealHits => hook is EHookID.Activation_OnPreAttack
            or EHookID.Movement_OnMoveThroughEnemy,
        // #197 P10 Crossing Attack: an auto-wound ability rolled by CrossingAttackStage at move-through.
        RuleOperation.InvokeDealAutoWounds => hook is EHookID.Movement_OnMoveThroughEnemy,
        // #197 P10 Storm of X: an action-choice ability enacted by StormStage (routed from ChooseActionStage).
        RuleOperation.InvokeStorm => hook is EHookID.Activation_OnActionChoice,
        // #197 P21 Fanatic: a deploy-time reposition placement DeployUnitStage folds after the executor.
        RuleOperation.RepositionModels => hook is EHookID.Deployment_OnUnitDeployed,
        RuleOperation.InvokeReactivate => hook is EHookID.Activation_OnNextActivatorRequested,
        _ => false,
    };

    private static void SeedCost(Cost cost, UnitData bearer)
    {
        switch (cost)
        {
            case Cost.SpellTokens st:
                SeedTokens(bearer, TokenType.SpellTokens, st.Count);
                break;
            case Cost.ConsumesToken ct:
                SeedTokens(bearer, ct.TType, ct.Count);
                break;
        }
    }

    /// <summary>
    /// Puts the world into a state satisfying the composition/state leaves of
    /// <paramref name="condition"/>. Only positive-polarity leaves act — a negated leaf is already
    /// satisfied by the default empty state (no rules attached, no tokens). Both branches of And/Or
    /// are satisfied; that is safe because satisfying one side of an Or never falsifies the other
    /// under default state. Capability leaves (distance, melee, moved, die faces) are covered by the
    /// context variants instead.
    /// </summary>
    private static void SatisfyCondition(Condition condition, LintWorld world, bool positive)
    {
        switch (condition)
        {
            case Condition.And and:
                SatisfyCondition(and.Left, world, positive);
                SatisfyCondition(and.Right, world, positive);
                break;
            case Condition.Or or:
                SatisfyCondition(or.Left, world, positive);
                SatisfyCondition(or.Right, world, positive);
                break;
            case Condition.Not not:
                SatisfyCondition(not.Inner, world, !positive);
                break;
            case Condition.UnitHasRule hasRule when positive:
                AttachStubRule(world.Bearer, hasRule.RuleName);
                break;
            case Condition.TargetHasRule targetHas when positive:
                AttachStubRule(world.Other, targetHas.RuleName);
                break;
            case Condition.TokenPresent token when positive:
                SeedTokens(world.Bearer, token.TType, token.MinCount);
                break;
        }
    }

    // UnitHasRule/TargetHasRule match attachments by NAME, so an empty stub definition under the
    // required name satisfies them without dragging in the real rule's own effects.
    private static void AttachStubRule(UnitData unit, string ruleName)
    {
        unit.AttachRuleDefinition(new ResolvedRule(ruleName, new SpecialRuleDefinition(ruleName,
            Array.Empty<HookEntry>(), Array.Empty<ActivatedAbility>())));
    }

    private static void SeedTokens(UnitData unit, TokenType type, int count)
    {
        for (int i = 0; i < count; i++)
        {
            unit.Tokens.AddToken(new Token(type, 1, new TokenClearTrigger.ManualOnly()));
        }
    }

    /// <summary> Generic values for every argument slot the rule reads (Deadly(3), Caster(3), ...). </summary>
    private static IReadOnlyList<RuleArgument> ArgumentsFor(SpecialRuleDefinition definition)
    {
        int count = RuleArgumentArity.MaxReferencedArgIndex(definition) + 1;
        var args = new RuleArgument[count];
        for (int i = 0; i < count; i++)
        {
            args[i] = new RuleArgument.Int(GenericArgumentValue);
        }
        return args;
    }

    /// <summary>
    /// The contexts synthesized for a hook, varied across the axes the corpus conditions read
    /// (distance, attacker-moved, melee, charging, action type, natural die faces). Two-unit contexts
    /// are oriented by <paramref name="seat"/>: the bearer plays that seat, the other unit the
    /// opposite. An unknown hook yields nothing (the caller has already reported the dead hook).
    /// </summary>
    private static IEnumerable<IHookContext> ContextVariants(EHookID hook, ERuleSeat seat, LintWorld world)
    {
        IUnit bearer = world.Bearer;
        IUnit attacker = seat == ERuleSeat.Actor ? world.Bearer : world.Other;
        IUnit defender = seat == ERuleSeat.Actor ? world.Other : world.Bearer;

        switch (hook)
        {
            case EHookID.Round_OnRoundStart:
                yield return new RoundStartContext(bearer);
                break;
            case EHookID.Lifecycle_OnCapabilityQuery:
                yield return new CapabilityQueryContext(bearer);
                break;
            case EHookID.Round_OnRoundEnd:
                yield return new RoundEndContext(bearer);
                break;
            case EHookID.Lifecycle_OnUnitCreated:
                yield return new UnitCreatedContext(bearer);
                break;
            case EHookID.Deployment_OnPreDeploymentSelect:
                yield return new PreDeploymentSelectContext(bearer);
                break;
            case EHookID.Deployment_OnUnitDeployed:
                yield return new UnitDeployedContext(bearer);
                break;
            case EHookID.Activation_OnNextActivatorRequested:
                yield return new NextActivatorRequestedContext(bearer);
                break;
            case EHookID.Activation_OnActivationStart:
                yield return new ActivationStartContext(bearer);
                break;
            case EHookID.Activation_OnActionChoice:
                yield return new ActionChoiceContext(bearer);
                break;
            case EHookID.Activation_OnPreAttack:
                foreach (EActionType action in Enum.GetValues<EActionType>())
                {
                    yield return new PreAttackContext(bearer, action);
                }
                break;
            case EHookID.Movement_OnMoveActionDeclared:
                foreach (EActionType action in Enum.GetValues<EActionType>())
                {
                    yield return new MoveActionDeclaredContext(bearer, action, BaseDistanceInches: 6f);
                }
                break;
            case EHookID.Movement_OnChargeDeclared:
                yield return new ChargeDeclaredContext(attacker, defender, BaseDistanceInches: 6f);
                break;
            case EHookID.Movement_OnMoveThroughEnemy:
                yield return new MoveThroughEnemyContext(bearer);
                break;
            case EHookID.Movement_OnMoveThroughTerrain:
                yield return new MoveThroughTerrainContext(bearer);
                break;
            case EHookID.Shooting_OnShootTargetsSelected:
                yield return new ShootTargetsSelectedContext(attacker, defender);
                break;
            case EHookID.Shooting_OnHitRollModifier:
                foreach (float distance in new[] { NearInches, FarInches })
                foreach (bool moved in new[] { false, true })
                foreach (bool isMelee in new[] { false, true })
                foreach (bool isCharging in new[] { false, true })
                foreach (float chargeOrigin in new[] { 0f, FarInches })
                // #197 (P15): the Unpredictable +1-to-hit arm gates on the HitBonus branch here.
                foreach (EUnpredictableBranch branch in UnpredictableBranches)
                {
                    yield return new HitRollModifierContext(attacker, defender, distance, moved,
                        isMelee, isCharging, chargeOrigin, branch);
                }
                // #197 misc: one terrain-populated variant for the Grounded family (not distance-gated).
                yield return new HitRollModifierContext(attacker, defender, FarInches,
                    TerrainPieces: OriginTerrain);
                break;
            case EHookID.Shooting_OnHitRollComplete:
                // chargeOrigin varies independently of the live distance: a melee swing is always resolved in
                // base contact, so only the charge's launch distance can satisfy AttackedFromOverInches there.
                // Without a far-origin variant, every "shoots or charges over 9in away" rule's melee arm would
                // look unfireable to the lint.
                foreach (float distance in new[] { NearInches, FarInches })
                foreach (bool isMelee in new[] { false, true })
                foreach (bool isCharging in new[] { false, true })
                foreach (float chargeOrigin in new[] { 0f, FarInches })
                // #197 (P15): the Unpredictable AP arm gates on the ApBonus branch here.
                foreach (EUnpredictableBranch branch in UnpredictableBranches)
                {
                    yield return new HitRollCompleteContext(attacker, defender, OneOfEachFace(),
                        distance, isMelee, isCharging, IsSpell: false,
                        ChargeOriginDistanceInches: chargeOrigin, UnpredictableBranch: branch);
                }
                // #197 misc: one terrain-populated variant for the Grounded family (not distance-gated).
                yield return new HitRollCompleteContext(attacker, defender, OneOfEachFace(), FarInches,
                    TerrainPieces: OriginTerrain);
                break;
            case EHookID.Shooting_OnSaveRollModifier:
                yield return new CoverIgnoreContext(attacker);
                break;
            case EHookID.Shooting_OnSaveRollComplete:
                foreach (bool isMelee in new[] { false, true })
                foreach (float distance in new[] { NearInches, FarInches })
                foreach (float chargeOrigin in new[] { 0f, FarInches })
                {
                    // isSpell only varies for the shooting-shaped use — the spell pipeline never sets IsMelee.
                    yield return new SaveRollCompleteContext(attacker, defender, OneOfEachFace(), isMelee,
                        IsSpell: false, DistanceInches: distance, ChargeOriginDistanceInches: chargeOrigin);
                }
                yield return new SaveRollCompleteContext(attacker, defender, OneOfEachFace(),
                    IsMelee: false, IsSpell: true);
                break;
            case EHookID.Shooting_OnPreApplyWound:
                yield return new PreApplyWoundContext(attacker, defender);
                break;
            case EHookID.Shooting_OnPostShoot:
                yield return new PostShootActionContext(bearer);
                yield return new PostShootContext(attacker, defender);
                break;
            case EHookID.Shooting_OnUnitDestroyed:
                yield return new UnitDestroyedContext(world.Other, bearer);
                yield return new UnitDestroyedContext(bearer, world.Other);
                break;
            case EHookID.Shooting_OnRangeCheck:
                yield return new RangeModifierContext(attacker);
                break;
            case EHookID.Melee_OnChargeContact:
                yield return new ChargeContactContext(attacker, defender);
                break;
            case EHookID.Melee_OnCounterTrigger:
                yield return new CounterTriggerContext(attacker, defender);
                break;
            case EHookID.Melee_OnMeleeResolution:
                yield return new MeleeResolutionContext(attacker, defender);
                break;
            case EHookID.Melee_OnPostMelee:
                yield return new PostMeleeActionContext(bearer);
                break;
            case EHookID.Morale_OnPreMoraleTest:
                yield return new PreMoraleTestContext(bearer);
                break;
            case EHookID.Morale_OnMoraleTestComplete:
                yield return new MoraleTestContext(bearer);
                break;
        }
    }

    /// <summary> One die of every face, so any UnmodifiedRollEquals(v) sees a match. </summary>
    private static DiceResults OneOfEachFace() => new(new float[] { 1f, 1f, 1f, 1f, 1f, 1f });

    private static string Describe(Condition condition) => condition switch
    {
        Condition.And and => $"And({Describe(and.Left)}, {Describe(and.Right)})",
        Condition.Or or => $"Or({Describe(or.Left)}, {Describe(or.Right)})",
        Condition.Not not => $"Not({Describe(not.Inner)})",
        _ => condition.GetType().Name,
    };

    /// <summary>
    /// A minimal two-unit world for one lint check: the bearer (carrying the rule under test — on its
    /// unit, and on each model's weapon for weapon-scoped rules) and an opposing unit. Fresh per entry
    /// so condition seeding never leaks between checks.
    /// </summary>
    private sealed class LintWorld
    {
        public UnitData Bearer { get; private init; } = null!;
        public UnitData Other { get; private init; } = null!;

        /// <summary> The bearer's carrying weapon for weapon-scoped rules; null for unit rules. </summary>
        public Weapon? Weapon { get; private init; }

        public static LintWorld Build(SpecialRuleDefinition definition)
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();

            Weapon? weapon = null;
            if (definition.Scope == ERuleScope.Weapon)
            {
                weapon = new Weapon("Lint weapon", rangeInches: 24f, attacks: 1, armorPenetration: 0);
                weapon.AttachRuleDefinition(
                    new ResolvedRule(definition.Name, definition, ArgumentsFor(definition)));
            }

            UnitData bearer = BuildUnit(store, "Lint bearer", weapon);
            bearer.AttachRuleDefinition(
                new ResolvedRule(definition.Name, definition, ArgumentsFor(definition)));

            return new LintWorld
            {
                Bearer = bearer,
                Other = BuildUnit(store, "Lint opponent", carriedWeapon: null),
                Weapon = weapon,
            };
        }

        private static UnitData BuildUnit(GameDataStore store, string name, Weapon? carriedWeapon)
        {
            var modelBindings = new List<DataBinding<ModelData>>(LintModelsPerUnit);
            for (int i = 0; i < LintModelsPerUnit; i++)
            {
                var weapons = carriedWeapon == null ? new List<Weapon>() : new List<Weapon> { carriedWeapon };
                var model = new ModelData(baseRadiusInches: 0.75f, weapons, new Position(), store);
                modelBindings.Add(store.GetDataBinding<ModelData>(store.Create(model)));
            }

            var unit = new UnitData(new PlayerID(Guid.NewGuid()), name, quality: 4, defense: 4,
                modelBindings);
            store.Create(unit);
            return unit;
        }
    }
}
