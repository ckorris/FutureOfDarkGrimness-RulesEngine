using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Serialization;
using FDG.Rules.Tokens;
using FDG.Utilities;
using System.Text.Json;

namespace FDG.SaveLoad
{
    /// <summary>Thrown when a scenario file is invalid; the message is the user-facing explanation.</summary>
    public class ScenarioCompileException : Exception
    {
        public ScenarioCompileException(string message) : base(message) { }
    }

    /// <summary>
    /// Compiles a <see cref="ScenarioFile"/> into a resumable save (#167 T1): builds a real
    /// <see cref="GameDataStore"/> through the same <see cref="GameBootstrap"/> path a live game uses
    /// (armies, rules, hero joins, creation rules), applies the scenario's placements / wounds /
    /// tokens / objectives, and writes one <see cref="GameProgressData"/> positioned at the start of
    /// the active player's activation — so loading the save re-enters at
    /// <c>DeterminePlayerTurnStage</c> and the very next decision is the one the scenario targets.
    /// Never hand-writes save JSON: the store is built live and serialized by
    /// <see cref="GameSaveSerializer"/>, so reference and $type integrity come for free.
    /// </summary>
    public static class ScenarioCompiler
    {
        /// <summary>Loads a scenario JSON (army paths resolved relative to it) and compiles it.</summary>
        public static GameDataStore CompileFromFile(string scenarioPath)
        {
            if (!File.Exists(scenarioPath))
                throw new ScenarioCompileException($"Scenario file not found: {scenarioPath}");

            ScenarioFile? scenario = JsonSerializer.Deserialize<ScenarioFile>(
                File.ReadAllText(scenarioPath), RuleJson.Options);
            if (scenario == null)
                throw new ScenarioCompileException($"Scenario file parsed to null: {scenarioPath}");

            string baseDir = Path.GetDirectoryName(Path.GetFullPath(scenarioPath)) ?? ".";
            List<ArmyListFile> armies = new List<ArmyListFile>(scenario.Players.Count);
            foreach (ScenarioPlayer player in scenario.Players)
            {
                string armyPath = Path.IsPathRooted(player.Army) ? player.Army : Path.Combine(baseDir, player.Army);
                if (!File.Exists(armyPath))
                    throw new ScenarioCompileException($"Army file not found: {armyPath}");

                ArmyListFile? army = JsonSerializer.Deserialize<ArmyListFile>(
                    File.ReadAllText(armyPath), RuleJson.Options);
                if (army == null)
                    throw new ScenarioCompileException($"Army file parsed to null: {armyPath}");

                armies.Add(army);
            }

            return Compile(scenario, armies);
        }

        /// <summary>Convenience: compile a scenario file straight to .fdgsave JSON.</summary>
        public static string CompileFileToSaveJson(string scenarioPath)
            => GameSaveSerializer.Save(CompileFromFile(scenarioPath));

        /// <summary>
        /// Compiles a scenario against already-loaded army files (one per
        /// <see cref="ScenarioFile.Players"/> entry, same order).
        /// </summary>
        public static GameDataStore Compile(ScenarioFile scenario, IReadOnlyList<ArmyListFile> armies)
        {
            Validate(scenario, armies);

            GameSettings settings = BuildSettings(scenario.Settings);
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();

            // Slots mirror a live launch: the PlayerSlot ctor registers its PlayerSlotInfo in the
            // store, which is exactly what the lobby's resume re-crew reads back from the save.
            PlayerSlot[] slots = new PlayerSlot[scenario.Players.Count];
            for (int i = 0; i < scenario.Players.Count; i++)
            {
                int team = scenario.Players[i].Team ?? i;
                slots[i] = new PlayerSlot(i, team, new PlayerID(Guid.NewGuid()), armies[i], store);
            }

            GameBootstrap.AddTeams(slots, store);
            RuleResolver ruleResolver = GameBootstrap.BuildRuleResolver(slots);
            foreach (PlayerSlot slot in slots)
            {
                GameBootstrap.CreateArmy(slot.PlayerID, slot.ArmyListFile, store, ruleResolver);
            }

            // Creation-time rules (Tough max wounds) before wounds are dealt, mirroring launch order.
            RuleEvaluator evaluator = new RuleEvaluator(BuildDiceRoller(settings), ruleResolver: ruleResolver);
            foreach (DataBinding<UnitData> unitBinding in store.GetAllDataBindings<UnitData>())
            {
                UnitCreationRules.Apply(unitBinding.GetValue(), evaluator);
            }

            List<List<DataBinding<UnitData>>> unitsPerPlayer = CollectUnitsPerPlayer(store, slots);

            List<DataBinding<UnitData>> activatedUnits = new List<DataBinding<UnitData>>();
            for (int i = 0; i < scenario.Players.Count; i++)
            {
                ApplyPlayerSetup(scenario.Players[i], i, slots, unitsPerPlayer[i], activatedUnits);
            }

            AutoPlaceUnplacedUnits(slots, unitsPerPlayer);

            CreateObjectives(scenario, store);

            CreateTerrain(scenario, store);

            WriteProgress(scenario, settings, store, slots, unitsPerPlayer, activatedUnits);

            return store;
        }

