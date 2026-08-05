using FDG.Rules.Dispatch;
using FDG.StageResolution.Requests;
using FDG.Utilities;
using System.Collections.Concurrent;

namespace FDG.Stages
{
    public class ChooseMeleeWeaponStage : StageBase<ICombatActionContext>
    {
        /// <summary>
        /// #320: the player held back every weapon that was left, so there is nothing more to swing. Bound to
        /// the same place <see cref="DetermineCanKeepSwingingStage.OnOutOfWeapons"/> goes in each graph
        /// (strike-back offer for the charger's swing, "finished" for the strike-back itself) - the melee
        /// carries on from there exactly as it would after a last swing.
        /// </summary>
        public StageBinding OnNoWeaponsLeftToSwing;

        public StageBinding OnChosen;

        /// <summary>
        /// #320: prefix marking an option as "decline this weapon" rather than "attack with it". Public
        /// because the label is the option's identity on this wire, so anything that must tell the two apart
        /// - the AI resolver, which must never decline its own attacks, and the tests that count weapon rows
        /// - has to test for it rather than guess from ordering. Mirrors
        /// <see cref="ChooseUnitToDeployStage.HoldChoiceFor"/>.
        /// </summary>
        public const string HOLD_BACK_PREFIX = "Hold back: ";

        /// <summary>True if <paramref name="option"/> is a hold-back row rather than a weapon to attack with.</summary>
        public static bool IsHoldBackChoice(string option) =>
            option.StartsWith(HOLD_BACK_PREFIX, StringComparison.Ordinal);

        public ChooseMeleeWeaponStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent) : base(gameContext, parent)
        {
            OnChosen = new StageBinding(this);
            OnNoWeaponsLeftToSwing = new StageBinding(this);
        }

        /// <summary>
        /// #320: repeats within a single entry when the player HOLDS BACK a weapon (it leaves the melee
        /// unswung and the rest are offered again), mirroring <see cref="ChooseRangedAttackStage"/>'s hold-fire
        /// loop. Any other choice exits on the first pass.
        /// </summary>
        public override async Task Enter(ICombatActionContext context)
        {
            if (context.AvailableWeapons.Count == 0)
            {
                throw new Exception($"Available weapon dictionary was empty when entering {nameof(ChooseMeleeWeaponStage)}.");
            }

            while (true)
            {
                bool holdingBack = await OfferWeapons(context);
                if (!holdingBack) return;
            }
        }

