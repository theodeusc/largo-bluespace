using UnityEngine;
using UnityEngine.UI;

namespace Glitchers.EcoKnow.Sandbox.UI
{
    // Canvas-based replacement for the IMGUI Fundraise button that lived in
    // WaterGameActionPanel. Sits as a child of CurrencyPanel (under PlayerSidePanel) so the
    // player can fundraise from the persistent HUD without opening any modal. Visuals come
    // from Button_Action_Fundraise.prefab (a variant of Button_Action_Primary); this script
    // only owns lifecycle, click routing, and interactable state.
    //
    // SandboxManager is the SSOT for both the eligibility predicate (CanFundraise) and the
    // effect (TryFundraise) — this component just forwards the click and mirrors the
    // predicate into Button.interactable.
    public class FundraiseButton : MonoBehaviour
    {
        [SerializeField] private Button _button;

        public void Init()
        {
            if (_button != null)
            {
                _button.onClick.RemoveListener(OnClick);
                _button.onClick.AddListener(OnClick);
            }
            Refresh();
        }

        public void Cleanup()
        {
            if (_button != null)
            {
                _button.onClick.RemoveListener(OnClick);
            }
        }

        // Called by SandboxUI.RefreshInventories after any action or inventory change so the
        // button greys out the moment the player runs out of AP or marks the per-turn flag.
        public void Refresh()
        {
            if (_button == null) return;
            _button.interactable = SandboxManager.Exists && SandboxManager.Instance.CanFundraise();
        }

        private void OnClick()
        {
            if (!SandboxManager.Exists) return;
            SandboxManager.Instance.TryFundraise();
        }
    }
}