        private static void Validate(ScenarioFile scenario, IReadOnlyList<ArmyListFile> armies)
        {
            if (scenario.Players.Count < 2)
                throw new ScenarioCompileException("A scenario needs at least 2 players.");
            if (armies.Count != scenario.Players.Count)
                throw new ScenarioCompileException(
                    $"Got {armies.Count} armies for {scenario.Players.Count} players.");
            if (scenario.ActivePlayer < 0 || scenario.ActivePlayer >= scenario.Players.Count)
                throw new ScenarioCompileException(
                    $"activePlayer {scenario.ActivePlayer} is out of range (players: {scenario.Players.Count}).");
            if (scenario.Round < 1 || scenario.Round > GameWideConstants.NUMBER_OF_ROUNDS)
                throw new ScenarioCompileException(
                    $"round {scenario.Round} is out of range (1..{GameWideConstants.NUMBER_OF_ROUNDS}).");

            HashSet<int> teams = new HashSet<int>();
            for (int i = 0; i < scenario.Players.Count; i++)
                teams.Add(scenario.Players[i].Team ?? i);
            if (teams.Count < 2)
                throw new ScenarioCompileException("A scenario needs at least 2 teams (opposing sides).");

            int activeTeam = scenario.Players[scenario.ActivePlayer].Team ?? scenario.ActivePlayer;
            bool activeUnitLeft = false;
            for (int i = 0; i < scenario.Players.Count; i++)
            {
                if ((scenario.Players[i].Team ?? i) != activeTeam) continue;
                foreach (ScenarioUnit unit in scenario.Players[i].Units)
                    if (!unit.Activated) { activeUnitLeft = true; break; }
                // Unlisted units are never pre-activated, so any army with more units than explicit
                // entries always has activations left.
                if (scenario.Players[i].Units.Count < armies[i].Units.Count) activeUnitLeft = true;
                if (activeUnitLeft) break;
            }
            if (!activeUnitLeft)
                throw new ScenarioCompileException(
                    "The active player's team has no unactivated units - nothing for them to do on resume.");
        }

        private static GameSettings BuildSettings(ScenarioSettings scenarioSettings)
        {
            GameSettings settings = GameSettings.GetDefault();
            settings.RandomnessType = scenarioSettings.Randomness.Trim().ToLowerInvariant() switch
            {
                "probabilistic" => ERandomnessType.Probabilistic,
                "realistic" => ERandomnessType.Realistic,
                _ => throw new ScenarioCompileException(
                    $"Unknown randomness '{scenarioSettings.Randomness}' (use Probabilistic or Realistic).")
            };
            settings.DiceSeed = scenarioSettings.DiceSeed;
            return settings;
        }

        private static IDiceRoller BuildDiceRoller(GameSettings settings)
            => settings.RandomnessType == ERandomnessType.Probabilistic
                ? new ProbabilisticDiceRoller()
                : new RealisticDiceRoller(settings.DiceSeed);

        private static List<List<DataBinding<UnitData>>> CollectUnitsPerPlayer(
            GameDataStore store, PlayerSlot[] slots)
        {
            List<ArmyData> armies = store.GetAllValues<ArmyData>().ToList();
            List<List<DataBinding<UnitData>>> result = new List<List<DataBinding<UnitData>>>(slots.Length);
            foreach (PlayerSlot slot in slots)
            {
                ArmyData army = armies.First(a => a.IsOwnedBy(slot.PlayerID));
                result.Add(army.UnitBindings);
            }
            return result;
        }

