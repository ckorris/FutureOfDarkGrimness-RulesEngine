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

            StringSelectionRequest request = new StringSelectionRequest(context.AttackingUnit.PlayerID(), 
                "Choose weapon:", validOptions.Select(option => option.Item1).ToList(), invalidOptions);

            string chosenWeaponStatsName = await GameContext.PlayerRequester
                .RequestDecision<StringSelectionRequest, string>(request);

            Weapon chosenWeapon = validOptions.First(option => option.Item1 == chosenWeaponStatsName).Item2;

            context.SetAttackWeapon(chosenWeapon, out int weaponCount);
            GameContext.Log($"Chose weapon: {chosenWeapon.Name}. Count: {weaponCount}.");

            await OnChosen.Activate(context);
        }
    }
}
