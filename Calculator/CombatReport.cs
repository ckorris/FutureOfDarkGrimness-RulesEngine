using System.Collections.Generic;

namespace FDG.Calculator
{
    /// <summary>Which half of the rules a <see cref="CombatCalculator"/> run exercises.</summary>
    public enum ECombatMode
    {
        Shooting,
        Melee,
    }

    /// <summary>
    /// The situational facts a real attack would read off the table, supplied by hand because the
    /// calculator has no table. Everything here is an INPUT the caller controls; anything derived from
    /// the units themselves (thresholds, ranges, which rules fire) is computed by the engine.
    /// </summary>
    public sealed record CombatSituation(
        ECombatMode Mode = ECombatMode.Shooting,
        float DistanceInches = 12f,
        bool DefenderInCover = false,
        bool AttackerMoved = false,
        bool AttackerCharging = false,
        bool AttackerFatigued = false);

    /// <summary>
    /// One save-threshold group within a volley - the shape the save stage produces when a per-hit rule
    /// (Rending) peels hits into their own bucket, or a hit-injecting rule (Furious) adds a group.
    /// </summary>
    public sealed record SaveBucket(int SaveNeeded, float Hits, string Label);

    /// <summary>One weapon profile's contribution: the numbers the stages produced for that batch.</summary>
    public sealed record VolleyReport(
        IWeapon Weapon,
        int Copies,
        bool InRange,
        float EffectiveRangeInches,
        float AttackDice,
        int HitRollNeeded,
        IReadOnlyList<string> HitTags,
        float ExpectedHits,
        IReadOnlyList<SaveBucket> Saves,
        IReadOnlyList<string> SaveTags,
        float ExpectedWounds,
        IReadOnlyList<string> Notes);

    /// <summary>
    /// The result of one attacker-attacks-defender simulation. <see cref="ExpectedWounds"/> is what the
    /// defender ACTUALLY lost (wounds are applied as the volleys resolve, so a later weapon fires into
    /// the casualties the earlier ones caused and cannot over-kill a dead unit).
    /// </summary>
    public sealed record CombatReport(
        ECombatMode Mode,
        string AttackerName,
        string DefenderName,
        float DefenderWoundsBefore,
        float DefenderWoundsAfter,
        IReadOnlyList<VolleyReport> Volleys,
        float ExpectedHits,
        float ExpectedWounds,
        IReadOnlyList<string> Notes,
        IReadOnlyList<string> Warnings);
}
