using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using System.Diagnostics;

namespace FDG.GameModel
{
    /// <summary>
    /// The world-building steps a new game runs at launch — teams, the shared rule resolver, and each
    /// player's army (units + models registered in the store, rules attached, heroes merged, spells
    /// resolved). Extracted from <see cref="FDGServer"/>'s constructor path (#167) so the scenario
    /// compiler (<see cref="FDG.SaveLoad.ScenarioCompiler"/>) can build the exact same world without
    /// launching a server; FDGServer delegates here, so there is one implementation.
    /// <para>
    /// Also holds the one step a RESUME needs from this file: <see cref="RestoreArmyRuleData"/>, which
    /// redoes the army-file-derived half of <see cref="CreateArmy"/> from data persisted in the save (#095).
    /// </para>
    /// </summary>
    public static class GameBootstrap
    {
        /// <summary>Groups the slots by team number and registers one <see cref="TeamData"/> per team.</summary>
        public static void AddTeams(PlayerSlot[] playerSlots, IReadWriteableGameDataStore gameDataStore)
        {
            Dictionary<int, List<PlayerID>> teams = new Dictionary<int, List<PlayerID>>();

            //This assumes team numbers are unique already. But if we ever use -1 for
            //those not on a team or something like that, there will be issues.

            for (int i = 0; i < playerSlots.Length; i++)
            {
                PlayerSlot slot = playerSlots[i];

                int teamSlot = slot.TeamNumber;
                if (teams.ContainsKey(teamSlot) == false)
                {
                    teams.Add(teamSlot, new List<PlayerID>());
                }

                teams[teamSlot].Add(slot.PlayerID);
            }

            foreach (KeyValuePair<int, List<PlayerID>> kvp in teams)
            {
                TeamData teamData = new TeamData(kvp.Key, kvp.Value);
                DataReference teamReference = gameDataStore.Create(teamData);
            }
        }

        /// <summary>
        /// Builds the shared in-game rule resolver — the core catalog plus every army's embedded (#059)
        /// rule definitions (registered after core so a data template can override a core rule by name).
        /// The evaluator needs it to read granted-rule tokens back to their definitions (auras /
        /// "gains rule X" buffs).
        /// </summary>
        public static RuleResolver BuildRuleResolver(PlayerSlot[] playerSlots)
        {
            RuleResolver ruleResolver = CoreRuleCatalog.CreateResolver();

            for (int i = 0; i < playerSlots.Length; i++)
            {
                ArmyListRuleResolution.RegisterEmbeddedDefinitions(ruleResolver, playerSlots[i].ArmyListFile);
            }

            return ruleResolver;
        }

        /// <summary>
        /// The resume counterpart to the army-load half of <see cref="CreateArmy"/> (#095). Reads back the
        /// blob each <see cref="ArmyData"/> persisted at army load and redoes the two things a resume
        /// otherwise loses: registers every army's embedded (#059) rule definitions into the shared
        /// resolver, then re-resolves each army's spell list (#033) onto it.
        /// <para>
        /// Definitions are registered for ALL armies before ANY spells resolve — a spell's
        /// <c>DealHits.WithRules</c> names weapon rules that may be embedded — and after
        /// <see cref="BuildRuleResolver"/>'s core-then-slot pass, so a persisted definition wins over a
        /// same-named core rule exactly as it did in the session that wrote the save.
        /// </para>
        /// <para>
        /// Not re-validated: these definitions already passed <see cref="ArmyListRuleResolution"/>'s
        /// validation at army load in that session, and a resume must not be able to fail on data the
        /// original game accepted.
        /// </para>
        /// </summary>
        public static void RestoreArmyRuleData(RuleResolver ruleResolver, IReadWriteableGameDataStore gameDataStore)
        {
            if (!gameDataStore.IsTypeAssigned<ArmyData>())
            {
                return;
            }

            List<ArmyData> armies = gameDataStore.GetAllValues<ArmyData>().ToList();
            List<(ArmyData Army, ArmyRuleDataPersistence.PersistedArmyRuleData Data)> restored = new();

            foreach (ArmyData army in armies)
            {
                ArmyRuleDataPersistence.PersistedArmyRuleData? data = army.RestoreRuleData();
                if (data == null)
                {
                    continue;
                }

                foreach (SpecialRuleDefinition definition in data.RuleDefinitions)
                {
                    ruleResolver.RegisterOrReplace(definition);
                }

                restored.Add((army, data));
            }

            foreach ((ArmyData army, ArmyRuleDataPersistence.PersistedArmyRuleData data) in restored)
            {
                army.SetSpells(ArmyListSpellResolution.ResolveSpells(data.Spells, ruleResolver));
            }
        }

