using System.Collections.Generic;
using FDG.Presentation;
using FDG.Presentation.Beats;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Utilities;

namespace FDG.Stages
{
    /// <summary>
    /// #042 Impact: when a charging unit reaches combat, it rolls X impact dice (each 2+ a hit) that
    /// resolve through save + wound application BEFORE the normal melee swings. Fired at charge contact,
    /// so only the charger triggers it (melee is only ever entered via Charge, and strike-back never
    /// runs this stage). The impact hits have no weapon — they ride a synthetic AP-0 attack seeded as a
    /// RollToHitResults, then run the existing save→wound sub-pipeline.
    ///
    /// Core Rules v3.6.0 has Impact roll "before either unit attacks": this stage precedes
    /// DetermineStrikeOrderStage in MeleeStage, so impact hits land before any swing - a Counter unit's
    /// first strike included.
    ///
    /// The charge-contact "when" is evaluated for BOTH combatants: the charger (Actor) emits its Impact(X)
    /// dice, and the defender (Subject) emits Brace(X)'s "-X total Impact rolls" (#422; 3.5 had this on
    /// Counter) — both fold into one ImpactSink, so the stage rolls the net dice and skips at zero or below.
    /// Both rules' "unless this unit is Fatigued" clauses are their entries' conditions
    /// (Condition.CountsAsFatiguedInMelee, read on each rule's own bearer), so a Fatigued charger
    /// contributes no dice and a Fatigued defender cancels none.
    ///
    /// #447: Impact(X) is per model. The charger joins with its living models (<see cref="ChargerParticipant"/>)
    /// so per-model copies - a joined hero's own Impact, a per-model grant - fire alongside the unit's, and
    /// Effect.ChargeImpactHits emits X x the living models carrying each copy; the sink sums them, Brace
    /// subtracts from that total. The log line and the dice beat's context show the sum
    /// (<see cref="DescribeDicePool"/>): "Iron Tusk-Beast Riders' Impact rolled 5 dice (5 models x Impact(1))".
    /// </summary>
    public class ResolveImpactHitsStage : ParentStage<ICombatActionContext, ICombatMetadata>
    {
        public StageBinding OnImpactResolved;

        // The number of impact HITS (2+ results) rolled this charge, computed in Enter and seeded into
        // the child metadata. Only meaningful between Enter and the child pipeline running.
        private float _impactHitCount;

        // #197 misc (Heavy Impact): the AP the impact hits carry (0 for core Impact), read off the sink.
        private int _impactArmorPenetration;

        public ResolveImpactHitsStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent)
            : base(gameContext, parent)
        {
        }

        public override async Task Enter(ICombatActionContext context)
        {
            IUnit attacker = context.AttackingUnit.GetValue();
            IUnit defender = context.DefendingUnit.GetValue();

            // Narrate only the impact-sink ops: the charger's models also carry its Ravage (a joined hero's
            // included), which ResolveRavageWoundsStage resolves and narrates itself (#447).
            IReadOnlyList<(RuleOperation Op, string RuleName)> named = GameContext.RuleEvaluator.EvaluateAllNamedLive(
                new ChargeContactContext(attacker, defender),
                op => op is SinkOperation<IImpactSink>,
                ChargeContactParticipants(attacker, defender));

            ImpactSink impact = new ImpactSink();
            impact.ApplyFrom(named.Select(n => n.Op));
            _impactArmorPenetration = impact.ArmorPenetration;
            string pool = DescribeDicePool(named);

            if (impact.TotalDice <= 0)
            {
                // No Impact rule on the charger, a Fatigued charger (its Impact's condition failed), or a
                // Brace defender cancelled the dice down to zero or below — skip straight to the normal
                // melee flow. Say so when there WERE dice to cancel, so the missing roll is explained.
                if (named.Any(n => n.Op is RuleOperation.ChargeImpactHits { DiceCount: > 0 }))
                {
                    GameContext.Log($"{Possessive(attacker.Name)} Impact rolls no dice ({pool}).");
                }

                await OnImpactResolved.Activate(context);
                return;
            }

            IDiceResults rolled = GameContext.DiceRoller.Roll(impact.TotalDice);
            _impactHitCount = rolled.AtOrAbove(2); // each 2+ is a hit (float under the probabilistic roller)

            GameContext.Log($"{Possessive(attacker.Name)} Impact rolled {impact.TotalDice} " +
                $"{(impact.TotalDice == 1 ? "die" : "dice")} ({pool}) -> {_impactHitCount} hit(s) on the charge.");

            await GameContext.Presenter.Present(
                DiceRolledBeat.From(rolled, 2, GameContext.Settings.RandomnessType, "Impact Hits",
                    RollTags.Count(_impactHitCount, "impact hit"), category: ERollBeatCategory.Offense,
                    context: $"{attacker.Name}: {pool}"));

            if (_impactHitCount <= 0f)
            {
                await OnImpactResolved.Activate(context);
                return;
            }

            // Run the save→wound sub-pipeline against the impact hits.
            await base.Enter(context);
        }