        /// <summary>
        /// One pass of the weapon offer. Returns true when a weapon was held back and others remain, i.e.
        /// <see cref="Enter"/> should offer again; false once this stage has routed onward.
        /// </summary>
        private async Task<bool> OfferWeapons(ICombatActionContext context)
        {
            IReadOnlyDictionary<Weapon, int> availableWeapons = new ConcurrentDictionary<Weapon, int>(context.AvailableWeapons);
            IReadOnlyDictionary<Weapon, int> unavailableWeapons = new ConcurrentDictionary<Weapon, int>(context.AlreadyUsedWeapons);

            // The option's LABEL is its identity on this wire (StringSelectionRequest carries strings, and
            // the reply is one of them), so two weapons that render identically would be one option and
            // the second would be unpickable. #306: labels are made unique before they go out - see
            // BuildUniqueLabel - which is the protection the old "no protection against identical names"
            // TODO here was asking for. Assigned over a profile-key-ordered pass so the disambiguation is
            // deterministic (#209), then sorted by label for display exactly as before.
            // #320: an option is now a (label, weapon, holdBack) triple - the same weapon can appear twice,
            // once to swing it and once to decline it. The label is still the option's identity on the wire,
            // and the stage still maps the reply back through this list, so nothing is ever parsed out of it.
            List<(string Label, Weapon Weapon, bool HoldBack)> validOptions =
                new List<(string, Weapon, bool)>();
            List<StringSelectionRequest.InvalidOption> invalidOptions = new List<StringSelectionRequest.InvalidOption>();
            HashSet<string> usedLabels = new HashSet<string>(StringComparer.Ordinal);
            // #321: weapon label -> its hold-back companion, so the two render as one row.
            Dictionary<string, StringSelectionRequest.SecondaryAction> secondaryActions =
                new Dictionary<string, StringSelectionRequest.SecondaryAction>(StringComparer.Ordinal);
            // #336: every WEAPON row's label -> the weapon it shows, so the request can carry each row's
            // special rules structured. Greyed rows are in here too: a weapon the Deadly gate is holding
            // back this pass still has to explain itself, and the player is choosing partly by what the
            // options they CANNOT take say. Hold-backs are not - they are a decline, not a weapon.
            Dictionary<string, Weapon> weaponByLabel = new Dictionary<string, Weapon>(StringComparer.Ordinal);

            // #028: Deadly (wound-multiplier) weapons must strike before the unit's other weapons, so a clump
            // removes whole models before normal wounds spread. While an un-used Deadly weapon is available,
            // the non-Deadly ones are offered as invalid; once all Deadly weapons are spent they free up.
            var attacker = context.AttackingUnit.GetValue();
            HashSet<Weapon> priorityWeapons = availableWeapons.Keys
                .Where(weapon => WoundPriorityQueries.MustResolveFirst(attacker, weapon, GameContext.RuleEvaluator))
                .ToHashSet();
            bool gateNonDeadly = priorityWeapons.Count > 0;

            // #320: only the models within melee range swing, so they are the ones whose once-per-game
            // charges are at stake. Falls back to the whole unit when the in-range set was never computed
            // (no path reaches this stage that way today, but an empty list must not read as "all spent").
            List<IModel> swingingModels = context.InRangeAttackingModels.Count > 0
                ? context.InRangeAttackingModels.Select(binding => (IModel)binding.GetValue()).ToList()
                : attacker.Models;

            // #320/#318: a hold-back that would leave the unit having attacked with NOTHING is not offered.
            // "Must strike with all melee weapons" is the rule; Limited's once-per-game clause is the only
            // implied exception, and it cannot stretch to striking with nothing at all. In every other case
            // declining is free, which is what keeps a Limited weapon declinable: hold it back and swing
            // something else.
            bool nothingHasSwungYet = context.AlreadyUsedWeapons.Count == 0;

            foreach(KeyValuePair<Weapon, int> kvp in InProfileOrder(availableWeapons))
            {
                string label = BuildUniqueLabel(kvp.Key, kvp.Value, usedLabels);
                weaponByLabel[label] = kvp.Key;

                // #320 Limited: a weapon every in-range carrier has already used this game is out.
                if (LimitedRules.IsSpent(swingingModels, kvp.Key))
                {
                    invalidOptions.Add(new StringSelectionRequest.InvalidOption(label,
                        $"Already used this game ({LimitedRules.LimitedRuleName(kvp.Key)})."));
                    continue;
                }

                bool gatedByDeadly = gateNonDeadly && !priorityWeapons.Contains(kvp.Key);
                if (gatedByDeadly)
                {
                    invalidOptions.Add(new StringSelectionRequest.InvalidOption(label,
                        "Must attack with Deadly weapons first."));
                }
                else
                {
                    validOptions.Add((label, kvp.Key, false));
                }

                // #320: the decline, offered beside the weapons that can actually swing this pass. A weapon
                // still waiting on the Deadly gate gets its hold-back entry once that gate clears - which is
                // the pass where declining it would mean anything.
                if (gatedByDeadly) continue;

                // #318: and ONLY for a once-per-game weapon. The melee rule is "models within 2" horizontally
                // and 4" vertically of enemies must strike with all melee weapons" - there is no general
                // opting out of a swing. Limited's "may only be used once per game" is the one clause that
                // implies a choice about WHETHER to use it (otherwise the first melee of the game would
                // always spend it, and "once per game" would mean "in your first fight"), so the exception
                // is scoped to exactly the weapons that carry it.
                if (LimitedRules.LimitedRuleName(kvp.Key) == null) continue;

                string holdBackLabel = HOLD_BACK_PREFIX + label;
                usedLabels.Add(holdBackLabel);
                if (nothingHasSwungYet && availableWeapons.Count == 1)
                {
                    // Even a Limited weapon must swing when it is all the unit has: holding it back would
                    // strike with nothing, and "must strike with all melee weapons" still governs that.
                    invalidOptions.Add(new StringSelectionRequest.InvalidOption(holdBackLabel,
                        "Must strike with all melee weapons - this is the only one left to strike with."));
                }
                else
                {
                    validOptions.Add((holdBackLabel, kvp.Key, true));
                }

                // #321: and say which weapon row OWNS that hold-back, so a resolver can draw it as a second
                // button on the weapon's own row (with the row's hotkey + Shift) instead of a second,
                // apparently unrelated, list entry. Recorded whether the hold-back is available or refused -
                // a greyed companion button carrying "At least one weapon must attack after charging" is
                // still attached to its weapon.
                secondaryActions[label] =
                    new StringSelectionRequest.SecondaryAction(holdBackLabel, "Hold back");
            }

            foreach(KeyValuePair<Weapon, int> kvp in InProfileOrder(unavailableWeapons))
            {
                string usedLabel = BuildUniqueLabel(kvp.Key, kvp.Value, usedLabels);
                weaponByLabel[usedLabel] = kvp.Key;
                invalidOptions.Add(new StringSelectionRequest.InvalidOption(
                    usedLabel, "The unit has already attacked with this weapon."));
            }

            // #209: the weapon pool is a dictionary keyed by the Weapon reference type, so its
            // enumeration order is identity-hash-dependent - it varied per run, which made multi-
            // weapon units swing in a random order and broke same-seed replay (#193). Present the
            // options in a deterministic order instead; #028's Deadly gating above still decides
            // which of them are valid.
            // #320: every weapon is spent-Limited, so the unit has nothing left to swing with. Route on
            // rather than send a request nobody can answer - the CLI/GUI would show an all-disabled list,
            // and the AI's catch-all (ValidOptions[0]) would throw on the empty list.
            if (validOptions.Count == 0)
            {
                GameContext.Log($"{attacker.Name} has no melee weapon left to use - no attacks.");
                await OnNoWeaponsLeftToSwing.Activate(context);
                return false;
            }

            // #320: swing options first, then the hold-backs, each block alphabetical - so the list reads
            // "what I can attack with" before "what I can decline" instead of interleaving the two.
            // The order is also load-bearing for the AI: AiStringSelectionResolver falls through to
            // ValidOptions[0] here, and a hold-back sorted to the top would have it decline its own attacks
            // (the trap AiStringSelectionResolverTests already pins for the Ambush prompt).
            validOptions.Sort((a, b) => a.HoldBack != b.HoldBack
                ? a.HoldBack.CompareTo(b.HoldBack)
                : string.CompareOrdinal(a.Label, b.Label));
            invalidOptions.Sort((a, b) => string.CompareOrdinal(a.Option, b.Option));

            StringSelectionRequest request = new StringSelectionRequest(context.AttackingUnit.PlayerID(),
                "Choose weapon:", validOptions.Select(option => option.Label).ToList(), invalidOptions,
                BuildRuleDescriptions(validOptions),
                secondaryActions: secondaryActions.Count > 0 ? secondaryActions : null,
                displayName: "Choosing a Melee Weapon",
                optionRules: BuildOptionRules(weaponByLabel));

            string chosenWeaponStatsName = await GameContext.PlayerRequester
                .RequestDecision<StringSelectionRequest, string>(request);

            // Labels are unique by construction (#306), so this maps back to exactly one option.
            (string _, Weapon chosenWeapon, bool holdBack) =
                validOptions.First(option => option.Label == chosenWeaponStatsName);

            if (holdBack)
            {
                return await HoldBack(context, chosenWeapon);
            }

            context.SetAttackWeapon(chosenWeapon, out int weaponCount);
            GameContext.Log($"Chose weapon: {chosenWeapon.Name}. Count: {weaponCount}.");

            // #320 Limited: choosing commits the weapon to swing, so spend it now - on the models that
            // actually swing it, never on a carrier standing out of melee range. Mirrors
            // ChooseRangedAttackStage, including saying so: this is the one irreversible thing here.
            string? limitedRule = LimitedRules.LimitedRuleName(chosenWeapon);
            LimitedRules.MarkFired(swingingModels, chosenWeapon);
            if (limitedRule != null)
            {
                GameContext.Log($"{chosenWeapon.Name} is {limitedRule} - spent for the rest of the game.");
            }

            await OnChosen.Activate(context);
            return false;
        }

