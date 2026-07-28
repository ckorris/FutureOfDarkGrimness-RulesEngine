using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.StageResolution;

namespace FDG.Stages
{
    /// <summary>
    /// #197 P11 reflect damage. After a melee fully resolves, a unit whose models carry Retaliate or
    /// Deathstrike deals hits back at the enemy:
    /// <list type="bullet">
    /// <item>Retaliate(X): X hits per wound each rule-bearing model took this melee.</item>
    /// <item>Deathstrike(X): X hits per rule-bearing model killed this melee.</item>
    /// </list>
    /// Both are resolved here rather than through the rule pipeline, because the trigger is "wounds taken /
    /// killed over the whole melee" - a tally only knowable once the exchange is over (which is also
    /// Self-Destruct's explicit "after both sides have finished attacking"). Attribution is PER MODEL (owner
    /// ruling): the count uses <see cref="ICombatActionContext.ModelRemainingWoundsAtStart"/>, the per-model
    /// snapshot captured before the first swing, so a champion carrying the rule on a mixed unit reflects only
    /// its own wounds. A model that has the rule via its unit is counted like any other rule-bearing model.
    ///
    /// The reflected hits are plain (no weapon rules, AP 0) and run the real save/wound pipeline against the
    /// attacker, so its armor and Regeneration apply. Because that pipeline runs as a child stage once per
    /// entry, the per-bearer batches LOOP (the StormStage pattern): <see cref="OnBatchDone"/> re-enters and
    /// dequeues the next; <see cref="OnReflectResolved"/> continues the melee flow when the queue drains.
    ///
    /// The hit count is kept FRACTIONAL (never int-locked): in probabilistic mode a model's wounds-taken is a
    /// histogram expectation, so X x wounds is fractional and flows through as such, the #100 dice invariant.
    /// </summary>
    public class ResolveMeleeReflectStage : ParentStage<ICombatActionContext, ICombatMetadata>
    {
        /// <summary> One bearer's reflected-hit batch resolved; bound back here to process the next. </summary>
        public StageBinding OnBatchDone;
        /// <summary> The queue is drained; continue the post-melee flow. </summary>
        public StageBinding OnReflectResolved;

        private readonly record struct ReflectBatch(
            DataBinding<UnitData> Source, DataBinding<UnitData> Target, float Hits);

        // Built once on the first entry, then drained one batch per re-entry. Null between uses so a later
        // melee rebuilds cleanly. Fields, not context state: only this stage reads them and one melee resolves
        // at a time.
        private Queue<ReflectBatch>? _queue;
        private ReflectBatch _current;

        public ResolveMeleeReflectStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent)
            : base(gameContext, parent)
        {
        }

        public override async Task Enter(ICombatActionContext context)
        {
            if (_queue == null)
            {
                // Compute every reflect batch from the melee-end state + snapshot FIRST, then enact any
                // Self-Destruct self-kill - so the kill can't disturb the wounds-taken a bearer's other rules
                // (Retaliate/Deathstrike) read off the same models.
                _queue = BuildBatches(context);
                await ApplySelfDestruct(context);
            }

            if (_queue.Count == 0)
            {
                _queue = null;
                await OnReflectResolved.Activate(context);
                return;
            }

            _current = _queue.Dequeue();
            await base.Enter(context);
        }

        // Both combatants are candidate bearers - after Counter swaps AttackingUnit/DefendingUnit are still
        // the two physical units, so iterating both covers each regardless of role.
        private Queue<ReflectBatch> BuildBatches(ICombatActionContext context)
        {
            var batches = new List<ReflectBatch>();
            AddBatch(context, context.AttackingUnit, context.DefendingUnit, batches);
            AddBatch(context, context.DefendingUnit, context.AttackingUnit, batches);
            return new Queue<ReflectBatch>(batches);
        }

        private void AddBatch(ICombatActionContext context, DataBinding<UnitData> bearer,
            DataBinding<UnitData> target, List<ReflectBatch> batches)
        {
            float hits = ReflectHits(context, bearer.GetValue());
            if (hits <= 0f) return;

            GameContext.Log($"{bearer.GetValue().Name} reflects {hits:0.##} hit(s) onto {target.GetValue().Name}.");
            batches.Add(new ReflectBatch(bearer, target, hits));
        }

        // Self-Destruct's survive-branch: a rule-bearing model that lived through the melee "is immediately
        // killed" now. Kills each surviving Self-Destruct model of both combatants; if that finishes a unit
        // off, routes it through the same destruction choke point a melee kill uses (marks cleared, hook
        // fired), crediting the enemy it fought.
        private async Task ApplySelfDestruct(ICombatActionContext context)
        {
            await SelfDestructBearer(context, context.AttackingUnit, context.DefendingUnit);
            await SelfDestructBearer(context, context.DefendingUnit, context.AttackingUnit);
        }

