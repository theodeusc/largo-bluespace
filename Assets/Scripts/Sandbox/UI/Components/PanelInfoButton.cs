using System;
using UnityEngine;
using UnityEngine.UI;

namespace Glitchers.EcoKnow.Sandbox.UI
{
    // Makes a HUD panel tappable so it can explain itself, without that panel's own script
    // having to grow click plumbing it doesn't otherwise need. Drop one on any panel root
    // (sickness counter, currency panel, ...), give it a key, and SandboxUI resolves the key
    // to explanatory copy and opens the shared InfoPopup.
    //
    // Adding a new explainable HUD panel is therefore Inspector-only work: add the component,
    // set the key, add it to SandboxUI's list.
    //
    // Clicks on interactive children (e.g. the Fundraise button inside the currency panel)
    // are consumed by those children — uGUI dispatches a click to the first handler up the
    // hierarchy — so this only fires for taps on the panel itself.
    public class PanelInfoButton : MonoBehaviour
    {
        public event Action<PanelInfoButton> onClicked;

        [Header("Interaction")]
        // Wired in Init rather than Awake: DoStateTransition can fire before Awake on
        // Selectable subclasses, so button setup belongs on an explicit call.
        [SerializeField] private Button _button;

        [Header("Info")]
        // Identifies the copy to show. Matched against the scenario's lose-condition item IDs
        // first, then against the briefing's entity_descriptions keys.
        [SerializeField] private string _infoKey;

        public string InfoKey => _infoKey;

        public void Init()
        {
            if (_button != null)
            {
                _button.onClick.RemoveAllListeners();
                _button.onClick.AddListener(OnClicked);
            }
        }

        public void Cleanup()
        {
            if (_button != null)
            {
                _button.onClick.RemoveAllListeners();
            }
        }

        private void OnClicked()
        {
            onClicked?.Invoke(this);
        }
    }
}