        /// <summary>
        /// The participants of the charge-contact evaluation: the charger with its living models
        /// (<see cref="ChargerParticipant"/>), then the defender as a Subject with its living models and each melee
        /// weapon (<see cref="DetermineStrikeOrderStage.SubjectWithMeleeWeapons"/>, so weapon-scoped Brace fires).
        /// Shared with the AI's CombatMath and the Combat Calculator so their dice match the stage's.
        /// </summary>
        public static RuleParticipant[] ChargeContactParticipants(IUnit attacker, IUnit defender)
        {
            RuleParticipant[] defenderSide = DetermineStrikeOrderStage.SubjectWithMeleeWeapons(defender);
            var participants = new RuleParticipant[defenderSide.Length + 1];
            participants[0] = ChargerParticipant(attacker);
            Array.Copy(defenderSide, 0, participants, 1, defenderSide.Length);
            return participants;
        }

        /// <summary>
        /// #447: the charger as the Actor WITH its living models (AnyOwner), so per-model Impact copies fire: a
        /// joined hero's own Impact (the #006 merge relocated it onto the hero's model) and #407 per-model grants.
        /// Unit-attached and granted copies fire through the unit as before; the evaluator's dedup keeps one firing
        /// per attachment, and Effect.ChargeImpactHits counts the living models carrying each.
        /// </summary>
        public static RuleParticipant ChargerParticipant(IUnit attacker) =>
            RuleParticipant.Actor(attacker, models: HeroStatRules.LivingModels(attacker));

        /// <summary>
        /// The impact pool as a sum, from the charge-contact evaluation's named ops (#447): each Impact source as
        /// "N models x Rule(X)" (sources at the same rule and X merged, so a resumed game's per-model attachments
        /// read like a fresh one's), joined by " + ", then each cancelling rule as " - N Rule" - e.g.
        /// "5 models x Impact(1) + 1 model x Impact(3) - 2 Brace". Ops other than impact dice are ignored.
        /// A rule is named as attached minus its authored value (an army file attaches "Impact(1)"), so the X shown
        /// is always the per-model one. ASCII only (game text).
        /// </summary>
        public static string DescribeDicePool(IEnumerable<(RuleOperation Op, string RuleName)> named)
        {
            var sources = new List<(string Rule, int PerModel, int Models, int Dice)>();
            var cancels = new List<(string Rule, int Dice)>();
            foreach ((RuleOperation op, string requestedName) in named)
            {
                if (op is not RuleOperation.ChargeImpactHits impact || impact.DiceCount == 0) continue;
                string ruleName = WithoutAuthoredValue(requestedName);

                if (impact.DiceCount < 0)
                {
                    int at = cancels.FindIndex(c => c.Rule == ruleName);
                    if (at >= 0) cancels[at] = (ruleName, cancels[at].Dice - impact.DiceCount);
                    else cancels.Add((ruleName, -impact.DiceCount));
                    continue;
                }

                int perModel = impact.Models > 0 ? impact.DiceCount / impact.Models : impact.DiceCount;
                int models = impact.Models > 0 ? impact.Models : 0;
                int same = sources.FindIndex(x => x.Rule == ruleName && x.PerModel == perModel
                    && (x.Models > 0) == (models > 0));
                if (same >= 0)
                {
                    sources[same] = (ruleName, perModel, sources[same].Models + models,
                        sources[same].Dice + impact.DiceCount);
                }
                else
                {
                    sources.Add((ruleName, perModel, models, impact.DiceCount));
                }
            }

            var text = new System.Text.StringBuilder();
            foreach ((string rule, int perModel, int models, int dice) in sources)
            {
                if (text.Length > 0) text.Append(" + ");
                text.Append(models > 0
                    ? $"{models} model{(models == 1 ? "" : "s")} x {rule}({perModel})"
                    : $"{rule}({dice})");
            }

            foreach ((string rule, int dice) in cancels)
            {
                text.Append(text.Length > 0 ? " - " : "-");
                text.Append($"{dice} {rule}");
            }

            return text.ToString();
        }