        private async Task SelfDestructBearer(ICombatActionContext context, DataBinding<UnitData> bearerBinding,
            DataBinding<UnitData> enemyBinding)
        {
            UnitData bearer = bearerBinding.GetValue();
            bool wasAlive = bearer.GetIsAlive();

            // Collected, not applied inline: a model that dies with no ModelDiedBeat simply vanishes,
            // because the front-end hides anything dead in state that has no death animation registered.
            // CasualtyPresentation lands each lethal wound and plays its death in the same instant.
            List<PendingModelWound> pending = new List<PendingModelWound>();

            foreach (DataBinding<ModelData> modelBinding in bearer.ModelBindings)
            {
                ModelData model = modelBinding.GetValue();
                if (RuleArg(bearer, model, CoreRuleCatalog.SelfDestruct) == null) continue;

                float startRemaining = context.ModelRemainingWoundsAtStart
                    .GetValueOrDefault(modelBinding.Reference, model.TotalWounds - model.WoundsDealt);
                float currentRemaining = model.TotalWounds - model.WoundsDealt;

                // Only a SURVIVOR of the melee self-kills; one killed fighting is already dead (and already
                // counted for its X hits).
                if (startRemaining > EPS && currentRemaining > EPS)
                {
                    pending.Add(new PendingModelWound(model, currentRemaining));
                }
            }

            bool killedAny = pending.Count > 0;
            await CasualtyPresentation.ApplyAndPresent(GameContext, pending, bearer.ID, bearer.Name);

            if (killedAny && wasAlive && !bearer.GetIsAlive())
            {
                GameContext.Log($"{bearer.Name} self-destructs.");
                await UnitDestructionNotifier.NotifyUnitDestroyed(GameContext, bearer, enemyBinding.GetValue());
            }
        }

        private float ReflectHits(ICombatActionContext context, UnitData bearer)
        {
            float total = 0f;
            foreach (DataBinding<ModelData> modelBinding in bearer.ModelBindings)
            {
                ModelData model = modelBinding.GetValue();

                int? retaliateX = RuleArg(bearer, model, CoreRuleCatalog.Retaliate);
                int? deathstrikeX = RuleArg(bearer, model, CoreRuleCatalog.Deathstrike);
                int? selfDestructX = RuleArg(bearer, model, CoreRuleCatalog.SelfDestruct);
                if (retaliateX == null && deathstrikeX == null && selfDestructX == null) continue;

                float startRemaining = context.ModelRemainingWoundsAtStart
                    .GetValueOrDefault(modelBinding.Reference, model.TotalWounds - model.WoundsDealt);
                float currentRemaining = model.TotalWounds - model.WoundsDealt;

                if (retaliateX.HasValue)
                {
                    float woundsTaken = System.Math.Max(0f, startRemaining - currentRemaining);
                    total += retaliateX.Value * woundsTaken;
                }

                if (deathstrikeX.HasValue && startRemaining > EPS && currentRemaining <= EPS)
                {
                    total += deathstrikeX.Value;
                }

                // Self-Destruct: every rule-bearing model that entered the melee alive deals X - whether it
                // died fighting or is about to be self-killed (below). Keyed on the START snapshot, so the
                // self-kill this same resolution performs doesn't change the count.
                if (selfDestructX.HasValue && startRemaining > EPS)
                {
                    total += selfDestructX.Value;
                }
            }
            return total;
        }

        private const float EPS = 0.0001f;

        // The model carries the rule if it, or its unit, has the definition; X is Arg(0). A unit-level rule
        // thus counts every model (all models have it); a model-level rule counts only that model.
        private static int? RuleArg(UnitData unit, ModelData model, SpecialRuleDefinition def)
        {
            ResolvedRule? rule = model.RuleDefinitions.FirstOrDefault(r => r.Definition == def)
                ?? unit.RuleDefinitions.FirstOrDefault(r => r.Definition == def);

            if (rule != null && rule.Arguments.Count > 0 && rule.Arguments[0] is RuleArgument.Int x)
            {
                return x.Value;
            }
            return null;
        }

        protected override ICombatMetadata GetNewChildContext(ICombatActionContext contextSelf)
        {
            // Reflected hits are plain: no weapon rules, AP 0. Build the hit group directly at a fractional
            // count rather than rolling an int, so the dice invariant holds; the target still rolls its normal
            // saves through the child pipeline.
            Weapon weapon = new Weapon("Reflect", rangeInches: 0f, attacks: 0, armorPenetration: 0);

            CombatMetadata metadata = new CombatMetadata(GameContext, _current.Source, _current.Target,
                weapon, weaponCount: 1, isMelee: false);

            var group = new SuccessfulHitInfo(SyntheticHitResolution.SyntheticHits(_current.Hits));
            RollToHitResults hitResults = new RollToHitResults(
                new List<SuccessfulHitInfo> { group }, new List<FailedHitInfo>());
            metadata.AddResult(hitResults);
            // No cover for a reflected hit; seed a zero bonus so the shared save stage won't throw.
            metadata.AddResult(new CoverCheckResults(0));

            return metadata;
        }

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<ICombatMetadata> startingChild)
        {
            OnBatchDone = new StageBinding(this);
            OnReflectResolved = new StageBinding(this);

            Dictionary<string, Transition> dictionary = new TransitionSetBuilder(this)
                .AddChild(new DetermineSaveRollsNeededStage<ICombatMetadata>(GameContext, this), out var determineSaveRollsNeeded)
                .AddChild(new RollToSaveStage<ICombatMetadata>(GameContext, this), out var rollToSave)
                .AddChild(new AssignWoundsStage<ICombatMetadata>(GameContext, this), out var assignWounds)
                .AddChild(new ApplyWoundsStage<ICombatMetadata>(GameContext, this), out var applyWounds)
                .AddSibling(nameof(OnBatchDone), OnBatchDone, out string batchDoneEvent)
                .AddSibling(nameof(OnReflectResolved), OnReflectResolved, out string reflectResolvedEvent)
                .Build();

            startingChild = determineSaveRollsNeeded;

            determineSaveRollsNeeded.BindNextStage(rollToSave)
                .BindNextStage(assignWounds)
                .BindNextStage(applyWounds)
                .BindToEvent(batchDoneEvent);

            return dictionary;
        }
    }
}