        /// <summary>
        /// #320: decline one weapon for this melee. It leaves the pool unswung, so its once-per-game use is
        /// kept - and a declined Deadly+Limited weapon stops gating the unit's other weapons (the gate above
        /// reads the AVAILABLE pool). #318: only Limited weapons are ever offered here, so "declining" never
        /// means dodging an ordinary swing. Returns true when weapons remain and the offer should repeat.
        /// </summary>
        private async Task<bool> HoldBack(ICombatActionContext context, Weapon weapon)
        {
            // The last weapon with nothing swung is never offered as a hold-back, so reaching here with
            // nothing left means the unit HAS already swung - and ending the attack early is the kind of
            // irreversible choice that gets a confirmation (user sign-off), same as "Done shooting".
            bool wouldEndTheAttack = context.AvailableWeapons.Count == 1;
            if (wouldEndTheAttack && !await ConfirmEndingTheAttack(context, weapon))
            {
                return true; // changed their mind - offer the weapons again
            }

            context.DeclineWeapon(weapon);
            GameContext.Log($"Holding back {weapon.Name} - not used this melee.");

            if (context.AvailableWeapons.Count > 0) return true;

            GameContext.Log("No melee weapons left to swing.");
            await OnNoWeaponsLeftToSwing.Activate(context);
            return false;
        }

