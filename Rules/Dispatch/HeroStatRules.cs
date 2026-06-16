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
/// NOTE: the bodies below are deliberately the pre-#006 behavior (always the unit's own stat). The
/// per-slice work fills each in (and wires the matching stage to call it). The HeroStatRulesTests red
/// tests pin the intended hero behavior so each slice has a failing test to drive it.
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
        // TODO #006 slice D: if the only living model is the hero, return HeroAttachment.Defense.
        return unit.Defense;
    }

    /// <summary>
    /// The Quality a weapon batch hits at. A batch whose living owners are exactly the hero fires at the
    /// hero's Quality; a batch the rank and file (also) carry stays at the unit's Quality (the deferred
    /// same-weapon-collision case).
    /// </summary>
    public static int GetAttackQuality(UnitData unit, IWeapon weaponType)
    {
        // TODO #006 slice E: if the living owners of weaponType are exactly the hero model, return
        // HeroAttachment.Quality (resolve owners via WeaponComparer, the AttackBeatPositions pattern).
        return unit.Quality;
    }
}
