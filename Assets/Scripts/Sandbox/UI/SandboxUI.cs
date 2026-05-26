using System;
using System.Collections.Generic;
using Glitchers.EcoKnow.Sandbox.Grid;
using UnityEngine;
using UnityEngine.UI;

namespace Glitchers.EcoKnow.Sandbox.UI
{
    public class SandboxUI : MonoBehaviour
    {
        [Header("Player Side Panel")]
        [SerializeField] private RoundIndicator _roundIndicator;
        [SerializeField] private CurrencyCounter _currencyCounter;
        [SerializeField] private CurrencyCounter _actionPointCounter;
        [SerializeField] private ZonePanel _zonePanel;
        [SerializeField] private FundraiseButton _fundraiseButton;

        [Header("Entity and Objective Panels")]
        [SerializeField] private EntityPanel _entityPanel;
        [SerializeField] private ObjectivesPanel _objectivePanel;
        [SerializeField] private ObjectivesModal _objectivesModal;
        [SerializeField] private ToolPanel _toolPanel;

        [Header("Modification Panels")]
        [SerializeField] private ModifyCellManager _modifyCellManager;

        [Header("Inventory")]
        [SerializeField] private InventoryPanel _inventoryPanel;
        [SerializeField] private InventoryModal _inventoryModal;

        [Header("Results")]
        [SerializeField] private PopulationGraph _populationGraph;
        [SerializeField] private SummaryModal _summaryModal;

        [Header("Multiplayer")]
        [SerializeField] private MultiplayerBorder _multiplayerBorder;
        [SerializeField] private MultiplayerModal _multiplayerModal;

        public int SelectedEntityIndex => _entityPanel == null ? -1 : _entityPanel.SelectedEntityIndex;

        private const string LogChannel = "[SandboxUI]";

        #region Setup
        public void Init(Scenario scenario, EntityManager entityManager, WinCondition[] winConditions, PlayerInventory playerInventory)
        {
            //Initialise our components
            _objectivesModal?.Init(scenario.Name, scenario.Author, scenario.Description, scenario.CoverImageBase64);
            _entityPanel?.Init(entityManager.GetEntityTypeList());
            _objectivePanel?.Init(winConditions, entityManager);
            _toolPanel?.Init();
            _modifyCellManager?.Init();
            _populationGraph?.Init();
            _summaryModal?.HideModal();

            // Zones panel is hidden unconditionally for the water gameplay loop — the player
            // doesn't need a zone legend in the lower-left HUD for this game. The GameObject
            // stays in the prefab so re-enabling it later is one boolean away.
            _zonePanel?.gameObject?.SetActive(false);

            _fundraiseButton?.Init();

            RefreshInventories();

            //Subscribe to UI events
            _entityPanel.onEntitySelected += OnEntitySelected;
            _entityPanel.onEntityDeselected += OnEntityDeselected;
            _modifyCellManager.onEnterModifyMode += OnEnterModifyMode;
            _modifyCellManager.onExitModifyMode += OnExitModifyMode;
            _modifyCellManager.onModifySuccess += OnModifySuccess;
            _inventoryModal.onSellSuccess += OnSellSuccess;

            //Subscribe to other events
            if (entityManager != null)
            {
                entityManager.onEntityHarvested += OnEntityUpdated;
                entityManager.onEntityIntroduced += OnEntityUpdated;
            }
            if (playerInventory != null)
            {
                playerInventory.onItemSold += OnInventoryUpdated;
                playerInventory.onItemBought += OnInventoryUpdated;
            }

            //Force rebuild
            foreach (RectTransform child in this.GetComponentsInChildren<RectTransform>())
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(child);
            }
        }

        public void Cleanup()
        {
            //Unsubscribe
            _entityPanel.onEntitySelected -= OnEntitySelected;
            _entityPanel.onEntityDeselected -= OnEntityDeselected;
            _modifyCellManager.onEnterModifyMode -= OnEnterModifyMode;
            _modifyCellManager.onExitModifyMode -= OnExitModifyMode;
            _modifyCellManager.onModifySuccess -= OnModifySuccess;

            _entityPanel?.Cleanup();
            _modifyCellManager?.Cleanup();
            _fundraiseButton?.Cleanup();
        }
        #endregion