        // "Impact(1)" -> "Impact", "Heavy Impact(3)" -> "Heavy Impact": drops the final parenthesised group (nested
        // ones included) that an attached rule's printable name carries; a bare name comes back unchanged.
        private static string WithoutAuthoredValue(string name)
        {
            string trimmed = name.TrimEnd();
            if (!trimmed.EndsWith(")")) return name;

            int depth = 0;
            for (int i = trimmed.Length - 1; i >= 0; i--)
            {
                if (trimmed[i] == ')') depth++;
                else if (trimmed[i] == '(' && --depth == 0)
                {
                    string head = trimmed[..i].TrimEnd();
                    return head.Length > 0 ? head : name;
                }
            }

            return name;
        }

        // "Iron Tusk-Beast Riders'" / "Warriors'" / "Ogre's" - the log line's possessive (#447's wording).
        private static string Possessive(string name) =>
            name.EndsWith("s", StringComparison.OrdinalIgnoreCase) ? name + "'" : name + "'s";

        protected override ICombatMetadata GetNewChildContext(ICombatActionContext contextSelf)
        {
            // Impact hits come from a weaponless charge — model them as a synthetic attack so the shared
            // save/wound stages can consume them. Core Impact is AP 0; Heavy Impact carries AP(1), read off
            // the sink (#197 misc). The histogram face is cosmetic (only TotalRolls matters downstream).
            Weapon impactWeapon = new Weapon("Impact", rangeInches: 0f, attacks: 0,
                armorPenetration: _impactArmorPenetration);

            CombatMetadata metadata = new CombatMetadata(GameContext, contextSelf.AttackingUnit,
                contextSelf.DefendingUnit, impactWeapon, weaponCount: 1, isMelee: true);

            metadata.AddResult(new RollToHitResults(
                new List<SuccessfulHitInfo>() { new SuccessfulHitInfo(SyntheticHits(_impactHitCount)) },
                new List<FailedHitInfo>()));
            // Melee never runs CoverCheckStage; seed a zero bonus so the shared save stage doesn't throw.
            metadata.AddResult(new CoverCheckResults(0));

            return metadata;
        }

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<ICombatMetadata> startingChild)
        {
            OnImpactResolved = new StageBinding(this);

            Dictionary<string, Transition> dictionary = new TransitionSetBuilder(this)
                .AddChild(new DetermineSaveRollsNeededStage<ICombatMetadata>(GameContext, this), out var determineSaveRollsNeeded)
                .AddChild(new RollToSaveStage<ICombatMetadata>(GameContext, this), out var rollToSave)
                .AddChild(new AssignWoundsStage<ICombatMetadata>(GameContext, this), out var assignWounds)
                .AddChild(new ApplyWoundsStage<ICombatMetadata>(GameContext, this), out var applyWounds)
                .AddSibling(nameof(OnImpactResolved), OnImpactResolved, out string impactResolvedEvent)
                .Build();

            startingChild = determineSaveRollsNeeded;

            determineSaveRollsNeeded.BindNextStage(rollToSave)
                .BindNextStage(assignWounds)
                .BindNextStage(applyWounds)
                .BindToEvent(impactResolvedEvent);

            return dictionary;
        }

        // Bridges a scalar hit count into the IDiceResults the save flow consumes (mirrors
        // RollToHitStage.SyntheticHits). The face is cosmetic — saves count by TotalRolls.
        private static IDiceResults SyntheticHits(float count)
        {
            float[] perSide = new float[IDiceRollerExtensions.DEFAULT_SIDE_COUNT];
            perSide[perSide.Length - 1] = count;
            return new DiceResults(perSide);
        }
    }
}
