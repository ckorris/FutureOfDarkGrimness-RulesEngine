namespace FDG.Rules.Foundation;

public enum EHookID
{
    /// <summary>
    /// Top of every round, before any activations.
    /// Context: round number, active player list.
    /// </summary>
    Round_OnRoundStart = 0,

    /// <summary>
    /// After ReconcileObjectives, before the next round begins.
    /// Context: round number.
    /// </summary>
    Round_OnRoundEnd = 1,

    /// <summary>
    /// About to deploy a unit — used to remove Ambush/Scout/Re-Deploy units from the
    /// normal selection pool so they can be placed by their own dedicated stages.
    /// Context: unit, deployment pool.
    /// </summary>
    Deployment_OnPreDeploymentSelect = 10,

    /// <summary>
    /// A unit's deployment placement just committed.
    /// Context: unit, position.
    /// </summary>
    Deployment_OnUnitDeployed = 11,

    /// <summary>
    /// All non-Scout / non-Ambush / non-Re-Deploy units have been placed; the Scout and
    /// Re-Deploy passes follow this hook.
    /// Context: all deployed units.
    /// </summary>
    Deployment_OnPostDeploymentComplete = 12,

    /// <summary>
    /// A unit was first added to ITableState (army-setup time). Used to grant
    /// once-per-game tokens and other creation-time bookkeeping.
    /// Context: unit, army list source.
    /// </summary>
    Lifecycle_OnUnitCreated = 20,

    /// <summary>
    /// A wound was absorbed (Regeneration or similar) rather than applied. Used by
    /// rules that count or react to ignored wounds (e.g. Regenerative Strength markers).
    /// Context: unit, source rule, attacker.
    /// </summary>
    Lifecycle_OnWoundIgnored = 21,

    /// <summary>
    /// The engine is choosing the next unit to activate. Used by rules that offer
    /// re-activation (e.g. Martial Prowess) before the normal pick happens.
    /// Context: candidate units, round number.
    /// </summary>
    Activation_OnNextActivatorRequested = 30,

    /// <summary>
    /// The selected unit's activation begins, before any action is chosen. Used by
    /// rules that offer per-activation choices (e.g. Versatile Attack/Reach).
    /// Context: unit.
    /// </summary>
    Activation_OnActivationStart = 31,

    /// <summary>
    /// ChooseAction is offering Hold/Advance/Rush/Charge. Used to gate or modify
    /// available actions (e.g. Shaken units must take no action).
    /// Context: unit, candidate actions.
    /// </summary>
    Activation_OnActionChoice = 32,

    /// <summary>
    /// The unit has chosen an attack action, before targets and weapons are resolved.
    /// Trigger point for activated abilities like Mend, Re-Position Artillery,
    /// Unstoppable Mark, Precision Fighting Mark.
    /// Context: unit, action type.
    /// </summary>
    Activation_OnPreAttack = 33,

    /// <summary>
    /// The activation is completing, before the stage machine transitions. Used for
    /// end-of-activation morale tests, clearing activation-scoped tokens, etc.
    /// Context: unit, wounds taken this activation.
    /// </summary>
    Activation_OnEndOfActivation = 34,

    /// <summary>
    /// The player has chosen Advance/Rush/Charge — used to compute the distance budget
    /// (Fast/Slow/Agile/Rapid Rush modify it here).
    /// Context: unit, action type, base distance.
    /// </summary>
    Movement_OnMoveActionDeclared = 50,

    /// <summary>
    /// Specific to a Charge action; fires alongside OnMoveActionDeclared. Used for
    /// rules that affect charge movement specifically (e.g. Melee Shrouding -3").
    /// Context: unit, target, base distance.
    /// </summary>
    Movement_OnChargeDeclared = 51,

    /// <summary>
    /// The unit's path passes through (not just adjacent to) an enemy unit's footprint.
    /// Trigger for Strafing-style mid-move attacks.
    /// Context: unit, enemy unit, path point.
    /// </summary>
    Movement_OnMoveThroughEnemy = 52,

    /// <summary>
    /// The unit's path enters a terrain piece. Used for dangerous-terrain tests and
    /// difficult-terrain movement caps.
    /// Context: unit, terrain piece, terrain type.
    /// </summary>
    Movement_OnMoveThroughTerrain = 53,

    /// <summary>
    /// The unit has finished its move and its position is committed. Trigger for
    /// Harassing-style "after moving" effects and Indirect's "moved this activation" flag.
    /// Context: unit, final path.
    /// </summary>
    Movement_OnMoveResolved = 54,

    /// <summary>
    /// After weapon groups and targets have been chosen, before any rolls. Last chance
    /// to react to target choice before the resolution flow runs.
    /// Context: unit, weapon groups, targets.
    /// </summary>
    Shooting_OnShootTargetsSelected = 70,

    /// <summary>
    /// Calculating how many hit rolls to make. Trigger for attack-count modifiers
    /// (e.g. Regenerative Strength's per-marker bonus attacks).
    /// Context: weapon, attacker, target, base attack count.
    /// </summary>
    Shooting_OnPreHitRollCount = 71,

    /// <summary>
    /// Adjusting Quality value or per-roll modifiers before rolling to hit. Trigger for
    /// Stealth, Indirect, Reliable, AP-on-hit-mod effects.
    /// Context: weapon, attacker, target, modifier stack.
    /// </summary>
    Shooting_OnHitRollModifier = 72,

