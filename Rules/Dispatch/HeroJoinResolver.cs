using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Rules.Definitions;
using FDG.Rules.Foundation;
using FDG.SaveLoad;

namespace FDG.Rules.Dispatch;

/// <summary>
/// Resolves Hero unit-joins at army setup (#006). A Hero is a single-model unit carrying the "Hero"
/// special rule whose army-list entry names a <see cref="UnitFileEntry.JoinsUnitId"/>; this merges its
/// model into that host unit (via <see cref="UnitData.AttachHero"/>) so the combined unit fights as one
/// for the rest of the game. The lifecycle counterpart of <see cref="UnitCreationRules"/>: instead of a
/// per-unit creation hook, army-load calls this once per army after all its units are built but before
/// they are registered, because the merge needs cross-unit information (the host) a single-unit hook
/// can't see.
///
/// Eligibility follows the rule text — "Heroes with up to Tough(6) may deploy as part of one multi-model
/// unit without another Hero." A hero whose join is rejected for any reason is left as its own standalone
/// unit (it deploys solo) and the reason is surfaced via <paramref name="warn"/>, never silently dropped.
/// </summary>
public static class HeroJoinResolver
{
    /// <summary> A hero may only join a host if its Tough value is at most this (rule text: "up to Tough(6)"). </summary>
    public const int MaxHeroToughForJoin = 6;

    /// <summary>
    /// Performs all valid Hero joins among <paramref name="built"/> (the freshly built units paired with
    /// their army-list entries, with rules already attached) and returns the units that should be
    /// registered as standalone units: every non-merging unit plus the hosts. A hero merged into a host is
    /// absorbed into it and omitted from the result. Order is preserved.
    /// </summary>
    public static IReadOnlyList<UnitData> Apply(
        IReadOnlyList<(UnitFileEntry Entry, UnitData Unit)> built,
        Action<string>? warn = null)
    {
        // Hosts are addressed by their authorable Id. Duplicate Ids are a data error; first one wins and
        // the rest are unaddressable (a hero targeting a duplicated Id resolves to the first).
        Dictionary<string, UnitData> byId = new Dictionary<string, UnitData>();
        foreach ((UnitFileEntry entry, UnitData unit) in built)
        {
            if (string.IsNullOrEmpty(entry.Id))
            {
                continue;
            }

            if (!byId.TryAdd(entry.Id, unit))
            {
                warn?.Invoke($"Hero join: duplicate unit Id '{entry.Id}'; only the first '{byId[entry.Id].Name}' is addressable.");
            }
        }

        HashSet<UnitData> merged = new HashSet<UnitData>();

        foreach ((UnitFileEntry entry, UnitData hero) in built)
        {
            // Only entries that are Heroes AND name a join target are candidates; a Hero with no
            // JoinsUnitId deploys on its own with no diagnostic.
            if (!HasRule(hero, CoreRuleCatalog.Hero) || string.IsNullOrEmpty(entry.JoinsUnitId))
            {
                continue;
            }

            if (hero.ModelBindings.Count != 1)
            {
                warn?.Invoke($"Hero join: '{hero.Name}' is not a single-model unit; it will deploy on its own.");
                continue;
            }

            bool hasTough = TryGetToughValue(hero, out int tough);
            if (hasTough && tough > MaxHeroToughForJoin)
            {
                warn?.Invoke($"Hero join: '{hero.Name}' has Tough({tough}) > {MaxHeroToughForJoin} and cannot join a unit; it will deploy on its own.");
                continue;
            }

            if (!byId.TryGetValue(entry.JoinsUnitId, out UnitData? host))
            {
                warn?.Invoke($"Hero join: '{hero.Name}' names join target Id '{entry.JoinsUnitId}', which no unit declares; it will deploy on its own.");
                continue;
            }

            if (ReferenceEquals(host, hero))
            {
                warn?.Invoke($"Hero join: '{hero.Name}' names itself as its join target; it will deploy on its own.");
                continue;
            }

            if (host.ModelBindings.Count <= 1)
            {
                warn?.Invoke($"Hero join: '{hero.Name}' cannot join '{host.Name}', which is not a multi-model unit; it will deploy on its own.");
                continue;
            }

            if (HasRule(host, CoreRuleCatalog.Hero) || host.HasHero)
            {
                warn?.Invoke($"Hero join: '{hero.Name}' cannot join '{host.Name}', which already contains a Hero; it will deploy on its own.");
                continue;
            }

            ModelData heroModel = hero.ModelBindings[0].GetValue();
            ModelID heroModelId = heroModel.ID;
            int heroWounds = hasTough ? tough : IModel.DEFAULT_WOUND_COUNT;
            host.AttachHero(new HeroAttachment(heroModelId, hero.Quality, ResolveJoinedHeroDefense(hero), heroWounds),
                hero.ModelBindings);

            // #329: the absorbed hero's cost folds into the host, mirroring the Forge's combined-cost
            // precedent (ListCompiler), so per-unit points still sum to the army total after the merge.
            host.PointCost += hero.PointCost;

            // #006 slice F: carry the hero's own unit-scoped rules onto the hero MODEL, so they fire for the
            // hero alone (Furious/Relentless/Thrust) rather than the whole host unit. The Hero marker stays
            // structural (no hooks); weapon-scoped rules already ride the hero's weapon and need no move.
            // #183: DEFENSIVE (Subject-seat) rules relocated here (Evasive/Resistance/Fortified/...) are now
            // visible at every Subject dispatch site, which passes the defender's living models (AnyOwner).
            // Their AllModelsHaveThisRule gate then governs both directions: a hero that lacks a rule the
            // host has breaks it for the unit, and a rule only the hero has fires only once the hero is the
            // sole survivor (matching the last-model-Defense philosophy). Before #183 these silently vanished.
            foreach (ResolvedRule rule in hero.RuleDefinitions)
            {
                if (rule.Definition == CoreRuleCatalog.Hero)
                {
                    continue;
                }

                heroModel.AttachRuleDefinition(rule);
            }

            merged.Add(hero);
        }

        return built.Where(pair => !merged.Contains(pair.Unit)).Select(pair => pair.Unit).ToList();
    }

