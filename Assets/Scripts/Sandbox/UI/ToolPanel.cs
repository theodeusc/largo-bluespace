using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Glitchers.EcoKnow.Sandbox.UI
{
    public class ToolPanel : MonoBehaviour
    {
        // Fires after an action button click has invoked its descriptor. SandboxUI subscribes
        // to this so it can deselect the entity in EntityPanel — without it, the entity row
        // stays "selected" and the next click on it is treated as a deselect, requiring a
        // second click to reopen the panel.
        public event Action onActionPerformed;


        [Header("Entity Info")]
        [SerializeField] private TMP_Text _entityNameText;
        [SerializeField] private TMP_Text _entityPopulationText;

        // Briefing blurb for the selected entity (why it matters, what it costs, what it
        // risks). Sourced from the briefing sidecar via BriefingLookup rather than from the
        // Entity record, which has no description field — this keeps the copy authored in
        // one place alongside the rest of the briefing. Hidden when no blurb exists, so
        // scenarios without a briefing render exactly as they did before.
        [SerializeField] private TMP_Text _entityDescriptionText;

        [Header("Action Button (Button_Action_Primary instance)")]
        // The Button component on the Button_Action_Primary instance. Its onClick is wired at
        // runtime to invoke whichever EntityActionRegistry descriptor matches the selected
        // entity. Persistent listeners on this button were intentionally cleared in the
        // prefab override so only the runtime listener fires.
        [SerializeField] private Button _actionButton;

        // The main label inside the button (e.g. "Forage" or "Pick"). This is the TMP_Text
        // child of the button's "Text" GameObject in Button_Action_Primary.
        [SerializeField] private TMP_Text _actionLabel;

        // The cost-number TMP_Text inside the button's "Action" badge (the box on the left
        // showing "1" with the action-point icon above it). Driven per-entity.
        [SerializeField] private TMP_Text _actionCostText;

        [Header("Positioning")]
        [SerializeField] private RectTransform _panelRectTransform;
        [SerializeField] private float entityWidgetXOffset = -70f;

        private const string LogChannel = "[ToolPanel]";

        // The descriptor for the currently-shown entity, or default if the entity has no
        // registered action. Cached so the button onClick listener doesn't have to re-resolve.
        private EntityActionDescriptor _currentDescriptor;
        private bool _hasCurrentAction;

        // Tracks which entity the panel is showing so OnActionCompleted can refresh
        // interactability (e.g. grey out Pick after the one-shot-per-turn flag flips).
        private int _currentEntityIndex = -1;
        private bool _isShowing;

        // Captured at Init from the prefab so non-pollution entities restore their original
        // styling — otherwise toggling between a pollution-tier entity (which sets a tier
        // colour) and a regular entity would drift the population text to whatever the last
        // tier was.
        private Color _defaultPopulationColour = Color.white;

        public void Init()
        {
            HideToolbar();

            if (_entityPopulationText != null)
            {
                _defaultPopulationColour = _entityPopulationText.color;
            }

            if (_actionButton != null)
            {
                _actionButton.onClick.RemoveAllListeners();
                _actionButton.onClick.AddListener(OnActionPressed);
            }
        }


        public void ShowToolbar(int entityIndex, EntityWidget anchoredWidget)
        {
            _currentEntityIndex = entityIndex;
            _isShowing = true;

            SetEntity(entityIndex);
            this.gameObject.SetActive(true);

            PanelAnchor.AnchorTo(this.transform, _panelRectTransform, anchoredWidget.transform.position, entityWidgetXOffset);
        }

        public void HideToolbar()
        {
            _isShowing = false;
            _currentEntityIndex = -1;
            _hasCurrentAction = false;
            this.gameObject.SetActive(false);
        }

        // Called by SandboxUI.OnActionCompleted so the currently-displayed action button
        // re-evaluates its interactable state after AP/inventory/round-flag changes (e.g.
        // greys out Pick after the one-shot-per-turn flag has been set).
        public void RefreshIfShowing()
        {
            if (!_isShowing) return;
            if (_currentEntityIndex < 0) return;
            SetEntity(_currentEntityIndex);
        }

        private void SetEntity(int entityIndex)
        {
            EntityManager em = SandboxManager.Instance.EntityManager;
            if (em == null) return;

            Entity entity = em.GetEntityType(entityIndex);
            if (entity == null) return;

            if (_entityNameText != null)
            {
                _entityNameText.text = !string.IsNullOrEmpty(entity.DisplayName) ? entity.DisplayName : entity.ID;
            }

            ApplyPopulation(em, entity, entityIndex);

            ApplyDescription(entity);

            ApplyAction(entity);
        }

        // Pulls the entity's briefing blurb and shows it above the action button. The whole
        // GameObject is toggled (not just the string) so the panel's VerticalLayoutGroup
        // collapses the gap entirely for entities with no copy, rather than leaving a
        // 16px hole where the text would have been.
        private void ApplyDescription(Entity entity)
        {
            if (_entityDescriptionText == null) return;

            string description = BriefingLookup.GetEntityDescription(entity);

            if (string.IsNullOrEmpty(description))
            {
                _entityDescriptionText.gameObject.SetActive(false);
                return;
            }

            _entityDescriptionText.text = MarkdownToRichText.Convert(description);
            _entityDescriptionText.gameObject.SetActive(true);
        }

        // Mirrors EntityWidget.UpdateQuantity so the ToolPanel readout stays consistent with
        // the right-side widget: pollution-tier entities show LOW / MED / HIGH coloured, the
        // aggregator shows the worst-of-all tier, everything else shows a formatted count.
        private void ApplyPopulation(EntityManager em, Entity entity, int entityIndex)
        {
            if (_entityPopulationText == null) return;

            if (entity.IsAggregatePollutionDisplay)
            {
                string tier = PollutionTier.ComputeAggregateTier(em);
                _entityPopulationText.text = tier;
                _entityPopulationText.color = PollutionTier.Colour(tier);
                return;
            }

            if (entity.DisplayAsPollutionTier)
            {
                long pop = em.GetTotalPopulationOfEntityTypeLong(entityIndex);
                string tier = PollutionTier.Classify(pop, entity);
                _entityPopulationText.text = tier ?? "?";
                _entityPopulationText.color = PollutionTier.Colour(tier);
                return;
            }

            int population = em.GetTotalPopulationOfEntityType(entityIndex);
            _entityPopulationText.text = population.ToString("n0");
            _entityPopulationText.color = _defaultPopulationColour;
        }

        private void ApplyAction(Entity entity)
        {
            _hasCurrentAction = EntityActionRegistry.TryGetAction(entity.ID, out _currentDescriptor);

            if (_actionButton == null) return;

            if (!_hasCurrentAction)
            {
                // Entities without a registered action (seal, seagrass, water treatment, etc.)
                // show name + count only.
                _actionButton.gameObject.SetActive(false);
                return;
            }

            _actionButton.gameObject.SetActive(true);

            if (_actionLabel != null)
            {
                _actionLabel.text = _currentDescriptor.Label;
            }

            if (_actionCostText != null)
            {
                // The Button_Action_Primary cost badge expects a bare number / short token.
                // Strip the " AP" suffix so the badge reads cleanly alongside the AP icon.
                _actionCostText.text = ExtractCostNumber(_currentDescriptor.CostText);
            }

            bool can = _currentDescriptor.CanPerform != null && _currentDescriptor.CanPerform();
            _actionButton.interactable = can;
        }

        // Pulls the leading numeric portion out of a cost-text string ("1 AP" -> "1"). Lets the
        // EntityActionRegistry keep human-readable costs ("1 AP") while the badge — which
        // already shows an AP icon — renders just the number.
        private static string ExtractCostNumber(string costText)
        {
            if (string.IsNullOrEmpty(costText)) return string.Empty;
            int end = 0;
            while (end < costText.Length && (char.IsDigit(costText[end]) || costText[end] == '-'))
            {
                end++;
            }
            return end > 0 ? costText.Substring(0, end) : costText;
        }

        private void OnActionPressed()
        {
            if (!_hasCurrentAction || _currentDescriptor.Perform == null)
            {
                Debug.LogWarning($"{LogChannel} Action pressed but no descriptor is bound.");
                return;
            }

            _currentDescriptor.Perform();
            // Signal SandboxUI to deselect the entity. That cascades into EntityPanel firing
            // onEntityDeselected, which calls HideToolbar — so we don't need to hide here
            // explicitly. The deselect also resets the panel's _selectedEntityIndex so the
            // very next click on the same row reopens the panel instead of toggling it off.
            onActionPerformed?.Invoke();
        }
    }
}
