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

            //TODO: Since we don't store weapons in bindings, we're hackedly using their stats names, which have no
            //protection against identical names.
            List<(string, Weapon)> validOptions = new List<(string, Weapon)>();
            List<StringSelectionRequest.InvalidOption> invalidOptions = new List<StringSelectionRequest.InvalidOption>();

            // #028: Deadly (wound-multiplier) weapons must strike before the unit's other weapons, so a clump
            // removes whole models before normal wounds spread. While an un-used Deadly weapon is available,
            // the non-Deadly ones are offered as invalid; once all Deadly weapons are spent they free up.
            var attacker = context.AttackingUnit.GetValue();
            HashSet<Weapon> priorityWeapons = availableWeapons.Keys
                .Where(weapon => WoundPriorityQueries.MustResolveFirst(attacker, weapon, GameContext.RuleEvaluator))
                .ToHashSet();
            bool gateNonDeadly = priorityWeapons.Count > 0;

            foreach(KeyValuePair<Weapon, int> kvp in availableWeapons)
            {
                string label = kvp.Key.GetWeaponNameAndStats(kvp.Value);
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

            foreach(KeyValuePair<Weapon, int> kvp in unavailableWeapons)
            {
                invalidOptions.Add(new StringSelectionRequest.InvalidOption(kvp.Key.GetWeaponNameAndStats(kvp.Value),
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

            Weapon chosenWeapon = validOptions.First(option => option.Item1 == chosenWeaponStatsName).Item2;

            context.SetAttackWeapon(chosenWeapon, out int weaponCount);
            GameContext.Log($"Chose weapon: {chosenWeapon.Name}. Count: {weaponCount}.");

            await OnChosen.Activate(context);
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

                // Indexer, not Add: two weapons can share a label (the stats-name keying this stage
                // already TODOs above), and a duplicate key must not throw mid-melee.
                if (lines.Count > 0) descriptions[label] = string.Join("\n", lines);
            }

            return descriptions.Count > 0 ? descriptions : null;
        }
    }
}
