using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Tests
{
    // #433 - the OPR 3.6 "Enhanced X" family as authored in FdgRaylib/Assets/Books/GdfRuleSupplement.json and
    // AofRuleSupplement.json: every Enhanced X is the ADDITIVE DELTA on top of X (Option B, signed off 2026-10-30), never
    // a re-authored total, and X itself is never edited. Hand-built because the engine suite cannot read the app's
    // supplements - the RangeConditional36Definitions convention; the app-side Enhanced36ShippedDataTests asserts the
    // authored JSON has exactly these entries (with the 3.6.0 text), so the engine tests and the data cannot drift apart.
    // One shape stands for each family; the siblings named in each comment share it entry for entry.
    //
    // Every entry is gated on the bearer having X (Condition.UnitHasRule, which since #433 reads the unit list, grant
    // tokens, living models and their weapons), so an Enhanced rule without its base is inert.
    internal static class Enhanced36Definitions
    {
        public static Condition Has(string x) => new Condition.UnitHasRule(x);
        private static Condition MostModels => new Condition.MostModelsHaveThisRule();
        public static Condition Attacked => new Condition.AttackedFromOver(ShootInches: 9f, ChargeInches: 6f);
        public static Condition Attacking => new Condition.AttackingFromOver(ShootInches: 9f, ChargeInches: 6f);
        /// <summary>"Was shot or charged at all" - the ward family's complement keeps "shooting/charging wounds".</summary>
        public static Condition ShotOrCharged => new Condition.AttackedFromOver(ShootInches: 0f, ChargeInches: 0f);
        private static Condition Not(Condition c) => new Condition.Not(c);
        private static Condition And(Condition l, Condition r) => new Condition.And(l, r);

        private static SpecialRuleDefinition Unit(string name, params HookEntry[] passive) =>
            new(name, passive, System.Array.Empty<ActivatedAbility>(), ERuleScope.Unit, Valence: EValence.Positive,
                Description: $"#433 test copy of {name}");

        // --- B1: sums (movement, range) ---------------------------------------------------------------------------

        /// <summary>Enhanced Lustbound; also Enhanced Scurry, Enhanced Agile. "+2" on Advance and +4" on Rush/Charge
        /// (instead of only +1" and +2")" - the same three entries as the base, which the movement sink sums.</summary>
        public static SpecialRuleDefinition Move(string x = "Lustbound") => Unit($"Enhanced {x}",
            MoveBonus(x, EActionType.Advance, 1f), MoveBonus(x, EActionType.Rush, 2f), MoveBonus(x, EActionType.Charge, 2f));

        private static HookEntry MoveBonus(string x, EActionType action, float inches) =>
            new(EHookID.Movement_OnMoveActionDeclared, And(new Condition.ActionTypeIs(action), Has(x)),
                new Effect.MovementBonus(action, inches, MinResultInches: 0f), ELifetime.ThisActivation);

        /// <summary>Enhanced Darkborn; also Shadowborn, Shrouded, Wild Veil. "-8"/-4" range to a min. of 6" (instead of
        /// only -4"/-2")" - a second {-4, 6, -2}; range ops sum and the floor is the largest among reductions.</summary>
        public static SpecialRuleDefinition Shrink(string x = "Darkborn") => Unit($"Enhanced {x}",
            new HookEntry(EHookID.Shooting_OnRangeCheck, And(Has(x), MostModels),
                new Effect.RangeModifier(Delta: -4, MinResultInches: 6, ChargeDelta: -2), ELifetime.ThisActivation,
                ERuleSeat.Subject));

        /// <summary>Enhanced Highborn; also Extra Reach, Royal Legion. "+8"/+4" range (instead of only +4"/+2")".</summary>
        public static SpecialRuleDefinition Reach(string x = "Highborn") => Unit($"Enhanced {x}",
            new HookEntry(EHookID.Shooting_OnRangeCheck, Has(x),
                new Effect.RangeModifier(Delta: 4, MinResultInches: 0, ChargeDelta: 2), ELifetime.ThisActivation));

        // --- B2: keep-best folds (AP, wound ignore, Shaken recovery, save re-roll, post-combat move) ------------------

        /// <summary>Enhanced Guardian; also Fortified, Ossified, Warden. "AP(-2) (instead of only AP(-1))" - a second
        /// reduceArmorPenetration 1; the save stage sums the reductions.</summary>
        public static SpecialRuleDefinition ApMinusTwo(string x = "Guardian") => Unit($"Enhanced {x}",
            new HookEntry(EHookID.Shooting_OnHitRollComplete, And(Has(x), MostModels),
                new Effect.ReduceArmorPenetration(1), ELifetime.ThisAttack, ERuleSeat.Subject));

        /// <summary>Enhanced Plaguebound; also Self-Repair, Protected, Cursed Undead. "ignore wounds on rolls of 5-6
        /// (instead of only on 6+)" - a 5+ entry; the wound-ignore sink keeps the best threshold.</summary>
        public static SpecialRuleDefinition IgnoreFivePlus(string x = "Plaguebound") => Unit($"Enhanced {x}",
            new HookEntry(EHookID.Shooting_OnSaveRollComplete, And(Has(x), MostModels),
                new Effect.IgnoreWoundOnRoll(MinRoll: 5), ELifetime.ThisAttack, ERuleSeat.Subject));

        /// <summary>Enhanced Steadfast; also Vale Oath. "stops being Shaken on rolls of 3+ (instead of only on 4+)" - a
        /// 3+ entry; TokenClearRollSink keeps the best threshold, one roll.</summary>
        public static SpecialRuleDefinition RecoverThreePlus(string x = "Steadfast") => Unit($"Enhanced {x}",
            new HookEntry(EHookID.Round_OnRoundStart,
                And(Has(x), And(new Condition.TokenPresent(TokenType.Shaken), MostModels)),
                new Effect.ClearTokenOnRoll(TokenType.Shaken, MinRoll: 3), ELifetime.ThisRound));

        /// <summary>Enhanced Mischievous; also Scrapper, Vicious, Bestial. "re-roll successful unmodified Defense results of
        /// 5-6 (instead of only results of 6)" - the 5+ band; RerollSink keeps the widest. Unit scope (the base is a
        /// weapon rule re-homed onto the weapons; the gate reads them).</summary>
        public static SpecialRuleDefinition RerollFivePlus(string x = "Mischievous") => Unit($"Enhanced {x}",
            new HookEntry(EHookID.Shooting_OnSaveRollComplete, Has(x),
                new Effect.Reroll(ERollKind.Save, new RerollCondition.OnUnmodifiedValue(MinValue: 5)), ELifetime.ThisAttack));

        /// <summary>Enhanced Harassing; also Guerrilla, Hit &amp; Run. "may move by up to 6" (instead of only 3")" - a 6"
        /// move beside the base's 3"; PostCombatMoveGate coalesces the family's moves into ONE at the largest budget.</summary>
        public static SpecialRuleDefinition PostCombatSix(string x = "Harassing") => Unit($"Enhanced {x}",
            new HookEntry(EHookID.Shooting_OnPostShoot, And(Has(x), MostModels),
                new Effect.TriggeredMove(MaxInches: 6f, IsOptional: true), ELifetime.ThisAttack),
            new HookEntry(EHookID.Melee_OnPostMelee, And(Has(x), MostModels),
                new Effect.TriggeredMove(MaxInches: 6f, IsOptional: true), ELifetime.ThisAttack));

        // --- B3: the activation move ---------------------------------------------------------------------------------

        /// <summary>Enhanced Wolfborn; also Nimble, Rapid Blink, Wave-Step. "may move by up to 2D3" (instead of only D3")"
        /// - a second D3 move entry; ActivationStartStage sums the reposition entries into one move (#434 D8).</summary>
        public static SpecialRuleDefinition TwoD3(string x = "Wolfborn") => Unit($"Enhanced {x}",
            new HookEntry(EHookID.Activation_OnActivationStart, And(Has(x), MostModels),
                new Effect.RepositionAtActivation(new DiceExpression.D3(), PlusInches: 0, Move: true),
                ELifetime.ThisActivation));

        // --- C: the "always ... (instead of only when over 9"/6")" complements ------------------------------------------
        // Each is the base's effect under the COMPLEMENT of the base's range gate, so the two arms are disjoint.

        /// <summary>Enhanced Changebound; also Machine-Fog, Screened, Empyrean Spirit.</summary>
        public static SpecialRuleDefinition MinusHit(string x = "Changebound") => Unit($"Enhanced {x}",
            new HookEntry(EHookID.Shooting_OnHitRollModifier, And(Has(x), And(Not(Attacked), MostModels)),
                new Effect.RollModifier(ERollKind.Hit, -1), ELifetime.ThisAttack, ERuleSeat.Subject));

        /// <summary>Enhanced Guarded; also Sturdy.</summary>
        public static SpecialRuleDefinition PlusDefense(string x = "Guarded") => Unit($"Enhanced {x}",
            new HookEntry(EHookID.Shooting_OnHitRollComplete, And(Has(x), And(Not(Attacked), MostModels)),
                new Effect.RollModifier(ERollKind.Save, +1), ELifetime.ThisAttack, ERuleSeat.Subject));

        /// <summary>Enhanced Knightborn; also Warding, Angelic Blessing. "always ignore shooting/charging wounds on 6+" -
        /// still only shooting/charging wounds (a strike-back's and a spell's are neither), at any distance.</summary>
        public static SpecialRuleDefinition Ward(string x = "Knightborn") => Unit($"Enhanced {x}",
            new HookEntry(EHookID.Shooting_OnSaveRollComplete,
                And(Has(x), And(And(ShotOrCharged, Not(Attacked)), MostModels)),
                new Effect.IgnoreWoundOnRoll(MinRoll: 6), ELifetime.ThisAttack, ERuleSeat.Subject));

        /// <summary>Enhanced Hive Bond; also Hold the Line, Valiant.</summary>
        public static SpecialRuleDefinition Morale(string x = "Hive Bond") => Unit($"Enhanced {x}",
            new HookEntry(EHookID.Morale_OnPreMoraleTest, And(Has(x), And(Not(Attacked), MostModels)),
                new Effect.RollModifier(ERollKind.Morale, +1), ELifetime.ThisActivation));

        /// <summary>Enhanced Devout; also Ferocious, Brutal, Lucky.</summary>
        public static SpecialRuleDefinition ExtraHit(string x = "Devout") => Unit($"Enhanced {x}",
            new HookEntry(EHookID.Shooting_OnHitRollComplete,
                And(Has(x), And(Not(Attacking), new Condition.UnmodifiedRollEquals(6))),
                new Effect.AddExtraHit(OnRollValue: 6, Count: 1), ELifetime.ThisAttack));

        /// <summary>Enhanced Havocbound; also Piercing Warrior.</summary>
        public static SpecialRuleDefinition ApPlusOne(string x = "Havocbound") => Unit($"Enhanced {x}",
            new HookEntry(EHookID.Shooting_OnHitRollComplete, And(Has(x), Not(Attacking)),
                new Effect.RollModifier(ERollKind.Save, -1), ELifetime.ThisAttack));

        /// <summary>Enhanced Infected; also Warbound, Violent, Destroyer. Unit scope: the base is a weapon rule re-homed
        /// onto the weapons, and the gate reads them.</summary>
        public static SpecialRuleDefinition ExtraWound(string x = "Infected") => Unit($"Enhanced {x}",
            new HookEntry(EHookID.Shooting_OnSaveRollComplete, And(Has(x), Not(Attacking)),
                new Effect.AddExtraWound(OnRollValue: 1, Count: 1), ELifetime.ThisAttack));

        /// <summary>Enhanced Versatile Warrior; also Watchborn, Vinci Tech. The pick's one-activation helper is a grant
        /// token, which UnitHasRule reads.</summary>
        public static SpecialRuleDefinition Pick(string x = "Versatile Warrior") => Unit($"Enhanced {x}",
            new HookEntry(EHookID.Shooting_OnHitRollComplete, And(Has($"{x} (Piercing)"), Not(Attacking)),
                new Effect.RollModifier(ERollKind.Save, -1), ELifetime.ThisAttack),
            new HookEntry(EHookID.Shooting_OnHitRollModifier, And(Has($"{x} (Precision)"), Not(Attacking)),
                new Effect.RollModifier(ERollKind.Hit, +1), ELifetime.ThisAttack));

        /// <summary>Enhanced Targeting Visor; also Precision Marksman, Buccaneer. "always gets +1 to hit rolls (instead of
        /// only when shooting over 9" away)" - the complement of the base's whole gate (D5).</summary>
        public static SpecialRuleDefinition PlusHit(string x = "Targeting Visor") => Unit($"Enhanced {x}",
            new HookEntry(EHookID.Shooting_OnHitRollModifier,
                And(Has(x), Not(And(Not(new Condition.IsMelee()), new Condition.DistanceGreaterThan(9f)))),
                new Effect.RollModifier(ERollKind.Hit, +1), ELifetime.ThisAttack));

        // --- D: extra attacks on 5-6 ---------------------------------------------------------------------------------

        /// <summary>Enhanced Bloodborn; also Clan Warrior, Primal, Predator, Royal Warrior. "extra attacks on successful
        /// unmodified hit results of 5-6 (instead of only on 6)" - the 5 face, gated on the 5 having HIT
        /// (Condition.UnmodifiedRollHits); the base's one-extra-hit approximation of "+1 attack" (#434 D7) inherited.</summary>
        public static SpecialRuleDefinition HitsOnFive(string x = "Bloodborn") => Unit($"Enhanced {x}",
            new HookEntry(EHookID.Shooting_OnHitRollComplete, And(Has(x), new Condition.UnmodifiedRollHits(5)),
                new Effect.AddExtraHit(OnRollValue: 5, Count: 1), ELifetime.ThisAttack));

        // --- E: the wrappers -----------------------------------------------------------------------------------------

        /// <summary>Enhanced X Aura: "This model and its unit get Enhanced X."</summary>
        public static SpecialRuleDefinition Aura(string x) => Unit($"Enhanced {x} Aura",
            new HookEntry(EHookID.Lifecycle_OnUnitCreated, new Condition.Always(), new Effect.Aura($"Enhanced {x}"),
                ELifetime.UntilEndOfGame));

        /// <summary>Enhanced X Buff: once per activation, before attacking, a friendly unit within 12" gets Enhanced X once.</summary>
        public static SpecialRuleDefinition Buff(string x) => new($"Enhanced {x} Buff",
            System.Array.Empty<HookEntry>(),
            new[]
            {
                new ActivatedAbility(EHookID.Activation_OnBeforeAttackAction, new Cost.OncePerActivation(),
                    new TargetSelector(RangeInches: 12f, MinCount: 1, MaxCount: 1, ETargetAffinity.Friend,
                        RequireLineOfSight: false),
                    new Effect.AddRule($"Enhanced {x}", ELifetime.NextTrigger),
                    new Condition.Always()),
            },
            ERuleScope.Unit, Valence: EValence.Positive, Description: $"#433 test copy of Enhanced {x} Buff");
    }
}
