using System;
using System.Collections;
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
        [SerializeField] private SicknessIndicator _sicknessIndicator;
        [SerializeField] private CurrencyCounter _currencyCounter;
        [SerializeField] private CurrencyCounter _actionPointCounter;
        [SerializeField] private ZonePanel _zonePanel;
        [SerializeField] private FundraiseButton _fundraiseButton;

        [Header("Entity and Objective Panels")]
        [SerializeField] private EntityPanel _entityPanel;
        [SerializeField] private ObjectivesPanel _objectivePanel;
        [SerializeField] private ObjectivesModal _objectivesModal;
        [SerializeField] private ToolPanel _toolPanel;

        [Header("Briefing")]
        // Shared title+body popup for HUD elements with no room for explanatory text.
        // One instance serves every caller; the content comes from whichever record owns it.
        [SerializeField] private InfoPopup _infoPopup;

        // Re-opens the briefing modal mid-run. Before this, the briefing was shown once at
        // scenario start and was unreachable for the rest of the session.
        [SerializeField] private Button _briefingButton;

        // Every tappable HUD panel that explains itself (sickness counter, currency panel).
        // Adding another is Inspector-only: drop a PanelInfoButton on the panel, set its key,
        // and append it here.
        [SerializeField] private PanelInfoButton[] _infoButtons;

        // The briefing bubble. It is excluded from the side column's layout group and placed
        // by hand instead: the entity panel above it is a full-height spacer, so a laid-out
        // sibling would be pushed to the bottom of the screen rather than sitting under the
        // entity list. Positioned against the entity panel's visible background instead.
        [SerializeField] private RectTransform _briefingButtonRect;

        // Fallback gap between the entity panel and the briefing bubble, used only if the side
        // column's layout group can't be found. Normally the column's own spacing is reused so
        // the bubble sits the same distance below the entity list as the panels above it do.
        [SerializeField] private float _briefingButtonGap = 16f;

        // Bubble height as a multiple of its width. It matches the entity panel's width, so
        // this is the one number that controls how chunky the button looks.
        [SerializeField] private float _briefingButtonHeightRatio = 1.3f;

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
            // Prefer the briefing sidecar if one is loaded; falls back to scenario.Description otherwise.
            string introDescription = BriefingFormatter.Compose(scenario, ScenarioLoader.Instance?.LoadedBriefing);
            _objectivesModal?.Init(scenario.Name, scenario.Author, introDescription, scenario.CoverImageBase64);
            _entityPanel?.Init(entityManager.GetEntityTypeList());
            _objectivePanel?.Init(winConditions, entityManager);
            _toolPanel?.Init();
            _infoPopup?.Init();
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
            if (_toolPanel != null) _toolPanel.onActionPerformed += OnToolPanelActionPerformed;
            if (_objectivePanel != null) _objectivePanel.onObjectiveSelected += OnObjectiveSelected;
            if (_infoButtons != null)
            {
                foreach (PanelInfoButton infoButton in _infoButtons)
                {
                    if (infoButton == null) continue;
                    infoButton.Init();
                    infoButton.onClicked += OnInfoButtonClicked;
                }
            }
            if (_briefingButton != null)
            {
                _briefingButton.onClick.RemoveAllListeners();
                _briefingButton.onClick.AddListener(OnBriefingButtonPressed);
            }

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

            // Positioned twice on purpose. The entity panel's height comes from a chain of
            // ContentSizeFitters (the widget container's drives the panel background's), and
            // the rebuild loop above walks parents before children — so on this pass the
            // background can still report its pre-populated height. Place once now so the
            // bubble is never wildly wrong, then again once Unity's own layout pass has
            // settled, which is when the measurement is actually trustworthy.
            PlaceBriefingButton();
            StartCoroutine(PlaceBriefingButtonWhenLaidOut());
        }

        private IEnumerator PlaceBriefingButtonWhenLaidOut()
        {
            yield return new WaitForEndOfFrame();
            PlaceBriefingButton();
        }

        // Sizes the briefing bubble to the entity panel's visible background and parks it
        // directly underneath, so the two read as one column. The entity panel stays the
        // single source of that width. One-shot: the entity list's width comes from a fixed
        // widget size and its height only changes when the scenario is rebuilt.
        private void PlaceBriefingButton()
        {
            if (_briefingButtonRect == null || _entityPanel == null) return;

            RectTransform panel = _entityPanel.PanelRect;
            if (panel == null) return;

            // Resolve the entity panel innermost-first: its background's height is driven by a
            // ContentSizeFitter that reads the widget container's own fitter, so rebuilding the
            // background alone would measure the container's stale size.
            if (panel.childCount > 0)
            {
                RectTransform container = panel.GetChild(0) as RectTransform;
                if (container != null) LayoutRebuilder.ForceRebuildLayoutImmediate(container);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel);

            float width = panel.rect.width;
            if (width <= 0f) return;

            _briefingButtonRect.sizeDelta = new Vector2(width, width * _briefingButtonHeightRatio);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_briefingButtonRect);

            // GetWorldCorners: 0 = bottom-left, 3 = bottom-right. The bubble's pivot is
            // top-centre, so its position is exactly the point we want its top edge centred on.
            Vector3[] corners = new Vector3[4];
            panel.GetWorldCorners(corners);

            float scale = _briefingButtonRect.lossyScale.y;
            float gap = ResolveColumnSpacing() * (Mathf.Approximately(scale, 0f) ? 1f : scale);

            _briefingButtonRect.position = new Vector3(
                (corners[0].x + corners[3].x) * 0.5f,
                corners[0].y - gap,
                _briefingButtonRect.position.z);
        }

        // The gap under the entity list is taken from the side column's own layout spacing, so
        // it matches the gap between the objectives panel and the entity panel above it rather
        // than being a second number that has to be kept in sync by hand.
        private float ResolveColumnSpacing()
        {
            if (_briefingButtonRect == null || _briefingButtonRect.parent == null) return _briefingButtonGap;

            HorizontalOrVerticalLayoutGroup column = _briefingButtonRect.parent.GetComponent<HorizontalOrVerticalLayoutGroup>();
            return column != null ? column.spacing : _briefingButtonGap;
        }

        public void Cleanup()
        {
            //Unsubscribe
            _entityPanel.onEntitySelected -= OnEntitySelected;
            _entityPanel.onEntityDeselected -= OnEntityDeselected;
            _modifyCellManager.onEnterModifyMode -= OnEnterModifyMode;
            _modifyCellManager.onExitModifyMode -= OnExitModifyMode;
            _modifyCellManager.onModifySuccess -= OnModifySuccess;
            if (_objectivePanel != null) _objectivePanel.onObjectiveSelected -= OnObjectiveSelected;
            if (_infoButtons != null)
            {
                foreach (PanelInfoButton infoButton in _infoButtons)
                {
                    if (infoButton == null) continue;
                    infoButton.onClicked -= OnInfoButtonClicked;
                    infoButton.Cleanup();
                }
            }
            if (_briefingButton != null) _briefingButton.onClick.RemoveAllListeners();

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

            _infoPopup?.Hide();

            _modifyCellManager?.ExitModifyMode();
            _modifyCellManager?.ResetLimits();

            _inventoryModal?.HideModal();

            RefreshInventories();
            RefreshSicknessIndicator();
        }

        public void OnActionCompleted()
        {
            _modifyCellManager?.ResetLimits();

            // Refresh currency / AP / inventory labels after every action so paths that don't
            // go through OnSellSuccess or OnModifySuccess (e.g. WaterGameActions.DoFish /
            // DoPickLitter / Fundraise / Buy Treatment) still see updated counters immediately.
            // Idempotent — paths that already refreshed inventories prior to OnActionCompleted
            // just re-set the same label values.
            RefreshInventories();
            // Sickness is mutated via raw PlayerInventory.AddItem (no onItemSold/Bought event),
            // so the HUD refresh has to be driven from here rather than reacting to an event.
            RefreshSicknessIndicator();
            // ToolPanel may be displayed for an entity whose action just consumed AP or
            // tripped a per-turn flag — re-evaluate its button interactability now.
            _toolPanel?.RefreshIfShowing();
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

            // ToolPanel and InfoPopup share the right-hand column and would overlap, so the
            // two are mutually exclusive: opening either closes the other.
            _infoPopup?.Hide();

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

        // Direct-action button on ToolPanel was just clicked. Deselect the entity so the
        // EntityPanel's selection state resets — otherwise the next click on the same row
        // is interpreted as a toggle-off and the player has to click twice to reopen.
        private void OnToolPanelActionPerformed()
        {
            _infoPopup?.Hide();
            _entityPanel?.DeselectEntity();
        }

        private void OnEntityUpdated(int column, int row, int id)
        {
            _objectivePanel?.OnEntityUpdated(column, row, id);
            _entityPanel?.OnEntityUpdated(column, row, id);
        }
        #endregion

        #region Briefing
        // An objective icon was tapped. The title and description come from the WinCondition
        // itself — the same records SummaryModal renders at game end — so the objective reads
        // identically in the HUD and in the results screen.
        private void OnObjectiveSelected(WinCondition condition)
        {
            if (condition == null || _infoPopup == null) return;

            // ToggleAt so a second tap on the same objective closes it, matching how the
            // entity rows behave. The popup places itself from the pointer.
            _entityPanel?.DeselectEntity();
            _infoPopup.ToggleAt(condition, condition.title, condition.description);
        }

        // A HUD panel that explains itself was tapped. Copy is resolved from whichever record
        // owns it: lose conditions win (so the sickness rule shown is the rule enforced,
        // threshold included), otherwise the briefing's entity_descriptions.
        private void OnInfoButtonClicked(PanelInfoButton source)
        {
            if (_infoPopup == null || source == null) return;

            string key = source.InfoKey;
            if (string.IsNullOrEmpty(key)) return;

            string title;
            string body;

            LoseCondition condition = SandboxManager.Instance?.GetLoseConditionForItem(key);
            if (condition != null)
            {
                title = condition.Title;
                body = condition.Description;
            }
            else
            {
                body = BriefingLookup.GetDescriptionByKey(key);
                if (string.IsNullOrEmpty(body))
                {
                    // No briefing loaded, or the scenario's briefing doesn't describe this
                    // panel. Nothing to say, so say nothing.
                    return;
                }
                title = BriefingLookup.Humanise(key);
            }

            _entityPanel?.DeselectEntity();
            _infoPopup.ToggleAt(source, title, body);
        }

        private void OnBriefingButtonPressed()
        {
            // Close the transient popups first so they aren't left floating behind the modal.
            _infoPopup?.Hide();
            _entityPanel?.DeselectEntity();

            _objectivesModal?.ShowModal();
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

        private void RefreshSicknessIndicator()
        {
            if (_sicknessIndicator == null) return;
            SandboxManager mgr = SandboxManager.Instance;
            if (mgr == null || mgr.PlayerInventory == null) return;

            int current = mgr.PlayerInventory.GetAmountHeld(WaterGameActions.SicknessInventoryId);

            // Pull the lose-condition threshold so the widget tracks whatever the scenario
            // defines, instead of hardcoding 3 in two places (scenario JSON + UI). The same
            // lookup backs the sickness info popup, so the counter and the explanation can
            // never disagree about the rule.
            LoseCondition condition = mgr.GetLoseConditionForItem(WaterGameActions.SicknessInventoryId);
            int max = condition != null ? Mathf.Max(1, (int)condition.LowerLimit) : 0;

            _sicknessIndicator.UpdateSicknessCounter(current, max);
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