        /// <summary>
        /// Builds one player's army from its list file: units (models registered by the UnitData ctor),
        /// unit-level rules attached, #006 hero joins resolved within the army, surviving units + the
        /// <see cref="ArmyData"/> registered in the store, and the army's #033 spell list resolved.
        /// </summary>
        public static void CreateArmy(PlayerID playerID, ArmyListFile armyListFile,
            IReadWriteableGameDataStore gameDataStore, IRuleResolver ruleResolver)
        {
            // Build every unit (with rules attached) first, paired with its army-list entry, so #006 Hero
            // joins can be resolved within this army before anything is registered. Models are created in
            // the store by the UnitData constructor; only the UnitData registration is deferred.
            List<(UnitFileEntry Entry, UnitData Unit)> built = new(armyListFile.Units.Count);
            foreach (UnitFileEntry unitEntry in armyListFile.Units)
            {
                UnitData unitData = new UnitData(playerID, unitEntry, gameDataStore, ruleResolver,
                    armyListFile.DefaultRangedEffectSet, armyListFile.DefaultMeleeEffectSet);
                AttachRulesFromArmyList(unitData, unitEntry, ruleResolver);
                built.Add((unitEntry, unitData));
            }

            // Merge heroes into their declared host units; survivors are the units that deploy on their own
            // (hosts + non-joining units). A merged hero is absorbed into its host and never registered.
            // The per-army call naturally enforces the rule's "part of one multi-model unit" being from the
            // same army. Tough still lands per-unit later in the creation-rules loop, hero-aware.
            IReadOnlyList<UnitData> survivors = HeroJoinResolver.Apply(built, message => Debug.WriteLine(message));

            List<DataBinding<UnitData>> unitBindings = new List<DataBinding<UnitData>>(survivors.Count);
            foreach (UnitData unitData in survivors)
            {
                DataReference unitDataReference = gameDataStore.Create(unitData);
                DataBinding<UnitData> unitBinding = gameDataStore.GetDataBinding<UnitData>(unitDataReference);
                unitBindings.Add(unitBinding);
            }

            ArmyData armyData = new ArmyData(playerID, unitBindings);
            // #033: resolve the army's embedded spell list (any Caster(X) unit can cast these). Done here
            // where the rule resolver is live, so a damage spell's weapon rules are pre-resolved for the
            // cast stage.
            armyData.SetSpells(ArmyListSpellResolution.ResolveSpells(armyListFile, ruleResolver));

            // #095: keep the two army-file lists this method just consumed, so RestoreArmyRuleData can
            // redo both halves on a resume — where this method doesn't run and the file is vestigial.
            armyData.PersistRuleData(armyListFile.RuleDefinitions, armyListFile.Spells);

            DataReference armyDataReference = gameDataStore.Create(armyData);
        }

        //Resolves each special rule named on the army-list entry against the rule registry and
        //attaches the resolved #042 definition to the unit. A valid-but-not-yet-implemented core
        //rule (one with no definition in the catalog) is skipped with a warning so partial armies
        //still load and the rules that ARE implemented still fire. Weapon-level rules attach inside
        //the UnitData constructor via the same ArmyListRuleResolution helper.
        //
        //#197 slice 0: a weapon-scoped rule CAN legitimately be named here. Wargear is a rule-bundle
        //flattened into the unit's rule list at compile time (ListCompiler), so an item that grants a
        //weapon rule to the whole unit ("Toxic Cysts: Bane in Melee") arrives at unit scope. Those
        //attach to every weapon the unit carries; the rules' own isMelee / not(isMelee) gates then pick
        //the right weapons at dispatch, which reads the firing weapon's rules (RollToHitStage passes it
        //into RuleParticipant.Actor). Wargear that names a specific weapon ("upgrade all Pulse Rifles
        //with a Drone Controller") never reaches this path — ListCompiler lands those rules directly on
        //the named weapon, so a Reliable rifle does not make its owner's melee taser hit on 2+.
        private static void AttachRulesFromArmyList(UnitData unitData, UnitFileEntry unitEntry, IRuleResolver ruleResolver)
        {
            foreach (SpecialRuleEntry ruleEntry in unitEntry.SpecialRules)
            {
                ResolvedRule? resolved = ArmyListRuleResolution.ResolveAnyScope(
                    ruleResolver, ruleEntry, $"unit '{unitData.Name}'");

                if (resolved == null)
                {
                    continue;
                }

                if (resolved.Definition.Scope == ERuleScope.Unit)
                {
                    unitData.AttachRuleDefinition(resolved);
                    continue;
                }

                AttachToEveryWeapon(unitData, ruleEntry, resolved);
            }

            // #035: every unit carries the engine-internal Disembark + Embark abilities (slice C/D), each
            // gated before it surfaces (Disembark by the EmbarkedIn token, Embark by an engine
            // transport-in-range check in ChooseActionStage). Attached here so they ride the normal
            // army-load rule lifecycle and are restored by the #094 resume rehydration.
            unitData.AttachRuleDefinition(new ResolvedRule(CoreRuleCatalog.DisembarkRuleName, CoreRuleCatalog.Disembark));
            unitData.AttachRuleDefinition(new ResolvedRule(CoreRuleCatalog.EmbarkRuleName, CoreRuleCatalog.Embark));
        }

        //Re-homes a unit-level weapon rule onto each of the unit's weapons. ResolvedRule is immutable, so
        //one instance is shared across them (as the UnitData constructor already does across a weapon
        //entry's quantity). A unit with no weapons has nowhere to put it — warn rather than lose it silently.
        private static void AttachToEveryWeapon(UnitData unitData, SpecialRuleEntry ruleEntry, ResolvedRule resolved)
        {
            List<Weapon> weapons = ((IUnit)unitData).AllWeapons(_ => true);

            if (weapons.Count == 0)
            {
                RuleDiagnostics.Warn(
                    $"Skipping weapon rule '{ruleEntry.PrintableName}' on unit '{unitData.Name}': " +
                    "it is granted at unit level but the unit carries no weapons to attach it to.");
                return;
            }

            foreach (Weapon weapon in weapons)
            {
                weapon.AttachRuleDefinition(resolved);
            }
        }
    }
}