    /// <summary>
    /// Hit rolls collected, before the save flow runs. This is where Furious / Rending /
    /// Surge convert unmodified 6s into bonus hits or AP promotions.
    /// Context: weapon, attacker, target, hit dice results.
    /// </summary>
    Shooting_OnHitRollComplete = 73,

    /// <summary>
    /// Determining how many save rolls are needed and at what target value. Where
    /// Rending-style AP overrides land before save rolls happen.
    /// Context: hits, AP, defender, weapon.
    /// </summary>
    Shooting_OnPreSaveRollCount = 74,

    /// <summary>
    /// Adjusting Defense value or per-roll modifiers before rolling to save. Trigger
    /// for Cover and Shielded.
    /// Context: defender, save needed, modifier stack.
    /// </summary>
    Shooting_OnSaveRollModifier = 75,

    /// <summary>
    /// After defense rolls, before applying wounds. Where Regeneration absorbs wounds
    /// and Bane forces re-rolls of defense 6s (and suppresses Regeneration in turn).
    /// Context: defender, failed saves.
    /// </summary>
    Shooting_OnSaveRollComplete = 76,

    /// <summary>
    /// Each wound about to be assigned to a model. Trigger for Tough priority,
    /// hero-last allocation, Deadly multiplication.
    /// Context: wound, defender, candidate models.
    /// </summary>
    Shooting_OnPreApplyWound = 77,

    /// <summary>
    /// Each wound just assigned (or absorbed). Used by rules that react to wounds
    /// landing on specific model types.
    /// Context: wound, target model, was-killed flag.
    /// </summary>
    Shooting_OnPostApplyWound = 78,

    /// <summary>
    /// A unit's last model died, with attribution to the killer. Trigger for
    /// Piercing Frenzy markers and similar "destroyed an enemy" effects.
    /// Context: destroyed unit, killer unit, hook origin (shoot/melee).
    /// </summary>
    Shooting_OnUnitDestroyed = 79,

    /// <summary>
    /// The shoot action is complete. Trigger point for "after shooting" effects like
    /// Harassing's optional 3" move.
    /// Context: unit, target, summary of the shoot.
    /// </summary>
    Shooting_OnPostShoot = 80,

    /// <summary>
    /// Deciding whether a target is within range of a weapon. A capability read: rules emit
    /// <see cref="Definitions.RuleOperation.ApplyRangeModifier"/> to widen the attacker's own weapon range
    /// (Increased Shooting Range) or shrink the range of enemies shooting the bearer (Ranged Shrouding),
    /// folded by <see cref="Dispatch.RangeRuleQueries.EffectiveRange"/>.
    /// Context: attacker (acting unit); the firing weapon and the defending unit ride as participants.
    /// </summary>
    Shooting_OnRangeCheck = 81,

    /// <summary>
    /// The charging unit completes its move into combat, before any strikes. Trigger
    /// for Furious, Thrust, Impact bonuses and Counter resolution.
    /// Context: attacker, defender, who-charged.
    /// </summary>
    Melee_OnChargeContact = 100,

    /// <summary>
    /// The defender has Counter and strikes first. Fires before the normal charging
    /// strikes resolve.
    /// Context: attacker, defender.
    /// </summary>
    Melee_OnCounterTrigger = 101,

    /// <summary>
    /// Wounds have been tallied on both sides, before morale checks. Trigger for
    /// Fear's "+X wounds dealt for winner-check" effect.
    /// Context: attacker, defender, wounds-each.
    /// </summary>
    Melee_OnMeleeResolution = 102,

    /// <summary>
    /// Melee fully resolved (after morale and consolidation). Trigger for "after
    /// melee" effects like Harassing's optional 3" move from being attacked.
    /// Context: both units, outcome.
    /// </summary>
    Melee_OnPostMelee = 103,

    /// <summary>
    /// About to take a morale test, before rolling. Trigger for Courage Aura's +1
    /// to morale tests and other modifiers.
    /// Context: unit, source (melee or wounds), modifier stack.
    /// </summary>
    Morale_OnPreMoraleTest = 120,

    /// <summary>
    /// Morale roll done, outcome about to be applied. Trigger for Fearless's
    /// reroll-as-pass-on-4+ effect.
    /// Context: unit, raw result, source.
    /// </summary>
    Morale_OnMoraleTestComplete = 121,

    /// <summary>
    /// Unit just became Shaken. Used for bookkeeping (set token) and any rules that
    /// react to Shaken application.
    /// Context: unit.
    /// </summary>
    Morale_OnShakenApplied = 122,

    /// <summary>
    /// Unit recovered from Shaken. Source distinguishes normal activation-end recovery
    /// from rule-driven recovery (e.g. Battleborn's start-of-round roll).
    /// Context: unit, source.
    /// </summary>
    Morale_OnShakenCleared = 123,

    /// <summary>
    /// A Caster has declared a spell and targets, before the casting roll. Used by
    /// rules that gate or modify the cast attempt.
    /// Context: caster, spell, tokens spent, targets.
    /// </summary>
    Casting_OnSpellCastAttempt = 130,

    /// <summary>
    /// Friendly Caster units within 18" may contribute tokens for ±1 to the casting
    /// roll, before it resolves.
    /// Context: caster, spell, friendly casters in range.
    /// </summary>
    Casting_OnSpellAssistOffered = 131,

    /// <summary>
    /// The spell cast roll passed; apply the spell's effect to the selected targets.
    /// Context: caster, spell, targets.
    /// </summary>
    Casting_OnSpellResolved = 132,
}