        /// <summary>
        /// #320: the melee counterpart of the "Done shooting" confirmation - it fires only on the hold-back
        /// that ENDS the attack, naming what goes unswung and what a once-per-game weapon keeps by it.
        /// Per-weapon holds with swings still to come stay silent, exactly as Hold fire does.
        /// </summary>
        private async Task<bool> ConfirmEndingTheAttack(ICombatActionContext context, Weapon weapon)
        {
            string? limitedRule = LimitedRules.LimitedRuleName(weapon);
            string keeps = limitedRule != null
                ? $" It keeps its {limitedRule} once-per-game use."
                : "";

            // defaultAnswer: true - the player had to pick the hold-back option to get here, so an EOF or
            // timed-out answer honours that rather than quietly swinging the weapon they just declined.
            YesNoRequest confirm = new YesNoRequest(context.AttackingUnit.PlayerID(),
                $"Hold back {weapon.Name}? It is the last weapon left, so " +
                $"{context.AttackingUnit.GetValue().Name} makes no further attacks this melee.{keeps}",
                defaultAnswer: true);

            return await GameContext.PlayerRequester
                .RequestDecision<YesNoRequest, bool>(confirm);
        }

        /// <summary>
        /// #306: pool dictionaries are keyed by the Weapon reference type, so their enumeration order is
        /// identity-hash-dependent (the #209 defect). Walking them in profile-key order makes the pass
        /// deterministic, which matters because <see cref="BuildUniqueLabel"/> hands out its
        /// disambiguating suffixes in the order it sees things.
        /// </summary>
        private static IEnumerable<KeyValuePair<Weapon, int>> InProfileOrder(
            IReadOnlyDictionary<Weapon, int> weapons)
        {
            return weapons.OrderBy(kvp => WeaponProfileKey.For(kvp.Key), StringComparer.Ordinal);
        }

