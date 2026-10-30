using System.Collections.Generic;
using System.Linq;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch;

/// <summary>
/// The stat-divergence seams for a joined Hero (#006). A merged unit is one <see cref="UnitData"/> for
/// all the per-unit machinery; these three helpers are the only points where the hero's own stats diverge
/// from the rank and file, each read by exactly one resolution stage:
/// <list type="bullet">
///   <item><see cref="GetMoraleQuality"/> — every morale test, through <c>MoraleUtilities.ResolveMoraleQuality</c>
///         (slice C): the unit tests morale at the hero's Quality while the hero lives and isn't Shaken (#421).</item>
///   <item><see cref="GetSaveDefense"/> — <c>DetermineSaveRollsNeededStage</c> (slice D): the hero uses
///         the unit's Defense until it is the sole survivor, then its own.</item>
///   <item><see cref="GetAttackQuality"/> — <c>DetermineHitRollNeededStage</c> (slice E): a weapon batch
///         owned solely by the hero fires at the hero's Quality.</item>
/// </list>
///
/// All three helpers are implemented and wired into their stages; HeroStatRulesTests pins the behavior.
/// <para>
/// #421 adds the one hero effect that reaches OTHER units: the 3.6 Tough(9)+ morale aura
/// (<see cref="TryGetMoraleAuraHero"/>), which <c>MoraleUtilities.ResolveMoraleQuality</c> weighs against the
/// testing unit's own and joined-hero Quality.
/// </para>
/// </summary>
public static class HeroStatRules
{
    /// <summary>
    /// The Quality a unit tests morale at on its own account: a living joined hero's Quality while the hero
    /// isn't Shaken, otherwise the unit's own. OPR 3.6 Hero: "The Hero's unit ... may use the Hero's Quality
    /// for Morale Tests as long as the Hero isn't Shaken."
    /// <para>
    /// #421: Shaken is a UNIT token (<see cref="TokenType.Shaken"/> lives on <see cref="IUnit.Tokens"/>, never
    /// on a model), so "the hero isn't Shaken" is "the hero's unit isn't Shaken". A Shaken unit always fails
    /// its Morale Tests before any Quality is read (<c>MoraleUtilities.TakeMoraleTest</c>), so this gate never
    /// changes a joined hero's own unit's pass or fail; it keeps the seam truthful for every reader.
    /// </para>
    /// </summary>
    public static int GetMoraleQuality(UnitData unit)
    {
        if (unit.HeroAttachment != null && !unit.Tokens.HasToken(TokenType.Shaken))
        {
            IModel? hero = unit.GetHeroModel();
            if (hero != null && hero.GetIsAlive())
            {
                return unit.HeroAttachment.Quality;
            }
        }

        return unit.Quality;
    }

    /// <summary>#421: the Tough a Hero needs before it lends its Quality to the units around it - 3.6 Hero,
    /// "units within 6\" of friendly Heroes with Tough(9) or higher".</summary>
    public const int MoraleAuraMinTough = 9;

    /// <summary>#421: the reach of that aura, base to base from the Hero to the testing unit's closest
    /// living model.</summary>
    public const float MoraleAuraRangeInches = 6f;

    /// <summary>
    /// #421: a Hero that may lend its Quality to friendly units within <see cref="MoraleAuraRangeInches"/> when
    /// they take a Morale Test: the hero's model (the point the 6" is measured from), its Quality, and the name a
    /// log line or roll chip gives it.
    /// </summary>
    public readonly record struct MoraleAuraHero(IModel Model, int Quality, string Name);

    /// <summary>
    /// #421: whether <paramref name="unit"/> holds a Hero that projects the 3.6 morale aura right now, and which.
    /// OPR 3.6 Hero: "The Hero's unit, and units within 6\" of friendly Heroes with Tough(9) or higher, may use
    /// the Hero's Quality for Morale Tests as long as the Hero isn't Shaken." All of these must hold:
    /// <list type="bullet">
    ///   <item>the unit carries a living Hero - a standalone unit with the Hero rule (Unique Hero attaches it
    ///         too, #430), or a host with a joined hero's model (<see cref="UnitData.HeroAttachment"/>);</item>
    ///   <item>that Hero has Tough(<see cref="MoraleAuraMinTough"/>) or higher, read from the Tough rule's
    ///         ARGUMENT (<see cref="GetToughValue"/>), so a wounded Tough(9) hero still projects. The join cap is
    ///         Tough(6) (<see cref="HeroJoinResolver.MaxHeroToughForJoin"/>), so in practice only standalone
    ///         heroes ever qualify; the joined branch is kept so the reading does not depend on that cap;</item>
    ///   <item>the Hero's unit is not Shaken (a unit token - see <see cref="GetMoraleQuality"/>);</item>
    ///   <item>the Hero's unit is on the table (<see cref="IUnitExtensions.GetIsOnBattlefield"/>: not in reserve,
    ///         not embarked, not pushed off the table) - a Hero that is not on the table is "within" nothing
    ///         (#421 D3, owner review).</item>
    /// </list>
    /// Whether the testing unit is friendly and within range is the caller's question
    /// (<c>MoraleUtilities.ResolveMoraleQuality</c>).
    /// </summary>
    public static bool TryGetMoraleAuraHero(IUnit unit, out MoraleAuraHero hero)
    {
        hero = default;
        if (!unit.GetIsAlive() || unit.Tokens.HasToken(TokenType.Shaken) || !unit.GetIsOnBattlefield())
        {
            return false;
        }

        if (unit is UnitData { HeroAttachment: { } attachment } host)
        {
            IModel? joined = host.GetHeroModel();
            if (joined == null || !joined.GetIsAlive() || GetToughValue(unit, joined) < MoraleAuraMinTough)
            {
                return false;
            }

            hero = new MoraleAuraHero(joined, attachment.Quality, attachment.Name ?? "Hero");
            return true;
        }

        if (!unit.RuleDefinitions.Any(rule => rule.Definition == CoreRuleCatalog.Hero))
        {
            return false;
        }

        // A standalone Hero unit: every model is the Hero (in the corpus it is one model). The toughest
        // living one stands for it, so the 6" is measured from a model that actually qualifies.
        IModel? toughest = null;
        int toughestValue = 0;
        foreach (IModel model in unit.Models)
        {
            if (!model.GetIsAlive()) continue;
            int tough = GetToughValue(unit, model);
            if (toughest == null || tough > toughestValue)
            {
                toughest = model;
                toughestValue = tough;
            }
        }

        if (toughest == null || toughestValue < MoraleAuraMinTough)
        {
            return false;
        }

        hero = new MoraleAuraHero(toughest, unit.Quality, unit.Name);
        return true;
    }

