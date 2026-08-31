using UnityEngine;
using TMPro;

namespace Glitchers.EcoKnow.Sandbox.UI
{
    // Generic title + body popup, used to surface explanatory text for HUD elements that
    // have no room for it inline (objective icons, the sickness counter, the currency panel).
    //
    // Placement is edge-relative rather than widget-relative: it sits level with the pointer
    // vertically, but horizontally is inset from whichever screen edge the pointer is nearer
    // by more than that side's HUD column is wide. That clears the panel the player just
    // tapped without the popup needing to know anything about its neighbours.
    //
    // Deliberately content-agnostic: callers pass already-resolved strings from whichever
    // record is the source of truth for their surface (WinCondition, LoseCondition, the
    // briefing sidecar). This keeps the popup free of any knowledge of the scenario schema
    // and lets one instance serve every caller.
    public class InfoPopup : MonoBehaviour
    {
        [Header("Content")]
        [SerializeField] private TMP_Text _titleText;
        [SerializeField] private TMP_Text _bodyText;

        [Header("Positioning")]
        [SerializeField] private RectTransform _panelRectTransform;

        // How far the popup is held off whichever screen edge it lands against. Per-side,
        // because each margin only has to clear that side's HUD column: the objectives strip
        // on the right is 410 wide plus padding, the player panel on the left only 208. One
        // shared margin left the left-hand popups stranded near the middle of the screen.
        [SerializeField] private float _edgeMarginLeft = 240f;
        [SerializeField] private float _edgeMarginRight = 500f;
        [SerializeField] private float _screenPadding = 12f;

        private const string LogChannel = "[InfoPopup]";

        // Identifies whatever the popup is currently describing, so a second tap on the same
        // source closes it. The entity ToolPanel gets this behaviour from EntityPanel's
        // selection toggle; objectives and HUD panels have no such selection model, so the
        // toggle lives here instead.
        private object _currentKey;

        private RectTransform _canvasRect;

        public bool IsShowing { get; private set; }

        public void Init()
        {
            Canvas canvas = this.GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                _canvasRect = canvas.transform as RectTransform;
            }

            Hide();
        }

        // Opens the popup for key, or closes it when key is already the one on display.
        public void ToggleAt(object key, string title, string body)
        {
            if (IsShowing && _currentKey != null && _currentKey.Equals(key))
            {
                Hide();
                return;
            }

            ShowAt(key, title, body);
        }

        public void ShowAt(object key, string title, string body)
        {
            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(body))
            {
                // Nothing worth showing — most likely a scenario without briefing/condition
                // copy. Stay hidden rather than popping an empty box.
                Hide();
                return;
            }

            if (_titleText != null)
            {
                _titleText.text = title != null ? title.Trim() : string.Empty;
            }

            if (_bodyText != null)
            {
                _bodyText.text = body != null ? MarkdownToRichText.Convert(body.Trim()) : string.Empty;
            }

            _currentKey = key;
            IsShowing = true;
            this.gameObject.SetActive(true);

            // Horizontally the popup is inset from whichever side edge the pointer is nearer,
            // which is what keeps it clear of the edge-hugging HUD columns; vertically it just
            // tracks the pointer so it stays level with whatever was tapped.
            Vector3 pointer = PanelAnchor.PointerWorldPosition();
            PanelAnchor.PlaceInsetFromEdges(this.transform, _panelRectTransform, pointer, _canvasRect, _edgeMarginLeft, _edgeMarginRight);
            PanelAnchor.ClampInside(this.transform, _panelRectTransform, _canvasRect, _screenPadding);
        }

        public void Hide()
        {
            IsShowing = false;
            _currentKey = null;
            this.gameObject.SetActive(false);
        }
    }
}
