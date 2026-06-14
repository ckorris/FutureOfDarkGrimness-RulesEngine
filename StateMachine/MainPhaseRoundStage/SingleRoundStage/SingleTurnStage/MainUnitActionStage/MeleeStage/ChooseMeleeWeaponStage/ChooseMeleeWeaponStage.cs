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

            //TODO: Handle situations like Deadly, where you have to use a specific weapon first.
            IReadOnlyDictionary<Weapon, int> availableWeapons = new ConcurrentDictionary<Weapon, int>(context.AvailableWeapons);
            IReadOnlyDictionary<Weapon, int> unavailableWeapons = new ConcurrentDictionary<Weapon, int>(context.AlreadyUsedWeapons);

            //TODO: Since we don't store weapons in bindings, we're hackedly using their stats names, which have no
            //protection against identical names.
            List<(string, Weapon)> validOptions = new List<(string, Weapon)>();
            List<StringSelectionRequest.InvalidOption> invalidOptions = new List<StringSelectionRequest.InvalidOption>();

            foreach(KeyValuePair<Weapon, int> kvp in availableWeapons)
            {
                validOptions.Add((kvp.Key.GetWeaponNameAndStats(kvp.Value), kvp.Key));
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