        /// <summary>
        /// #306: the weapon's datasheet line, made unique across the whole request. Normally it already is
        /// - a rule's requested name carries its argument ("Deadly(3)"), so profiles that differ read
        /// differently - but nothing GUARANTEED it, and this label is the option's identity on the wire:
        /// two weapons rendering the same string collapse into one option, and picking it binds whichever
        /// was listed first. A numbered suffix keeps the mapping one-to-one in that last-resort case.
        /// </summary>
        private static string BuildUniqueLabel(Weapon weapon, int count, HashSet<string> usedLabels)
        {
            string label = weapon.GetWeaponNameAndStats(count);
            if (usedLabels.Add(label)) return label;

            for (int suffix = 2; ; suffix++)
            {
                string candidate = $"{label} #{suffix}";
                if (usedLabels.Add(candidate)) return candidate;
            }
        }

        /// <summary>
        /// Free-form prose about what taking an option COSTS OR BUYS. #336 moved the weapon rules out of
        /// here into <see cref="BuildOptionRules"/>, so today only the #320 hold-back line remains: a
        /// hold-back explains what declining buys - but only when there is something to buy, i.e. a
        /// once-per-game use to keep. "Do not attack with Blade" would just restate the label, and the plain
        /// weapon menu must stay description-free (#298). #321: it does NOT repeat the weapon's rules, which
        /// the weapon's own row already carries right above it. Returns null when no option has anything to
        /// say, which is the common plain-weapon case.
        /// </summary>
        private static Dictionary<string, string>? BuildRuleDescriptions(
            List<(string Label, Weapon Weapon, bool HoldBack)> options)
        {
            Dictionary<string, string> descriptions = new Dictionary<string, string>();

            foreach ((string label, Weapon weapon, bool holdBack) in options)
            {
                if (!holdBack) continue;

                string? limitedRule = LimitedRules.LimitedRuleName(weapon);
                if (limitedRule == null) continue;

                // Indexer, not Add: labels are unique by construction since #306, but a duplicate key
                // must never be the thing that throws mid-melee.
                descriptions[label] = $"Keeps its {limitedRule} once-per-game use for a later melee.";
            }

            return descriptions.Count > 0 ? descriptions : null;
        }

        /// <summary>
        /// #298/#336: the option label lists a weapon's special rules by NAME only, which is no help at the
        /// moment the choice is made - "Deadly(3)" and "Rending" are the whole reason to prefer one weapon
        /// over another. Each swing option carries its rules structured (name + what it does) so a front end
        /// can underline the names where they already sit in the label and hover the text (GUI), or print
        /// them as indented lines (CLI). #298 shipped this pre-formatted into one prose blob, which left the
        /// GUI no way to find where a rule name started - so it could only dump the whole thing under the
        /// button, the inconsistency #336 exists to fix.
        /// <para>Undocumented rules are included: the front end says they are not enforced, which is worth
        /// knowing before swinging. Greyed rows are included too (<paramref name="weaponByLabel"/> holds
        /// them), since an option the player cannot take is still part of the comparison. Hold-backs are
        /// absent - a decline is not a weapon, and its owner's row right above carries the rules (#321).
        /// Returns null when no option has a rule at all.</para>
        /// </summary>
        private static Dictionary<string, List<StringSelectionRequest.OptionRule>>? BuildOptionRules(
            Dictionary<string, Weapon> weaponByLabel)
        {
            Dictionary<string, List<StringSelectionRequest.OptionRule>> optionRules =
                new Dictionary<string, List<StringSelectionRequest.OptionRule>>(StringComparer.Ordinal);

            foreach (KeyValuePair<string, Weapon> kvp in weaponByLabel)
            {
                List<StringSelectionRequest.OptionRule> rules =
                    new List<StringSelectionRequest.OptionRule>();

                // Attachment order, which is also the order GetWeaponNameAndStats appended the names to the
                // label in - front ends walk the label and these in lockstep to find each name.
                foreach (ResolvedRule rule in kvp.Value.RuleDefinitions)
                {
                    string? description = string.IsNullOrWhiteSpace(rule.Definition.Description)
                        ? null
                        : rule.Definition.Description;
                    rules.Add(new StringSelectionRequest.OptionRule(rule.RequestedName, description));
                }

                if (rules.Count > 0) optionRules[kvp.Key] = rules;
            }

            return optionRules.Count > 0 ? optionRules : null;
        }
    }
}
