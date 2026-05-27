using UnityEngine;
using UnityEngine.UI;

public class MultiGraphicButton : Button
{
    // Lazy-cached on first access. Awake can't be used here because Selectable's
    // base.set_interactable invokes DoStateTransition from external code paths
    // (e.g. SetPlayerFieldInteractable on a freshly activated hierarchy) before
    // this MonoBehaviour's Awake has been scheduled, NPE'ing on the cache field.
    private GraphicColourTint[] _cachedGraphics;
    private GraphicColourTint[] _graphics
    {
        get
        {
            if (_cachedGraphics == null) _cachedGraphics = this.GetComponentsInChildren<GraphicColourTint>();
            return _cachedGraphics;
        }
    }

    protected override void DoStateTransition(SelectionState state, bool instant)
    {
        // Unity's default Selectable behaviour leaves the button in the Selected
        // visual state after a click — the EventSystem keeps the button as its
        // selected GameObject until focus moves elsewhere, so the player sees a
        // "stuck pressed" highlight (selectedColor is often visually close to
        // pressedColor). This UI is mouse-driven; no keyboard/gamepad navigation
        // surfaces a Selected highlight intentionally. Remap Selected to Normal so
        // a clicked button returns to rest the instant the pointer is released.
        if (state == SelectionState.Selected) state = SelectionState.Normal;

        switch (this.transition)
        {
            case Transition.ColorTint:
                SetStateForAllGraphics(state);
                break;
            case Transition.None:
                break;
            default:
                throw new System.NotSupportedException(string.Format("MultiGraphicButton does not support this transition type - {0}", this.transition));
        }
    }

    private void SetStateForAllGraphics(SelectionState state)
    {
        //Set all our tints
        foreach(GraphicColourTint graphic in _graphics)
        {
            graphic.SetState((int)state);
        }

        //Override standard ColorTint behaviour with our own
        Graphic localGraphic = this.GetComponent<Graphic>();
        if (localGraphic != null)
        {
            switch (state)
            {
                case (SelectionState.Highlighted):
                    {
                        localGraphic.color = colors.highlightedColor;
                        break;
                    }
                case (SelectionState.Pressed):
                    {
                        localGraphic.color = colors.pressedColor;
                        break;
                    }
                case (SelectionState.Selected):
                    {
                        localGraphic.color = colors.selectedColor;
                        break;
                    }
                case (SelectionState.Disabled):
                    {
                        localGraphic.color = colors.disabledColor;
                        break;
                    }
                case (SelectionState.Normal):
                default:
                    {
                        localGraphic.color = colors.normalColor;
                        break;
                    }
            }
        }
    }
}
