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
    /// <param name="ChargeFromInches">#442 (#432): how far, base to base, the charger stood from the defender when it
    /// declared the charge - what the engine snapshots at activation start and the "charges / is charged from over
    /// 6"" rules read (Ferocious's extra hit, a Changebound defender's -1). Only a charging melee carries it; 0 (the
    /// default) means not given, and no charge arm fires.</param>
    public sealed record CombatSituation(
        ECombatMode Mode = ECombatMode.Shooting,
        float DistanceInches = 12f,
        bool DefenderInCover = false,
        bool AttackerMoved = false,
        bool AttackerCharging = false,
        bool AttackerFatigued = false,
        float ChargeFromInches = 0f);

    /// <summary>
    /// One save-threshold group within a volley - the shape the save stage produces when a per-hit rule
    /// (Rending) peels hits into their own bucket, or a hit-injecting rule (Furious) adds a group.
    /// </summary>
    public sealed record SaveBucket(int SaveNeeded, float Hits, string Label);

    /// <summary>
    /// #423 / #441 F4: the defender's Parry step on one volley - the dice it rolled (its pool capped at the hits),
    /// the hits those dice ignored, and the hits that had landed before it. Structured so the front end can show it
    /// as a rule chip with the numbers in a hover; <see cref="Describe"/> is the sentence the volley note used to carry.
    /// </summary>
    public sealed record ParrySummary(float DiceRolled, float HitsIgnored, float HitsBefore)
    {
        /// <summary>The chip's label - the rule's name, as the hit and save chips name theirs.</summary>
        public const string ChipLabel = "Parry";

        /// <summary>"4 dice ignored 0.67 of 4 hits before the saves."</summary>
        public string Describe() =>
            $"{DiceRolled:0.##} dice ignored {HitsIgnored:0.##} of {HitsBefore:0.##} hits before the saves.";
    }

    /// <summary>One weapon profile's contribution: the numbers the stages produced for that batch.
    /// <paramref name="Parry"/> is the defender's Parry step (#423), null when no Parry die was rolled.</summary>
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
        IReadOnlyList<string> Notes,
        ParrySummary? Parry = null);

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