    private static bool HasRule(UnitData unit, Rules.Definitions.SpecialRuleDefinition definition) =>
        unit.RuleDefinitions.Any(rule => rule.Definition == definition);

    private static bool TryGetToughValue(UnitData unit, out int value)
    {
        ResolvedRule? tough = unit.RuleDefinitions.FirstOrDefault(rule => rule.Definition == CoreRuleCatalog.Tough);
        if (tough != null && tough.Arguments.Count > 0 && tough.Arguments[0] is RuleArgument.Int toughArg)
        {
            value = toughArg.Value;
            return true;
        }

        value = 0;
        return false;
    }

    /// <summary>
    /// The Defense the attachment should carry for a joining hero: its creation-time defense set
    /// (Armor(X), #197) if it has one, else its own stat. The hero's standalone unit never passes
    /// through <see cref="UnitCreationRules"/> — the join consumes it first — so the set must be
    /// baked in here, the same way <c>heroWounds</c> bakes in Tough. Matched by effect shape
    /// (a <see cref="Effect.SetDefense"/> at <see cref="EHookID.Lifecycle_OnUnitCreated"/>), not by
    /// rule name, so a book alias can't dodge it; folded through the same sink as the creation
    /// applicator (best of several, literal SET over the base stat). Conditions are not evaluated,
    /// matching <see cref="TryGetToughValue"/>'s argument-only read — creation-time entries are
    /// authored <c>Condition.Always</c>.
    /// </summary>
    private static int ResolveJoinedHeroDefense(UnitData hero)
    {
        DefenseSetSink defenseSet = new DefenseSetSink();
        foreach (ResolvedRule rule in hero.RuleDefinitions)
        {
            foreach (HookEntry entry in rule.Definition.Passive)
            {
                if (entry.HookID != EHookID.Lifecycle_OnUnitCreated || entry.Effect is not Effect.SetDefense set)
                {
                    continue;
                }

                List<RuleOperation> operations = new List<RuleOperation>();
                set.Apply(new RuleInvocation(Hook: null, Bearer: hero, Arguments: rule.Arguments,
                    Definition: rule.Definition), operations);
                defenseSet.ApplyFrom(operations);
            }
        }

        return defenseSet.HasSet ? defenseSet.Defense : hero.Defense;
    }
}
