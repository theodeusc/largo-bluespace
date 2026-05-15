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
                Random.InitState(scenario.Seed);

                //Setup rounds
                _currentRound = -1;
                _maxRounds = scenario.Rounds <= 0 ? _defaultRounds : scenario.Rounds;
                _maxActionsPerRound = scenario.ActionsPerRound <= 0 ? _defaultActionsPerRound : scenario.ActionsPerRound;

                MultiplayerManager.Instance.SetCurrentPlayer(-1);

                InitWinConditions(scenario.WinConditions.ToList());

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

            _gridManager?.Cleanup();
            _playerInventory.Cleanup();
            _sandboxUI.Cleanup();
        }
        #endregion

        #region Rounds and Steps
        public void OnAdvanceRoundPressed()
        {
            CalculateMaths();
            OnRoundEnded();
        }

        private void CalculateMaths()
        {
            _entityManager?.PerformCalculations();
            _gridManager?.UpdateAllCells();
        }

        private void OnRoundEnded()
        {
            //Check our win conditions
            foreach (WinCondition winCondition in _winConditions)
            {
                winCondition.OnNewRound();
            }

            //Now track data
            Data.DataManager.Instance.RecordEvent(Data.EventType.ROUND_END);

            //Branch based on current round number
            bool finalRound = _currentRound >= _maxRounds - 1;
            if (finalRound)
            {
                EndGame();
            }
            else
            {
                StartNewRound();
            }
        }

        public void StartNewRound()
        {
            _currentRound += 1;
            MultiplayerManager.Instance.SetCurrentPlayer(0);//Reset

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
        }

        private void EndGame()
        {
            //Refreshes all of the UI to prevent errors appearing behind the Summary Modal
            _sandboxUI?.OnNewRoundStarted(_currentRound, _maxRounds, 0);

            bool playerWins = AreWinConditionsMet();
            _sandboxUI?.OnGameEnded(playerWins == true ? Result.WIN : Result.LOSE);
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
