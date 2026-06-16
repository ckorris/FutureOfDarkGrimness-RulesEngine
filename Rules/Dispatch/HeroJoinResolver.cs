using System;
using System.Collections.Generic;
using System.Linq;
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

            ModelID heroModelId = hero.ModelBindings[0].GetValue().ID;
            int heroWounds = hasTough ? tough : IModel.DEFAULT_WOUND_COUNT;
            host.AttachHero(new HeroAttachment(heroModelId, hero.Quality, hero.Defense, heroWounds), hero.ModelBindings);
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
}
