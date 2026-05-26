using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Glitchers.EcoKnow.Sandbox.Data;
using Glitchers.EcoKnow.Sandbox.Grid;
using Glitchers.EcoKnow.Sandbox.Grid.Regions;
using Glitchers.EcoKnow.Sandbox.Terrain;
using Glitchers.EcoKnow.Sandbox.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Glitchers.EcoKnow.Sandbox
{
    public class SandboxManager : MonoBehaviour
    {
        #region Singleton
        protected static SandboxManager instance;
        public static SandboxManager Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = GetInstance();

                    if (instance == null)
                    {
                        Debug.LogError("An instance of " + typeof(SandboxManager) +
                            " is needed in the scene, but there is none.");
                    }
                }

                return instance;
            }
        }

        public static bool Exists
        {
            get
            {
                return instance;
            }
        }

        private static SandboxManager GetInstance()
        {
            if (instance == null)
            {
                return FindFirstObjectByType<SandboxManager>();
            }
            return instance;
        }
        #endregion


        [Header("Gameplay")]
        [SerializeField] private EntityManager _entityManager;
        public EntityManager EntityManager => _entityManager;
        [SerializeField] private GridManager _gridManager;
        public GridManager GridManager => _gridManager;

        [SerializeField] private PlayerInventory _playerInventory;
        public PlayerInventory PlayerInventory => _playerInventory;

        [Header("Rendering")]
        [SerializeField] private WaterTintController _waterTintController;
        public WaterTintController WaterTintController => _waterTintController;

        [Header("UI")]
        [SerializeField] private SandboxUI _sandboxUI;

        private int _maxRounds = 1;
        private int _currentRound = -1;

        public int MaxRounds => _maxRounds;
        public int CurrentRound => _currentRound;

        private int _maxActionsPerRound = 1;

        private int _defaultRounds = 2;
        private int _defaultActionsPerRound = 2;

        public enum Result { WIN, LOSE };
        private List<WinCondition> _winConditions;
        public List<WinCondition> WinConditions => _winConditions;

        // Optional defeat conditions. The first time IsMet() returns true at OnRoundEnded the
        // game short-circuits to EndGame(Result.LOSE) and skips the remaining rounds.
        private List<LoseCondition> _loseConditions = new List<LoseCondition>();
        public List<LoseCondition> LoseConditions => _loseConditions;

        // Per-turn action flags consumed by combo-bonus resolution at OnRoundEnded. Cleared
        // at the top of every StartNewRound so the flags only represent the round just played.
        public const string ActionFlagLitter = "litter";
        public const string ActionFlagFundraise = "fundraise";
        private readonly HashSet<string> _roundActionFlags = new HashSet<string>();

        // Elevation/shore-distance map exposing per-cell water classification and an upsampled
        // shore-distance texture. Built once per scenario load. Consumers (e.g. future water
        // shader binders) read it via the public ElevationMap property.
        private ElevationMap _elevationMap;
        public ElevationMap ElevationMap => _elevationMap;

        // Static region map built once per scenario when Scenario.UseRegionWideCompute is true.
        // Owns the cell -> region mapping and the region adjacency graph used by region-mode
        // movement. Null when region mode is off.
        private RegionComputeManager _regionComputeManager;
        public RegionComputeManager RegionComputeManager => _regionComputeManager;

        // Cached snapshot of the currently running scenario. Set by SetupAndRunScenario and
        // consumed by per-round helpers (RoundEventApplier, PollutantColorDriver) that need
        // the scenario's baseline / addition / decline / schedule tables.
        private Scenario _currentScenario;
        public Scenario CurrentScenario => _currentScenario;

        // Fires once per scenario start after grid/entities/region-init are complete and just
        // before SandboxUI initialises. Late enough that RegionComputeManager (if used) is
        // fully built and EntityManager has its lookup table populated; early enough that
        // listeners can place pixel-art entities, debug overlays, etc. before the first frame
        // is presented. Subscribers MUST be resilient to the event firing multiple times across
        // replays (use Cleanup hooks or idempotent setup).
        public event Action OnScenarioReady;

        // Fires at the end of StartNewRound, after RoundEventApplier has applied this round's
        // addition / decline events and the grid/UI/water-tint have been refreshed. Includes
        // round 0 (the initial round triggered from SetupAndRunScenario), so listeners that
        // need to render in response to settled per-round populations can subscribe to this
        // single event instead of pairing OnScenarioReady with a separate per-round hook.
        public event Action OnRoundAdvanced;

        private const string LogChannel = "[SandboxManager]";

        #region Lifecycle
        void Start()
        {

        }

        void Update()
        {
            if ((_sandboxUI != null) && (!_sandboxUI.IsFocused()))
            {
                _gridManager?.HandleInput();
            }
        }

        public void StartNewGame(Scenario scenario)
        {
            StartCoroutine(SetupAndRunScenario(scenario));
        }

        private IEnumerator SetupAndRunScenario(Scenario scenario)
        {
            if (scenario != null)
            {
                Debug.Log($"Scenario Name is: {scenario.Name}");
                Debug.Log($"Map Layout is: {scenario.Map.fileName}");

                Cleanup();

                //Setup Random
                UnityEngine.Random.InitState(scenario.Seed);

                //Setup rounds
                _currentRound = -1;
                _maxRounds = scenario.Rounds <= 0 ? _defaultRounds : scenario.Rounds;
                _maxActionsPerRound = scenario.ActionsPerRound <= 0 ? _defaultActionsPerRound : scenario.ActionsPerRound;

                MultiplayerManager.Instance.SetCurrentPlayer(-1);

                InitWinConditions(scenario.WinConditions.ToList());
                InitLoseConditions(scenario.LoseConditions);

                //Init grid
                _gridManager?.Init();

                //Init inventory
                _playerInventory?.Init();
                _playerInventory?.AddItem(PlayerInventory.CurrencyID, scenario.StartCurrency);

                //Setup entities and items
                Matrix primaryMatrix = scenario.Matrix;
                if (primaryMatrix == null && scenario.Matrices != null && scenario.Matrices.Length > 0)
                {
                    primaryMatrix = scenario.Matrices[0];
                }
                _entityManager?.RegisterAlphaMatrix(primaryMatrix);
                _entityManager?.RegisterEntities(scenario.Entities.ToList());

                //Setup zone data
                if (scenario.Matrices != null)
                {
                    _entityManager?.RegisterZoneAlphaMatrices(scenario.Matrices);
                }

                if (scenario.Items != null)
                {
                    _playerInventory?.RegisterItemDefinitions(scenario.Items.ToList());
                }

                //Setup grid
                GridDef gridDef = scenario.Map.gridDef;
                _gridManager?.EnableGrid();
                _gridManager?.SetupGrid(gridDef);

                //Build elevation map. Provides per-cell water classification and a shore-distance
                //texture consumed by the EcoKnow/Water shader (via WaterTintController) and any
                //future water-rendering features (read it via SandboxManager.ElevationMap).
                if (_gridManager != null)
                {
                    _elevationMap?.Dispose();
                    _elevationMap = new ElevationMap();
                    _elevationMap.Generate(_gridManager, scenario.Seed);

                    //Reset water material colours to their authored defaults (so tints from the
                    //previous scenario don't carry over) and bind the freshly generated
                    //altitude map + bathymetry parameters to the seawater / freshwater material
                    //instances. Future entity-count-driven tinting calls into the same controller.
                    if (_waterTintController != null)
                    {
                        _waterTintController.ResetToDefaults();
                        _waterTintController.BindElevationData(_elevationMap, _gridManager);
                    }
                }

                _entityManager?.AddEntitiesToGrid(gridDef, _gridManager);

                // Region-wide compute: one-shot region build at scenario load when the toggle is on.
                // After Initialize each region has a single compute cell holding the region's total
                // population; visual cells are zeroed and skipped by the calculators thereafter.
                // RegionComputeManager is the SSOT for cell->region mapping and never mutates again
                // for the life of this scenario. RegionMovementPolicy stays empty by default — add
                // per-entity rules (e.g. water pollution across freshwater/estuary/seawater/overflow)
                // to enable specific region-to-region flows.
                _regionComputeManager = null;
                _entityManager?.SetRegionMode(null);
                if (scenario.UseRegionWideCompute && _entityManager != null && _gridManager != null)
                {
                    _regionComputeManager = new RegionComputeManager();
                    _regionComputeManager.Initialize(_gridManager, _entityManager);
                    _entityManager.SetRegionMode(_regionComputeManager);

                    // Register one-way pollutant dispersion (freshwater -> seawater, overflow -> seawater)
                    // before applying baselines, so the movement policy is in place by the time the
                    // first round ticks. Estuary regions alias to freshwater inside RegionComputeManager,
                    // so no separate estuary entry is needed.
                    RegionMovementPolicy.RegisterWaterPollutants(_entityManager);

                    // Apply per-zone baseline populations to each region's compute cell. Pure
                    // pollutant entries (freshwater / seawater / overflow zones) seed pollutant
                    // counts; sand/seawater entity-baselines seed sealife (1 seal per sand region,
                    // 200 oysters + 200 seagrass per seawater region). Aliased regions (estuary)
                    // are skipped so their state stays zero — they read freshwater via ResolveCell.
                    ApplyZoneBaselines(scenario);
                }

                _currentScenario = scenario;

                //Notify late-stage listeners (e.g. pixel-art entity spawner) that the
                //scenario is fully wired up. Fire BEFORE UI init so any sprites placed in
                //response are part of the first frame the player sees.
                OnScenarioReady?.Invoke();

                // Round 0 colour pass: with baselines in place and no events applied, every
                // per-pollutant norm is 0 and the water keeps its authored defaults. This call
                // also re-syncs the diffusion tint from the (possibly mutated) freshwater
                // instance — necessary if WaterTintController was already in mid-lerp from a
                // previous scenario when Cleanup ran.
                if (_waterTintController != null)
                {
                    Terrain.PollutantColorDriver.Refresh(_currentScenario, _entityManager, _regionComputeManager, _waterTintController);
                }

                //Set up all of our UI
                _sandboxUI?.Init(scenario, _entityManager, _winConditions.ToArray(), _playerInventory);

                yield return new WaitForEndOfFrame();

                StartNewRound();
            }
        }

        public void ReplayCurrentScenario()
        {
            if (ScenarioLoader.Instance.LastPlayedScenario != null)
            {
                StartNewGame(ScenarioLoader.Instance.LastPlayedScenario);
            }
        }

        public void QuitGame()
        {
            RequestLoadMainMenuScene();
        }

        private void Cleanup()
        {
            DataManager.Instance?.ClearData();

            _elevationMap?.Dispose();
            _elevationMap = null;

            _entityManager?.SetRegionMode(null);
            _regionComputeManager = null;
            _currentScenario = null;

            // Wipe per-entity pollutant movement entries left over from the previous scenario.
            // The policy table is static; without this, replaying / switching scenarios stacks
            // stale Allow() entries that would re-enable movement for the wrong entity indices.
            RegionMovementPolicy.Clear();

            _gridManager?.Cleanup();
            _playerInventory.Cleanup();
            _sandboxUI.Cleanup();
        }

        // Per-zone, per-entity seeding driven by Scenario.ZoneBaselines. Writes directly to
        // the (non-aliased) compute cell of every region whose zone id matches. Idempotent —
        // safe to call multiple times during scenario init (each call overwrites with the same
        // value). Aliased regions (estuary -> freshwater) are intentionally skipped so estuary
        // state stays at zero and only the linked freshwater compute cell holds pollutants.
        private void ApplyZoneBaselines(Scenario scenario)
        {
            if (scenario == null || scenario.ZoneBaselines == null) return;
            if (_entityManager == null || _regionComputeManager == null || !_regionComputeManager.IsActive) return;

            for (int i = 0; i < scenario.ZoneBaselines.Length; i++)
            {
                ZoneBaseline baseline = scenario.ZoneBaselines[i];
                if (baseline == null) continue;
                int entityIndex = _entityManager.GetEntityIndex(baseline.EntityID);
                if (entityIndex < 0)
                {
                    Debug.LogWarning($"[SandboxManager] ApplyZoneBaselines: unknown entity ID '{baseline.EntityID}'");
                    continue;
                }

                long value = baseline.Value >= (double)long.MaxValue ? long.MaxValue : (long)System.Math.Floor(System.Math.Max(0.0, baseline.Value));

                foreach (int regionId in _regionComputeManager.AllRegionIds)
                {
                    if (_regionComputeManager.IsAliased(regionId)) continue;
                    if (!_regionComputeManager.TryGetCenterCell(regionId, out var center)) continue;
                    int centerZone = _entityManager.GetZoneType(center.col, center.row);
                    if (centerZone != baseline.ZoneID) continue;
                    _entityManager.RawSetPopulation(center.col, center.row, entityIndex, value);
                }
            }
        }
        #endregion

        #region Rounds and Steps
        public void OnAdvanceRoundPressed()
        {
            OnRoundEnded();
        }

        private void OnRoundEnded()
        {
            //Check our win conditions (evaluated on the round the player just observed)
            foreach (WinCondition winCondition in _winConditions)
            {
                winCondition.OnNewRound();
            }

            // Resolve any per-turn combo bonus BEFORE evaluating defeat — the bonus currency
            // it grants should count toward win conditions that reference currency, and toward
            // the player's record if defeat is about to trigger this round.
            ResolveRoundCombos();

            //Now track data
            Data.DataManager.Instance.RecordEvent(Data.EventType.ROUND_END);

            // Defeat triggers the moment any lose condition reads as met (e.g. oyster collapse).
            // Skip the round-advance branch entirely so the player sees the game-end summary
            // immediately rather than playing through an unrecoverable state.
            if (AnyLoseConditionMet())
            {
                EndGame(Result.LOSE);
                return;
            }

            //Branch based on current round number
            bool finalRound = _currentRound >= _maxRounds - 1;
            if (finalRound)
            {
                EndGame();
            }
            else
            {
                // Transition to the next round and refresh display so the player sees the
                // round's addition/decline events on the previous round's UNMODIFIED state.
                StartNewRound();

                // Movement (and L-V) runs AFTER the new round's events have been applied
                // and the display has refreshed. The cell tokens and water tint stay frozen
                // at the post-Apply visual until the next round's StartNewRound triggers
                // another refresh — that next refresh shows post-(Movement + Apply(N+1)).
                // Net effect: the player gets to see each round's addition pre-drainage,
                // and movement's effect surfaces on the following round.
                _entityManager?.PerformCalculations();
            }
        }

        // Litter + Fundraise in the same turn awards a flat bonus on top of the per-action
        // currency. Resolved once per round on OnRoundEnded, after which the flags are cleared
        // by the next StartNewRound. Bonus amount lives on Scenario for per-scenario tuning.
        private void ResolveRoundCombos()
        {
            if (_currentScenario == null || _playerInventory == null) return;
            int bonus = _currentScenario.LitterFundraiseComboBonus;
            if (bonus <= 0) return;
            if (_roundActionFlags.Contains(ActionFlagLitter) && _roundActionFlags.Contains(ActionFlagFundraise))
            {
                _playerInventory.AddItem(PlayerInventory.CurrencyID, bonus);
                Debug.Log($"{LogChannel} Litter+Fundraise combo bonus awarded: +{bonus} currency.");
            }
        }

        private bool AnyLoseConditionMet()
        {
            if (_loseConditions == null) return false;
            for (int i = 0; i < _loseConditions.Count; i++)
            {
                if (_loseConditions[i] != null && _loseConditions[i].IsMet())
                {
                    Debug.Log($"{LogChannel} Lose condition met: \"{_loseConditions[i].Title}\".");
                    return true;
                }
            }
            return false;
        }

        public void StartNewRound()
        {
            _currentRound += 1;
            MultiplayerManager.Instance.SetCurrentPlayer(0);//Reset

            // Combo flags only describe the round the player is about to play. Wipe before
            // distributing AP so the panel's gating sees a clean slate.
            _roundActionFlags.Clear();

            // Apply the NEW round's addition / decline events here (rather than in
            // CalculateMaths) so the populations the player sees on screen are the
            // post-addition state for this round. Then refresh cell tokens and the
            // water tint so visuals match. Skipped silently when there's no schedule.
            RoundEventApplier.Apply(_currentRound, _currentScenario, _entityManager, _regionComputeManager);
            _gridManager?.UpdateAllCells();
            if (_waterTintController != null)
            {
                Terrain.PollutantColorDriver.Refresh(_currentScenario, _entityManager, _regionComputeManager, _waterTintController);
            }

            //Update actions
            int actionsHeld = 0;
            if (_playerInventory != null)
            {
                actionsHeld = _playerInventory.GetAmountHeld(PlayerInventory.ActionID);
                int actionsToAdd = _maxActionsPerRound - actionsHeld;
                actionsHeld = _playerInventory.AddItem(PlayerInventory.ActionID, actionsToAdd);
            }

            //Update UI
            _sandboxUI?.OnNewRoundStarted(_currentRound, _maxRounds, actionsHeld);
            _sandboxUI?.UpdateMultiplayer(MultiplayerManager.Instance.CurrentPlayerIndex, true, MultiplayerManager.Instance.IsMultiplayer);

            if (_currentRound == 0)
            {
                //Capture here to make sure we have our starting action count and Player index
                DataManager.Instance.RecordEvent(Data.EventType.GAME_START);
            }

            //Notify per-round listeners (e.g. pixel-art entity spawner re-rendering bottles
            //driven by litter populations). Fires after the round's events have been applied
            //and the grid/water-tint have been refreshed, so listeners read settled state.
            OnRoundAdvanced?.Invoke();
        }

        private void EndGame()
        {
            //Refreshes all of the UI to prevent errors appearing behind the Summary Modal
            _sandboxUI?.OnNewRoundStarted(_currentRound, _maxRounds, 0);

            bool playerWins = AreWinConditionsMet();
            _sandboxUI?.OnGameEnded(playerWins == true ? Result.WIN : Result.LOSE);
            Data.DataManager.Instance.RecordEvent(Data.EventType.GAME_END);

        }

        // Forced-result variant used when a defeat trigger short-circuits the round loop.
        // Skips AreWinConditionsMet so a half-met set of win conditions can't accidentally
        // turn a defeat into a victory.
        private void EndGame(Result forcedResult)
        {
            _sandboxUI?.OnNewRoundStarted(_currentRound, _maxRounds, 0);
            _sandboxUI?.OnGameEnded(forcedResult);
            Data.DataManager.Instance.RecordEvent(Data.EventType.GAME_END);
        }
        #endregion

        #region WinConditions
        private bool AreWinConditionsMet()
        {
            //Check win conditions first
            int totalWinConditionsCompleted = 0;
            foreach (WinCondition winCondition in _winConditions)
            {
                totalWinConditionsCompleted += winCondition.Completed == true ? 1 : 0;
            }

            Debug.Log($"Win Conditions Met: {totalWinConditionsCompleted}");

            if (totalWinConditionsCompleted >= _winConditions.Count)
            {
                Debug.Log("WIN THE GAME!");
                return true;
            }

            return false;
        }

        private void InitWinConditions(List<WinConditionRecord> winConditions)
        {
            _winConditions = new List<WinCondition>();

            foreach (WinConditionRecord record in winConditions)
            {
                WinCondition condition = new WinCondition();
                condition.Init(record);

                _winConditions.Add(condition);
            }
        }

        private void InitLoseConditions(LoseConditionRecord[] loseConditions)
        {
            _loseConditions.Clear();
            if (loseConditions == null) return;
            for (int i = 0; i < loseConditions.Length; i++)
            {
                if (loseConditions[i] == null) continue;
                LoseCondition lc = new LoseCondition();
                lc.Init(loseConditions[i]);
                _loseConditions.Add(lc);
            }
        }
        #endregion

        #region Multiplayer
        private void IncrementCurrentPlayer()
        {
            bool shouldAutoAdvance = false;

            int nextPlayer = MultiplayerManager.Instance.CurrentPlayerIndex + 1;
            if (nextPlayer >= MultiplayerManager.Instance.MaxPlayers)
            {
                nextPlayer = 0;
                shouldAutoAdvance = GetAvailableActionPoints() <= 0; //Cater for situations where action points are higher than players (e.g. 2 players 4 action points)
            }
            else if (nextPlayer < 0) // Just in case
            {
                nextPlayer = 0;
            }

            MultiplayerManager.Instance.SetCurrentPlayer(nextPlayer);
            _sandboxUI?.UpdateMultiplayer(MultiplayerManager.Instance.CurrentPlayerIndex, false, MultiplayerManager.Instance.IsMultiplayer);

            //Doesn't make sense for the players to have to press Next Round in multiplayer situations
            if (shouldAutoAdvance)
            {
                OnAdvanceRoundPressed();
            }
        }
        #endregion

        #region Actions
        public static void OnActionCompleted()
        {
            if (MultiplayerManager.Instance != null)
            {
                if (MultiplayerManager.Instance.IsMultiplayer)
                {
                    Instance.IncrementCurrentPlayer();
                }
            }

            Instance?._sandboxUI?.OnActionCompleted();
        }

        public static bool CanPerformAction()
        {
            if ((Instance != null) && (Instance.PlayerInventory != null))
            {
                return Instance.PlayerInventory.GetAmountHeld(PlayerInventory.ActionID) > 0;
            }

            return false;
        }

        public static int SpendActionPoint()
        {
            int actionsRemaining = 0;
            if ((Instance != null) && (Instance.PlayerInventory != null))
            {
                actionsRemaining = Instance.PlayerInventory.RemoveItem(PlayerInventory.ActionID, 1);
            }

            return actionsRemaining;
        }

        public static int GetAvailableActionPoints()
        {
            int actionsRemaining = 0;
            if ((Instance != null) && (Instance.PlayerInventory != null))
            {
                actionsRemaining = Instance.PlayerInventory.GetAmountHeld(PlayerInventory.ActionID);
            }

            return actionsRemaining;
        }

        public static int GetMaxActionPoints()
        {
            int maxActions = 0;
            if (Instance != null)
            {
                maxActions = Instance._maxActionsPerRound;
            }

            return maxActions;
        }

        // Per-turn action flag accessors used by the water-game action panel to track which
        // one-shot actions (e.g. Pick Litter, Fundraise) the player has already used this turn
        // — gates the UI's enabled state and feeds the OnRoundEnded combo-bonus resolver.
        // Returns true when the flag is newly added (false if it was already present).
        public bool MarkRoundAction(string flag)
        {
            if (string.IsNullOrEmpty(flag)) return false;
            return _roundActionFlags.Add(flag);
        }

        public bool HasRoundAction(string flag)
        {
            if (string.IsNullOrEmpty(flag)) return false;
            return _roundActionFlags.Contains(flag);
        }

        // Single source of truth for the fundraise action's eligibility. Used by the
        // FundraiseButton to gate its interactable state — keeping the predicate here means
        // the UI and any future caller (e.g. AI players, scripted demos) read the same rule.
        public bool CanFundraise()
        {
            return CanPerformAction()
                && !HasRoundAction(ActionFlagFundraise)
                && _playerInventory != null
                && _currentScenario != null;
        }

        // Single source of truth for the fundraise action's effect. Mirrors the previous
        // private DoFundraise that lived in WaterGameActionPanel: credits the scenario's
        // FundraisingIncome to the player's currency, marks the per-turn fundraise flag (so
        // the Litter+Fundraise combo bonus in ResolveRoundCombos still fires), spends one AP,
        // and notifies the UI / multiplayer pipeline. Returns true when the action ran.
        public bool TryFundraise()
        {
            if (!CanFundraise()) return false;

            _playerInventory.AddItem(PlayerInventory.CurrencyID, _currentScenario.FundraisingIncome);
            MarkRoundAction(ActionFlagFundraise);
            SpendActionPoint();
            OnActionCompleted();
            return true;
        }
        #endregion

        #region Main Menu
        public void RequestLoadMainMenuScene()
        {
            StartCoroutine(LoadMainMenuScene());
        }

        private IEnumerator LoadMainMenuScene()
        {
            AsyncOperation asyncLoad = SceneManager.LoadSceneAsync("scene_Start");
            while (!asyncLoad.isDone)
            {
                yield return null;
            }
        }
        #endregion
    }
}
