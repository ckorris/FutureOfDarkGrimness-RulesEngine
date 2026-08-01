using FDG.Rules.Dispatch;
using FDG.StageResolution.Requests;
using FDG.Utilities;
using System.Collections.Concurrent;

namespace FDG.Stages
{
    public class ChooseMeleeWeaponStage : StageBase<ICombatActionContext>
    {
        public StageBinding OnChosen;

        public ChooseMeleeWeaponStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent) : base(gameContext, parent)
        {
            OnChosen = new StageBinding(this);
        }

        public override async Task Enter(ICombatActionContext context)
        {
            if (context.AvailableWeapons.Count == 0)
            {
                throw new Exception($"Available weapon dictionary was empty when entering {nameof(ChooseMeleeWeaponStage)}.");
            }

            IReadOnlyDictionary<Weapon, int> availableWeapons = new ConcurrentDictionary<Weapon, int>(context.AvailableWeapons);
            IReadOnlyDictionary<Weapon, int> unavailableWeapons = new ConcurrentDictionary<Weapon, int>(context.AlreadyUsedWeapons);

            // The option's LABEL is its identity on this wire (StringSelectionRequest carries strings, and
            // the reply is one of them), so two weapons that render identically would be one option and
            // the second would be unpickable. #306: labels are made unique before they go out - see
            // BuildUniqueLabel - which is the protection the old "no protection against identical names"
            // TODO here was asking for. Assigned over a profile-key-ordered pass so the disambiguation is
            // deterministic (#209), then sorted by label for display exactly as before.
            List<(string, Weapon)> validOptions = new List<(string, Weapon)>();
            List<StringSelectionRequest.InvalidOption> invalidOptions = new List<StringSelectionRequest.InvalidOption>();
            HashSet<string> usedLabels = new HashSet<string>(StringComparer.Ordinal);

            // #028: Deadly (wound-multiplier) weapons must strike before the unit's other weapons, so a clump
            // removes whole models before normal wounds spread. While an un-used Deadly weapon is available,
            // the non-Deadly ones are offered as invalid; once all Deadly weapons are spent they free up.
            var attacker = context.AttackingUnit.GetValue();
            HashSet<Weapon> priorityWeapons = availableWeapons.Keys
                .Where(weapon => WoundPriorityQueries.MustResolveFirst(attacker, weapon, GameContext.RuleEvaluator))
                .ToHashSet();
            bool gateNonDeadly = priorityWeapons.Count > 0;

            foreach(KeyValuePair<Weapon, int> kvp in InProfileOrder(availableWeapons))
            {
                string label = BuildUniqueLabel(kvp.Key, kvp.Value, usedLabels);
                if (gateNonDeadly && !priorityWeapons.Contains(kvp.Key))
                {
                    invalidOptions.Add(new StringSelectionRequest.InvalidOption(label,
                        "Must attack with Deadly weapons first."));
                }
                else
                {
                    validOptions.Add((label, kvp.Key));
                }
            }

            foreach(KeyValuePair<Weapon, int> kvp in InProfileOrder(unavailableWeapons))
            {
                invalidOptions.Add(new StringSelectionRequest.InvalidOption(
                    BuildUniqueLabel(kvp.Key, kvp.Value, usedLabels),
                    "The unit has already attacked with this weapon."));
            }

            // #209: the weapon pool is a dictionary keyed by the Weapon reference type, so its
            // enumeration order is identity-hash-dependent - it varied per run, which made multi-
            // weapon units swing in a random order and broke same-seed replay (#193). Present the
            // options in a deterministic order instead; #028's Deadly gating above still decides
            // which of them are valid.
            validOptions.Sort((a, b) => string.CompareOrdinal(a.Item1, b.Item1));
            invalidOptions.Sort((a, b) => string.CompareOrdinal(a.Option, b.Option));

            StringSelectionRequest request = new StringSelectionRequest(context.AttackingUnit.PlayerID(),
                "Choose weapon:", validOptions.Select(option => option.Item1).ToList(), invalidOptions,
                BuildRuleDescriptions(validOptions));

            string chosenWeaponStatsName = await GameContext.PlayerRequester
                .RequestDecision<StringSelectionRequest, string>(request);

            // Labels are unique by construction (#306), so this maps back to exactly one weapon.
            Weapon chosenWeapon = validOptions.First(option => option.Item1 == chosenWeaponStatsName).Item2;

            context.SetAttackWeapon(chosenWeapon, out int weaponCount);
            GameContext.Log($"Chose weapon: {chosenWeapon.Name}. Count: {weaponCount}.");

            await OnChosen.Activate(context);
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
        /// #298: the option label lists a weapon's special rules by NAME only, which is no help at the moment
        /// the choice is made - "Deadly(3)" and "Rending" are the whole reason to prefer one weapon over
        /// another. Each option gets one line per documented rule ("Name - what it does") as its
        /// <see cref="StringSelectionRequest.OptionDescriptions"/> entry; front ends already render that as
        /// subtext (GUI) or an indented line (CLI). Weapons whose rules carry no description are simply
        /// absent from the map - a name with nothing to say adds no information the label doesn't have.
        /// Returns null when no option has anything to describe, which is the common plain-weapon case.
        /// </summary>
        private static Dictionary<string, string>? BuildRuleDescriptions(List<(string Label, Weapon Weapon)> options)
        {
            Dictionary<string, string> descriptions = new Dictionary<string, string>();

            foreach ((string label, Weapon weapon) in options)
            {
                List<string> lines = new List<string>();
                foreach (ResolvedRule rule in weapon.RuleDefinitions)
                {
                    if (string.IsNullOrWhiteSpace(rule.Definition.Description)) continue;
                    lines.Add($"{rule.RequestedName} - {rule.Definition.Description}");
                }

                // Indexer, not Add: labels are unique by construction since #306, but a duplicate key
                // must never be the thing that throws mid-melee.
                if (lines.Count > 0) descriptions[label] = string.Join("\n", lines);
            }

            return descriptions.Count > 0 ? descriptions : null;
        }
    }
}