    /// <summary>
    /// #421: one model's Tough(X) as printed - the Tough rule's argument, never its max or remaining wounds (a
    /// wounded Tough(9) hero is still a Tough(9) hero). A native model has the higher of the unit's Tough and a
    /// Tough of its own (#407 per-model grants, e.g. a one-model Weapon Team's Tough(3)); a joined hero has only
    /// its own, which the join relocated onto its model (#006 slice F) - the host's Tough is the rank and file's.
    /// 0 when no Tough rule applies.
    /// </summary>
    public static int GetToughValue(IUnit unit, IModel model)
    {
        int own = MaxToughArgument(model.RuleDefinitions);
        if (unit is UnitData { HeroAttachment: { } hero } && model.ID == hero.HeroModelId)
        {
            return own;
        }

        return Math.Max(own, MaxToughArgument(unit.RuleDefinitions));
    }

    private static int MaxToughArgument(IReadOnlyList<ResolvedRule> rules)
    {
        int max = 0;
        foreach (ResolvedRule rule in rules)
        {
            if (rule.Definition == CoreRuleCatalog.Tough && rule.Arguments.Count > 0
                && rule.Arguments[0] is RuleArgument.Int tough && tough.Value > max)
            {
                max = tough.Value;
            }
        }
        return max;
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
    /// #431: one MODEL's own Defense - the hero's for a joined hero's model, the unit's for every other - for the
    /// "where most models have Defense X+ to Y+" gates (<c>Condition.TargetMostModelsDefenseInRange</c>), which
    /// count models rather than asking what the unit saves at. Distinct from <see cref="GetSaveDefense"/>, the
    /// single value the save stage rolls against. Takes an <see cref="IUnit"/> so rule conditions can call it;
    /// only a <see cref="UnitData"/> can carry a joined hero.
    /// </summary>
    public static int GetModelDefense(IUnit unit, IModel model)
    {
        if (unit is UnitData { HeroAttachment: { } hero } && model.ID == hero.HeroModelId)
        {
            return hero.Defense;
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
        // #191 search perf: this was Where(...).ToList() with a fresh comparer, an outer closure and a
        // LINQ Any() over every model's weapons - ~2.5 KB of garbage per call, and the Tactician's
        // volley estimate calls it twice per volley on units of twenty-plus models (measured: 55% of
        // the combat estimate's allocation). Same models, same order; the comparer is stateless, and
        // the empty answer - a batch nobody living carries - needs no list at all.
        IReadOnlyList<IModel> models = unit.Models;
        List<IModel>? owners = null;
        for (int i = 0; i < models.Count; i++)
        {
            IModel model = models[i];
            if (!model.GetIsAlive()) continue;

            IReadOnlyList<Weapon> weapons = model.Weapons;
            bool carriesBatch = false;
            for (int w = 0; w < weapons.Count; w++)
            {
                if (!s_weaponComparer.Equals(weapons[w], weaponType)) continue;
                carriesBatch = true;
                break;
            }
            if (!carriesBatch) continue;

            owners ??= new List<IModel>(models.Count - i);
            owners.Add(model);
        }
        return owners ?? (IReadOnlyList<IModel>)Array.Empty<IModel>();
    }

    /// <summary>Stateless (it only reads the two weapons), so one instance serves every comparison.</summary>
    private static readonly WeaponComparer s_weaponComparer = new WeaponComparer();

    /// <summary>
    /// The living models of a defending unit — the Subject-seat counterpart to
    /// <see cref="LivingWeaponBatchOwners"/>. The defensive dispatch sites (#183) pass these as the
    /// per-model rule contributors under <see cref="EModelRuleScope.AnyOwner"/>, so a joined hero's
    /// relocated defensive rules (Evasive, Resistance, Fortified, ...) become VISIBLE to the evaluator —
    /// and the #163 trace — after the merge. The rule's own <see cref="Definitions.Condition.MostModelsHaveThisRule"/>
    /// gate (#428) then decides whether it actually applies (only when most living models have it — for a
    /// hero-only rule, once the hero is the sole survivor). For a non-hero unit no model carries per-model
    /// rules, so unioning them adds nothing and behaviour is unchanged.
    /// </summary>
    public static IReadOnlyList<IModel> LivingModels(IUnit unit)
    {
        // Pre-sized loop: called per unit per Tactician evaluation, and the Where().ToList() growth
        // showed in the profile (#191 step 3, 2026-09-03).
        IReadOnlyList<IModel> models = unit.Models;
        var living = new List<IModel>(models.Count);
        for (int i = 0; i < models.Count; i++)
        {
            if (models[i].GetIsAlive()) living.Add(models[i]);
        }
        return living;
    }
}
