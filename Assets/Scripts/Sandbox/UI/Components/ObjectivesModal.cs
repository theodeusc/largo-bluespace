using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Glitchers.EcoKnow.Sandbox.UI
{
    public class ObjectivesModal : MonoBehaviour
    {
        [SerializeField] private TMP_Text _titleText;
        [SerializeField] private TMP_Text _authorText;
        [SerializeField] private TMP_Text _descriptionText;
        [SerializeField] private Image _coverImage;

        // Label on the dismiss button. Reads "Start" the first time the modal appears (it
        // gates the beginning of the run) and "Close" on every re-open from the HUD briefing
        // button, where the run is already underway and "Start" would be misleading.
        [SerializeField] private TMP_Text _dismissButtonLabel;

        [Header("Dismiss Labels")]
        [SerializeField] private string _firstShowLabel = "Start";
        [SerializeField] private string _reopenLabel = "Close";

        [Header("Responsive Sizing")]
        // Drives the modal box's size. Authored at a fixed 440x540, which is only ~11% of a
        // 1920x1080 canvas — far too small for a briefing this long once the player has to
        // scroll it. Resized on every show instead, because the tablet build can change
        // orientation mid-run.
        [SerializeField] private LayoutElement _modalContainerLayout;

        // Fractions of the canvas the modal occupies. Both multiply out to ~45% of the screen
        // area; they differ in shape so the modal stays wide-and-shallow in landscape and
        // narrow-and-tall in portrait rather than becoming a letterbox in one of them.
        [SerializeField] private Vector2 _landscapeFraction = new Vector2(0.66f, 0.68f);
        [SerializeField] private Vector2 _portraitFraction = new Vector2(0.86f, 0.52f);

        // Canvas size the current layout was built for. Watched while the modal is open so a
        // device rotation or a resized player window re-fits it — sizing only on open left
        // the modal at its previous shape until it was closed and reopened.
        private Vector2 _lastCanvasSize = Vector2.zero;

        private Texture2D _coverImageTexture = null;

        // True once Init has successfully populated the modal. Guards ShowModal so the
        // briefing button can't surface an empty modal for a scenario that supplied no
        // description or cover image.
        private bool _hasContent = false;

        // Distinguishes the automatic scenario-start presentation from later re-opens, which
        // is the only difference in how the modal behaves.
        private bool _hasBeenDismissedOnce = false;

        private const string LogChannel = "[ObjectivesModal]";

        public bool HasContent => _hasContent;

        // Called once from SandboxUI.Init. Caches the composed briefing and shows the modal
        // immediately, as before. The content stays cached for the whole scenario so the HUD
        // briefing button can re-show it without recomposing.
        public void Init(string title, string author, string description, string coverImageBase64)
        {
            if (string.IsNullOrEmpty(description) || string.IsNullOrEmpty(coverImageBase64))
            {
                _hasContent = false;
                this.gameObject.SetActive(false);
                return;
            }

            if (_titleText != null)
                _titleText.text = title;

            if (_authorText != null)
                _authorText.text = author;

            if (_descriptionText != null)
                _descriptionText.text = description;

            SetCoverImage(coverImageBase64);

            _hasContent = true;

            ShowModal();
        }

        // Re-opens the briefing mid-run. Follows the project's ShowModal/HideModal convention
        // so it reads like every other modal in the sandbox UI. SandboxUI.IsFocused already
        // tests this modal's active state, so gameplay input is blocked while it is open
        // without any extra wiring.
        public void ShowModal()
        {
            if (!_hasContent)
            {
                Debug.LogWarning($"{LogChannel} ShowModal called before Init supplied content; ignoring.");
                return;
            }

            ApplyDismissLabel();

            this.gameObject.SetActive(true);

            // After SetActive, so the canvas rect and the modal's own layout are live.
            ApplyResponsiveSize();
        }

        // Re-fits the modal when the canvas changes shape underneath it (orientation change on
        // the tablet build, or a resized player window). Cheap: two float compares per frame,
        // and only while the modal is actually on screen.
        private void Update()
        {
            ApplyResponsiveSize();
        }

        // Scales the modal box to a fixed share of the canvas, picking proportions from the
        // current orientation. No-ops unless the canvas has actually changed size since the
        // last fit, so the per-frame call costs nothing in the steady state.
        private void ApplyResponsiveSize()
        {
            if (_modalContainerLayout == null) return;

            Canvas canvas = this.GetComponentInParent<Canvas>();
            RectTransform canvasRect = canvas != null ? canvas.transform as RectTransform : null;
            if (canvasRect == null) return;

            Rect canvasBounds = canvasRect.rect;
            if (canvasBounds.width <= 0f || canvasBounds.height <= 0f) return;

            Vector2 canvasSize = new Vector2(canvasBounds.width, canvasBounds.height);
            if (canvasSize == _lastCanvasSize) return;
            _lastCanvasSize = canvasSize;

            Vector2 fraction = canvasBounds.width >= canvasBounds.height ? _landscapeFraction : _portraitFraction;

            float width = canvasBounds.width * fraction.x;
            float height = canvasBounds.height * fraction.y;

            _modalContainerLayout.minWidth = width;
            _modalContainerLayout.preferredWidth = width;
            _modalContainerLayout.minHeight = height;
            _modalContainerLayout.preferredHeight = height;

            RectTransform self = this.transform as RectTransform;
            if (self != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(self);
            }
        }

        public void HideModal()
        {
            _hasBeenDismissedOnce = true;
            this.gameObject.SetActive(false);
        }

        private void ApplyDismissLabel()
        {
            if (_dismissButtonLabel == null) return;

            _dismissButtonLabel.text = _hasBeenDismissedOnce ? _reopenLabel : _firstShowLabel;
        }

        private void SetCoverImage(string base64)
        {
            if (_coverImage == null)
            {
                return;
            }

            byte[] imageBytes = Convert.FromBase64String(base64);
            _coverImageTexture = new Texture2D(2, 2);
            if (_coverImageTexture.LoadImage(imageBytes))
            {
                _coverImage.sprite = Sprite.Create(_coverImageTexture, new Rect(0, 0, _coverImageTexture.width, _coverImageTexture.height), Vector2.zero);
                _coverImage.preserveAspect = true;
            }
        }

        // Wired to the dismiss button's onClick in the prefab. Kept under its original name
        // so the existing persistent call in ObjectivesModal.prefab keeps resolving.
        public void OnStartPressed()
        {
            //Dismiss
            HideModal();
        }
    }
}
