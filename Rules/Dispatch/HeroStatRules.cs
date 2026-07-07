using System.Collections.Generic;
using System.Linq;

namespace FDG.Rules.Dispatch;

/// <summary>
/// The stat-divergence seams for a joined Hero (#006). A merged unit is one <see cref="UnitData"/> for
/// all the per-unit machinery; these three helpers are the only points where the hero's own stats diverge
/// from the rank and file, each read by exactly one resolution stage:
/// <list type="bullet">
///   <item><see cref="GetMoraleQuality"/> — <c>DetermineMoraleSaveNeededStage</c> / wound-driven morale
///         (slice C): the unit tests morale at the hero's Quality while the hero lives.</item>
///   <item><see cref="GetSaveDefense"/> — <c>DetermineSaveRollsNeededStage</c> (slice D): the hero uses
///         the unit's Defense until it is the sole survivor, then its own.</item>
///   <item><see cref="GetAttackQuality"/> — <c>DetermineHitRollNeededStage</c> (slice E): a weapon batch
///         owned solely by the hero fires at the hero's Quality.</item>
/// </list>
///
/// All three helpers are implemented and wired into their stages; HeroStatRulesTests pins the behavior.
/// </summary>
public static class HeroStatRules
{
    /// <summary>
    /// The Quality a unit tests morale at. With a living joined hero this is the hero's Quality (the rule's
    /// "may take morale tests on behalf of the unit"); otherwise the unit's own.
    /// </summary>
    public static int GetMoraleQuality(UnitData unit)
    {
        if (unit.HeroAttachment != null)
        {
            IModel? hero = unit.GetHeroModel();
            if (hero != null && hero.GetIsAlive())
            {
                return unit.HeroAttachment.Quality;
            }
        }

        return unit.Quality;
    }

    /// <summary>
    /// The Defense a unit's models save at. A joined hero uses the unit's Defense until every other model
    /// is dead; once the hero is the sole survivor the unit saves at the hero's own Defense.
    /// </summary>
    public static int GetSaveDefense(UnitData unit)
    {
        if (unit.HeroAttachment != null)
        {
            List<IModel> living = unit.Models.Where(model => model.GetIsAlive()).ToList();
            if (living.Count == 1 && living[0].ID == unit.HeroAttachment.HeroModelId)
            {
                return unit.HeroAttachment.Defense;
            }
        }

        return unit.Defense;
    }

    /// <summary>
    /// The Quality a weapon batch hits at. A batch whose living owners are exactly the hero fires at the
    /// hero's Quality; a batch the rank and file (also) carry stays at the unit's Quality (the deferred
    /// same-weapon-collision case).
    /// </summary>
    public static int GetAttackQuality(UnitData unit, IWeapon weaponType)
    {
        if (unit.HeroAttachment != null)
        {
            IReadOnlyList<IModel> owners = LivingWeaponBatchOwners(unit, weaponType);

            // Hero's own Quality only when this batch is owned by the hero alone. If a rank-and-file model
            // also carries a matching weapon (the deferred same-weapon-pooling case), it stays unit Quality.
            if (owners.Count > 0 && owners.All(model => model.ID == unit.HeroAttachment.HeroModelId))
            {
                return unit.HeroAttachment.Quality;
            }
        }

        return unit.Quality;
    }

    /// <summary>
    /// The living models in <paramref name="unit"/> carrying a weapon matching <paramref name="weaponType"/>
    /// (by <see cref="WeaponComparer"/>) — the owners of a weapon batch. The hit stages pass these as the
    /// batch's per-model rule contributors under <see cref="EModelRuleScope.AllOwners"/>, so a per-model rule
    /// fires only when every owner shares it (#093): a hero-only batch fires the hero's own rules; a
    /// homogeneous squad's shared per-model rule fires once; a weapon the rank and file also carry doesn't
    /// leak the hero's rules onto their pooled shots. Also the source of the slice-E attack-Quality gate.
    /// </summary>
    public static IReadOnlyList<IModel> LivingWeaponBatchOwners(UnitData unit, IWeapon weaponType)
    {
        WeaponComparer comparer = new WeaponComparer();
        return unit.Models
            .Where(model => model.GetIsAlive() && model.Weapons.Any(weapon => comparer.Equals(weapon, weaponType)))
            .ToList();
    }
}
