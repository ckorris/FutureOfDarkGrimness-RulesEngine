using FDG.Rules.Definitions;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;

namespace FDG.Rules.Dispatch;

/// <summary>
/// Applies creation-time #042 rules to a freshly built unit. The lifecycle counterpart of the
/// stage integrations: instead of a stage firing a "when", army-load calls this once per unit after
/// its rules are attached. Currently drives Tough (sets each model's max wounds), but any future
/// <see cref="EHookID.Lifecycle_OnUnitCreated"/> rule folds in here the same way.
/// </summary>
public static class UnitCreationRules
{
    /// <summary>
    /// Fires <see cref="EHookID.Lifecycle_OnUnitCreated"/> for <paramref name="unit"/>, folds the
    /// max-wounds sink, and (if any Tough-style rule fired) sets every model's max wounds. A no-op
    /// for units with no creation rules.
    /// </summary>
    public static void Apply(IUnit unit, RuleEvaluator evaluator)
    {
        IReadOnlyList<RuleOperation> operations = evaluator.EvaluateAll(
            new UnitCreatedContext(unit), RuleParticipant.Actor(unit));

        // Auras (and any other creation-time grants): an Effect.Aura at Lifecycle_OnUnitCreated emits a
        // GrantTokenToUnit; apply it here so the granted rule projects unit-wide for the rest of the game
        // (read back by RuleEvaluator.CollectGrantedRules). ApplyTokenOperations only touches token ops,
        // so the SetMaxWounds ops the MaxWoundsSink folds below are untouched — applying both over the one
        // queue is safe. Without this the grant op was produced and dropped, leaving auras inert in-game.
        OperationApplier.ApplyTokenOperations(operations);

        MaxWoundsSink maxWounds = new MaxWoundsSink();
        maxWounds.ApplyFrom(operations);

        // #197 Armor(X): a defense-set rule replaces the unit's Defense stat outright (a literal SET,
        // not a floor). Written here, at creation, so every downstream reader — the save stages,
        // impact/reflect synthetic paths, HeroStatRules.GetSaveDefense, and the AI's CombatMath — sees
        // the set value with no per-path folding. A joined hero's own Armor is the join resolver's
        // job (its standalone unit never passes through here); this write covers the HOST unit's stat.
        DefenseSetSink defenseSet = new DefenseSetSink();
        defenseSet.ApplyFrom(operations);
        if (defenseSet.HasSet && unit is UnitData unitStats)
        {
            unitStats.Defense = defenseSet.Defense;
        }

        // #006: a joined hero keeps its OWN max wounds (its Tough), not the host unit's. The hero's
        // standalone unit is never registered, so the creation-rules pass never runs for it directly —
        // its wound count rides on the host's HeroAttachment and is applied to the hero model here.
        HeroAttachment? hero = (unit as UnitData)?.HeroAttachment;

        // #382: the join relocated the hero's unit-scoped rules onto the hero MODEL, and the
        // participant above carries no models — so an aura the hero brought (Effect.Aura at this
        // hook: a Robot Legions lord's Reanimation Aura) never fired, leaving the granted rule inert
        // on exactly the unit it was bought for. Matched by EFFECT SHAPE, mirroring
        // HeroJoinResolver.ResolveJoinedHeroDefense, NOT by walking the hero model's rules through
        // the evaluator: the hero's other creation-time rules are hero-personal (Tough is baked into
        // HeroAttachment.HeroWounds, Armor(X) into the join's defense) and a general walk would apply
        // them unit-wide. Auras are the only creation-time effect whose semantics are the whole unit.
        if (hero != null)
        {
            ApplyJoinedHeroAuras(unit, hero);
        }

        if (!maxWounds.HasMax && hero == null)
        {
            return;
        }

        foreach (IModel model in unit.Models)
        {
            if (hero != null && model.ID == hero.HeroModelId)
            {
                model.SetMaxWounds(hero.HeroWounds);
                continue;
            }

            if (maxWounds.HasMax)
            {
                model.SetMaxWounds(maxWounds.MaxWounds);
            }
        }
    }

    /// <summary>
    /// #382 — fires the creation-time auras riding the joined hero's model, granting to the HOST unit.
    /// Conditions are not evaluated (creation entries are authored <c>Condition.Always</c> — the
    /// <c>HeroJoinResolver.ResolveJoinedHeroDefense</c> precedent). Deduped by granted rule name against
    /// grants the unit already holds, so a host that statically carries the same aura (which fired in the
    /// main pass above) doesn't double-grant, matching the evaluator's argument-less dedup.
    /// </summary>
    private static void ApplyJoinedHeroAuras(IUnit unit, HeroAttachment hero)
    {
        IModel? heroModel = unit.Models.FirstOrDefault(m => m.ID == hero.HeroModelId);
        if (heroModel == null)
        {
            return;
        }

        List<RuleOperation> operations = new List<RuleOperation>();
        foreach (ResolvedRule rule in heroModel.RuleDefinitions)
        {
            foreach (HookEntry entry in rule.Definition.Passive)
            {
                if (entry.HookID != EHookID.Lifecycle_OnUnitCreated || entry.Effect is not Effect.Aura aura
                    || UnitHoldsGrant(unit, aura.RuleName))
                {
                    continue;
                }

                aura.Apply(new RuleInvocation(Hook: null, Bearer: unit, Arguments: rule.Arguments,
                    Definition: rule.Definition), operations);
                OperationApplier.ApplyTokenOperations(operations);
                operations.Clear();
            }
        }
    }

    /// <summary> Whether the unit already holds a <see cref="TokenType.RuleGrant"/> for this rule name. </summary>
    private static bool UnitHoldsGrant(IUnit unit, string ruleName)
    {
        foreach (Token token in unit.Tokens.GetAllTokens(TokenType.RuleGrant))
        {
            if (token.Payload is TokenPayload.RuleGrant grant
                && string.Equals(grant.RuleName, ruleName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