        private static void ApplyPlayerSetup(ScenarioPlayer player, int playerIndex, PlayerSlot[] slots,
            List<DataBinding<UnitData>> units, List<DataBinding<UnitData>> activatedUnits)
        {
            foreach (ScenarioUnit spec in player.Units)
            {
                DataBinding<UnitData> unitBinding = FindUnit(spec, playerIndex, units);
                UnitData unit = unitBinding.GetValue();

                if (spec.Models != null)
                {
                    if (spec.Models.Count != unit.ModelBindings.Count)
                        throw new ScenarioCompileException(
                            $"Unit '{unit.Name}' (player {playerIndex}) has {unit.ModelBindings.Count} models " +
                            $"(after hero joins) but the scenario places {spec.Models.Count}.");

                    for (int m = 0; m < spec.Models.Count; m++)
                    {
                        Position position = ParsePosition(spec.Models[m],
                            $"unit '{unit.Name}' model {m}");
                        unit.ModelBindings[m].GetValue().SetPosition(position);
                    }
                }

                if (spec.Facing != null)
                {
                    Float2 facing = ParseFacing(spec.Facing, $"unit '{unit.Name}'");
                    foreach (DataBinding<ModelData> model in unit.ModelBindings)
                        model.GetValue().SetFacing(facing);
                }

                if (spec.WoundsDealt != null)
                {
                    if (spec.WoundsDealt.Count > unit.ModelBindings.Count)
                        throw new ScenarioCompileException(
                            $"Unit '{unit.Name}' (player {playerIndex}) has {unit.ModelBindings.Count} models " +
                            $"but woundsDealt lists {spec.WoundsDealt.Count}.");

                    for (int m = 0; m < spec.WoundsDealt.Count; m++)
                    {
                        if (spec.WoundsDealt[m] > 0f)
                            unit.ModelBindings[m].GetValue().DealWounds(spec.WoundsDealt[m]);
                    }
                }

                if (spec.Tokens != null)
                {
                    foreach (ScenarioToken tokenSpec in spec.Tokens)
                        unit.Tokens.AddToken(BuildToken(tokenSpec, unit.Name));
                }

                if (spec.Activated)
                    activatedUnits.Add(unitBinding);
            }
        }