        #region Game Lifecycle
        public bool IsFocused()
        {
            if (_objectivesModal.isActiveAndEnabled)
            {
                return true;
            }

            if (_summaryModal.isActiveAndEnabled)
            {
                return true;
            }

            if (_inventoryModal.isActiveAndEnabled)
            {
                return true;
            }

            if (_populationGraph.IsVisible)
            {
                return true;
            }

            return false;
        }

        public void OnGameEnded(SandboxManager.Result result)
        {
            _summaryModal?.ShowModal();
        }

        public void OnNewRoundStarted(int currentRound, int maxRounds, int actions)
        {
            _roundIndicator?.UpdateRoundCounter(currentRound, maxRounds);

            _objectivePanel?.UpdateWinConditions(currentRound, maxRounds);

            _entityPanel?.DeselectEntity();
            _entityPanel?.UpdateAllWidgets();

            _modifyCellManager?.ExitModifyMode();
            _modifyCellManager?.ResetLimits();

            _inventoryModal?.HideModal();

            RefreshInventories();
        }

        public void OnActionCompleted()
        {
            _modifyCellManager?.ResetLimits();

            // Refresh currency / AP / inventory labels after every action so paths that don't
            // go through OnSellSuccess or OnModifySuccess (e.g. WaterGameActionPanel's Fish /
            // Pick Litter / Fundraise / Buy Treatment) still see updated counters immediately.
            // Idempotent — paths that already refreshed inventories prior to OnActionCompleted
            // just re-set the same label values.
            RefreshInventories();
        }
        #endregion

        #region Entities
        private void OnEntitySelected(int entityIndex)
        {
            //Forces reset
            if (_modifyCellManager.IsModifying)
            {
                _modifyCellManager?.ExitModifyMode();
            }

            _toolPanel?.ShowToolbar(entityIndex, _entityPanel.GetWidgetForEntity(entityIndex));
        }

        private void OnEntityDeselected()
        {
            if (_modifyCellManager.IsModifying)
            {
                _modifyCellManager?.ExitModifyMode();
            }

            _toolPanel?.HideToolbar();
        }

        private void OnEntityUpdated(int column, int row, int id)
        {
            _objectivePanel?.OnEntityUpdated(column, row, id);
            _entityPanel?.OnEntityUpdated(column, row, id);
        }
        #endregion

        #region Inventory
        private void OnInventoryUpdated(string id, int amount)
        {
            RefreshInventories();
            _objectivePanel?.OnInventoryUpdated(id, amount);
        }

        private void RefreshInventories()
        {
            PlayerInventory inventory = SandboxManager.Instance.PlayerInventory;
            if (inventory != null)
            {
                _currencyCounter.SetCurrencyText(inventory.GetAmountHeld(PlayerInventory.CurrencyID));
                _actionPointCounter?.SetCurrencyText(inventory.GetAmountHeld(PlayerInventory.ActionID));
            }

            _inventoryPanel?.RefreshInventory();

            // Re-evaluate the Fundraise button's interactable state every time an action runs
            // or inventory changes, so the button greys out the moment the player spends their
            // last AP or marks the per-turn fundraise flag.
            _fundraiseButton?.Refresh();
        }

        private void OnSellSuccess()
        {
            SandboxManager.SpendActionPoint();
            RefreshInventories();
            Data.DataManager.Instance.RecordEvent(Data.EventType.SELL);
            SandboxManager.OnActionCompleted(); //We only want to increment multiplayer index after the data event is recorded to maintain the correct index in data
        }
        #endregion

        #region Modify Mode
        private void OnEnterModifyMode(ModifyMode mode)
        {

        }

        private void OnModifySuccess()
        {
            RefreshInventories();
            _entityPanel.DeselectEntity();
        }

        private void OnExitModifyMode()
        {

        }
        #endregion

        #region Multiplayer
        public void UpdateMultiplayer(int currentPlayer, bool isNewRound = false, bool isMultiplayer = false)
        {
            _multiplayerBorder.SetBorderVisible(isMultiplayer);

            if (MultiplayerManager.Instance != null)
            {
                Color playerColour = MultiplayerManager.Instance.GetPlayerColour(currentPlayer);
                _multiplayerBorder.SetBorderColour(playerColour);

                string playerName = MultiplayerManager.Instance.GetPlayerName(currentPlayer);
                _multiplayerBorder?.SetPlayer(playerName);
            

                if (isMultiplayer)
                {
                    _multiplayerModal?.ShowModal(playerName, isNewRound);
                }
                else
                {
                    _multiplayerModal.HideModal();
                }
            }
        }
        #endregion
    }
}