        private static DataBinding<UnitData> FindUnit(ScenarioUnit spec, int playerIndex,
            List<DataBinding<UnitData>> units)
        {
            if (spec.UnitIndex is int index)
            {
                if (index < 0 || index >= units.Count)
                    throw new ScenarioCompileException(
                        $"unitIndex {index} out of range for player {playerIndex} ({units.Count} units after hero joins).");
                return units[index];
            }

            if (string.IsNullOrWhiteSpace(spec.Unit))
                throw new ScenarioCompileException(
                    $"A unit entry for player {playerIndex} names no unit (set 'unit' or 'unitIndex').");

            List<DataBinding<UnitData>> matches = units
                .Where(u => string.Equals(u.GetValue().Name, spec.Unit, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matches.Count == 1) return matches[0];

            string available = string.Join(", ", units.Select(u => $"'{u.GetValue().Name}'"));
            throw new ScenarioCompileException(matches.Count == 0
                ? $"Player {playerIndex} has no unit named '{spec.Unit}'. Available (post hero-join): {available}. " +
                  "(A hero that joins a unit is part of its HOST's entry.)"
                : $"Player {playerIndex} has {matches.Count} units named '{spec.Unit}' - use unitIndex. Units: {available}.");
        }

        private static Position ParsePosition(float[] pair, string what)
        {
            if (pair.Length != 2)
                throw new ScenarioCompileException($"{what}: positions are [x, z] pairs, got {pair.Length} numbers.");
            if (pair[0] == 0f && pair[1] == 0f)
                throw new ScenarioCompileException(
                    $"{what}: (0, 0) is reserved for 'not on the table' (reserves); nudge it to e.g. [0.1, 0.1].");
            return new Position(pair[0], pair[1]);
        }

        private static Float2 ParseFacing(float[] pair, string what)
        {
            if (pair.Length != 2)
                throw new ScenarioCompileException($"{what}: facing is an [x, z] normal, got {pair.Length} numbers.");
            float length = MathF.Sqrt(pair[0] * pair[0] + pair[1] * pair[1]);
            if (length <= 0f)
                throw new ScenarioCompileException($"{what}: facing must be a non-zero vector.");
            return new Float2(pair[0] / length, pair[1] / length);
        }

        private static Token BuildToken(ScenarioToken spec, string unitName)
        {
            if (string.IsNullOrWhiteSpace(spec.Type))
                throw new ScenarioCompileException($"Unit '{unitName}': a token entry has no type.");
            if (spec.Count < 1)
                throw new ScenarioCompileException($"Unit '{unitName}': token '{spec.Type}' count must be >= 1.");

            // Known engine IDs are matched case-insensitively to their canonical spelling; anything
            // else passes through verbatim (rule data may define arbitrary string-typed tokens).
            string[] knownIds =
            {
                TokenType.SHAKEN_ID, TokenType.FATIGUED_ID, TokenType.SPELL_TOKENS_ID,
                TokenType.ACCUMULATOR_TOKENS_ID,
                TokenType.RULE_GRANT_ID, TokenType.ARRIVED_FROM_RESERVE_ID, TokenType.EMBARKED_IN_ID,
                TokenType.MARK_ID, TokenType.POST_COMBAT_MOVE_USED_ID,
                TokenType.OFF_TABLE_FROM_FORCED_MOVE_ID, TokenType.LIMITED_SPENT_ID,
                TokenType.HIT_ROLL_MODIFIER_ID, TokenType.SAVE_ROLL_MODIFIER_ID,
                TokenType.MORALE_ROLL_MODIFIER_ID, TokenType.CAST_ROLL_MODIFIER_ID,
            };
            string typeId = knownIds.FirstOrDefault(
                id => string.Equals(id, spec.Type, StringComparison.OrdinalIgnoreCase)) ?? spec.Type;

            TokenClearTrigger trigger = spec.ClearTrigger.Trim().ToLowerInvariant() switch
            {
                "manualonly" => new TokenClearTrigger.ManualOnly(),
                "roundend" => new TokenClearTrigger.RoundEnd(),
                "activationend" => new TokenClearTrigger.ActivationEnd(),
                "attackend" => new TokenClearTrigger.AttackEnd(),
                "firsttrigger" => new TokenClearTrigger.FirstTrigger(),
                "unitdestroyed" => new TokenClearTrigger.UnitDestroyed(),
                "ownerdestroyed" => new TokenClearTrigger.OwnerDestroyed(),
                _ => throw new ScenarioCompileException(
                    $"Unit '{unitName}': unknown token clearTrigger '{spec.ClearTrigger}'.")
            };

            // A granted roll modifier is only worth anything with its delta: the roll stages read the
            // StatModifier payload, so a payload-less HitRollModifier token nets zero and looks like the
            // modifier silently not working. Only the four carrier types take one.
            TokenPayload? payload = null;
            if (spec.Delta.HasValue)
            {
                bool isRollModifier = typeId is TokenType.HIT_ROLL_MODIFIER_ID or TokenType.SAVE_ROLL_MODIFIER_ID
                    or TokenType.MORALE_ROLL_MODIFIER_ID or TokenType.CAST_ROLL_MODIFIER_ID;
                if (!isRollModifier)
                {
                    throw new ScenarioCompileException($"Unit '{unitName}': token '{spec.Type}' takes no " +
                        "'delta' - only the roll-modifier tokens (HitRollModifier, SaveRollModifier, " +
                        "MoraleRollModifier, CastRollModifier) carry a signed modifier.");
                }
                payload = new TokenPayload.StatModifier(spec.Delta.Value);
            }

            return new Token(new TokenType(typeId), spec.Count, trigger, Payload: payload);
        }

        /// <summary>
        /// Rows any unit the scenario didn't place inside its team's deployment band, mirroring the
        /// engine's single-turn tester: teams get the near/far bands (extra teams spread between),
        /// teammates get parallel rows, models line up along +X. Scenario-central units are expected
        /// to be placed explicitly; this keeps the JSON down to the units that matter.
        /// </summary>
        private static void AutoPlaceUnplacedUnits(PlayerSlot[] slots, List<List<DataBinding<UnitData>>> unitsPerPlayer)
        {
            List<int> teamNumbers = slots.Select(s => s.TeamNumber).Distinct().OrderBy(t => t).ToList();
            float tableH = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
            float nearZ = GameWideConstants.DEPLOYMENT_DISTANCE_INCHES;
            float bandSpan = tableH - 2f * nearZ;

            for (int p = 0; p < slots.Length; p++)
            {
                int teamIndex = teamNumbers.IndexOf(slots[p].TeamNumber);
                float teamZ = teamNumbers.Count == 1
                    ? nearZ
                    : nearZ + bandSpan * teamIndex / (teamNumbers.Count - 1);

                // Parallel rows for teammates, nudged toward the table center so they stay in-band.
                int indexWithinTeam = 0;
                for (int q = 0; q < p; q++)
                    if (slots[q].TeamNumber == slots[p].TeamNumber) indexWithinTeam++;
                float rowZ = teamZ + (teamZ < tableH / 2f ? -3f : 3f) * indexWithinTeam;

                Float2 facing = rowZ < tableH / 2f ? new Float2(0f, 1f) : new Float2(0f, -1f);

                float x = 2f;
                foreach (DataBinding<UnitData> unitBinding in unitsPerPlayer[p])
                {
                    UnitData unit = unitBinding.GetValue();
                    if (unit.GetIsOnBattlefield())
                        continue; // The scenario placed it.

                    foreach (DataBinding<ModelData> modelBinding in unit.ModelBindings)
                    {
                        ModelData model = modelBinding.GetValue();
                        float radius = model.BaseShape.CircumscribedRadiusInches;
                        x += radius;
                        model.SetPosition(new Position(x, rowZ));
                        model.SetFacing(facing);
                        x += radius + 0.2f;
                    }

                    x += 2f; // Gap between units.
                }
            }
        }

        private static void CreateObjectives(ScenarioFile scenario, GameDataStore store)
        {
            List<Position> positions = new List<Position>();
            if (scenario.Objectives != null)
            {
                for (int i = 0; i < scenario.Objectives.Count; i++)
                    positions.Add(ParsePosition(scenario.Objectives[i], $"objective {i}"));
            }
            else
            {
                // Objectives decide the winner - default to three across the midline so every
                // scenario ends in a real victory calculation.
                float w = GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES;
                float midZ = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES / 2f;
                positions.Add(new Position(w * 0.25f, midZ));
                positions.Add(new Position(w * 0.50f, midZ));
                positions.Add(new Position(w * 0.75f, midZ));
            }

            foreach (Position position in positions)
                store.Create(new ObjectiveData(position, store));
        }

        /// <summary>
        /// Synthesizes the scenario's terrain pieces (#264 enabling slice on the #167 umbrella),
        /// mirroring the live PlaceTerrainStage construction: a shape built at its center, rotation
        /// wrapped around the AABB center via <see cref="Stages.TerrainTemplateUtilities"/>, stored
        /// as a plain <see cref="TerrainData"/>. Terrain rules (movement sweeps, cover, LoS) then
        /// see scenario terrain exactly as they see player-placed terrain.
        /// </summary>
        private static void CreateTerrain(ScenarioFile scenario, GameDataStore store)
        {
            if (scenario.Terrain == null) return;

            for (int i = 0; i < scenario.Terrain.Count; i++)
            {
                ScenarioTerrain spec = scenario.Terrain[i];
                string what = $"terrain {i}";

                ETerrainType type = ParseTerrainType(spec.Type, what);
                IZone shape = BuildTerrainShape(spec, what);
                if (spec.HeightInches < 0f)
                    throw new ScenarioCompileException($"{what}: heightInches must be >= 0.");

                store.Create(new TerrainData(type, shape, spec.HeightInches));
            }
        }

        private static ETerrainType ParseTerrainType(string spec, string what)
        {
            if (string.IsNullOrWhiteSpace(spec))
                throw new ScenarioCompileException(
                    $"{what}: no type. Combine {TerrainTypeNames()} with '|' or ','.");

            ETerrainType combined = ETerrainType.None;
            foreach (string part in spec.Split('|', ','))
            {
                string name = part.Trim();
                if (name.Length == 0) continue;
                if (!Enum.TryParse(name, ignoreCase: true, out ETerrainType flag)
                    || flag == ETerrainType.None)
                {
                    throw new ScenarioCompileException(
                        $"{what}: unknown terrain type '{name}'. Known: {TerrainTypeNames()}.");
                }
                combined |= flag;
            }

            if (combined == ETerrainType.None)
                throw new ScenarioCompileException(
                    $"{what}: no type. Combine {TerrainTypeNames()} with '|' or ','.");
            return combined;
        }

        private static string TerrainTypeNames() => string.Join(", ",
            Enum.GetValues<ETerrainType>().Where(t => t != ETerrainType.None));

        private static IZone BuildTerrainShape(ScenarioTerrain spec, string what)
        {
            if (spec.Center == null || spec.Center.Length != 2)
                throw new ScenarioCompileException($"{what}: 'center' must be an [x, z] pair.");
            float cx = spec.Center[0];
            float cz = spec.Center[1];

            switch (spec.Shape.Trim().ToLowerInvariant())
            {
                case "rectangle":
                case "rect":
                {
                    if (spec.Diameter.HasValue)
                        throw new ScenarioCompileException(
                            $"{what}: 'diameter' is for circles - a rectangle takes 'size': [width, depth].");
                    if (spec.Size == null || spec.Size.Length != 2)
                        throw new ScenarioCompileException(
                            $"{what}: a rectangle needs 'size': [width, depth].");
                    if (spec.Size[0] <= 0f || spec.Size[1] <= 0f)
                        throw new ScenarioCompileException($"{what}: size values must be > 0.");

                    float halfW = spec.Size[0] / 2f;
                    float halfD = spec.Size[1] / 2f;
                    IZone rect = new RectangularZone(cx - halfW, cx + halfW, cz - halfD, cz + halfD);
                    // Rotation around the piece's own center, same wrapper the live placement uses.
                    return Stages.TerrainTemplateUtilities.Rotate(rect, spec.RotationDegrees);
                }
                case "circle":
                {
                    if (spec.Size != null)
                        throw new ScenarioCompileException(
                            $"{what}: 'size' is for rectangles - a circle takes 'diameter'.");
                    if (spec.RotationDegrees != 0f)
                        throw new ScenarioCompileException(
                            $"{what}: rotationDegrees on a circle does nothing - remove it.");
                    if (!(spec.Diameter > 0f))
                        throw new ScenarioCompileException($"{what}: a circle needs 'diameter' > 0.");

                    return new CircularZone(cx, cz, spec.Diameter.Value / 2f);
                }
                default:
                    throw new ScenarioCompileException(
                        $"{what}: unknown shape '{spec.Shape}' (use Rectangle or Circle).");
            }
        }

        private static void WriteProgress(ScenarioFile scenario, GameSettings settings, GameDataStore store,
            PlayerSlot[] slots, List<List<DataBinding<UnitData>>> unitsPerPlayer,
            List<DataBinding<UnitData>> activatedUnits)
        {
            int activeTeamNumber = slots[scenario.ActivePlayer].TeamNumber;

            // Active team first; the cursor parks at the END so TryAdvance (which starts checking at
            // index + 1, wrapping) lands on index 0 = the active team.
            List<int> teamOrder = slots.Select(s => s.TeamNumber).Distinct().ToList();
            teamOrder.Remove(activeTeamNumber);
            teamOrder.Insert(0, activeTeamNumber);
            int currentTeamIndex = teamOrder.Count - 1;

            // Within each team the round-robin cursor also parks one before the player who should act
            // first: the scenario's active player on the active team, the first-listed teammate elsewhere.
            Dictionary<int, int> playerIndexPerTeam = new Dictionary<int, int>();
            foreach (int teamNumber in teamOrder)
            {
                List<int> slotIndices = new List<int>();
                for (int i = 0; i < slots.Length; i++)
                    if (slots[i].TeamNumber == teamNumber) slotIndices.Add(i);

                int firstToAct = teamNumber == activeTeamNumber
                    ? slotIndices.IndexOf(scenario.ActivePlayer)
                    : 0;
                playerIndexPerTeam[teamNumber] =
                    (firstToAct - 1 + slotIndices.Count) % slotIndices.Count;
            }

            List<DataBinding<UnitData>> unactivated = new List<DataBinding<UnitData>>();
            foreach (List<DataBinding<UnitData>> units in unitsPerPlayer)
                foreach (DataBinding<UnitData> unit in units)
                    if (!activatedUnits.Contains(unit) && unit.GetValue().GetIsAlive())
                        unactivated.Add(unit);

            GameProgressUtilities.WriteProgress(store, new GameProgressData(
                stage: EResumeStage.MainPhase,
                roundCount: scenario.Round,
                teamActivateOrder: teamOrder,
                currentRoundTeamFinishOrder: new List<int>(),
                currentTeamIndex: currentTeamIndex,
                currentPlayerIndexPerTeam: playerIndexPerTeam,
                unactivatedUnits: unactivated,
                settings: settings));
        }
    }
}
