using UnityEngine;
using UnityEngine.UI;

namespace Glitchers.EcoKnow.Sandbox.UI
{
    // Shared positioning for the floating panels that attach themselves to a HUD widget
    // (ToolPanel next to an entity row, InfoPopup next to whatever the player just tapped).
    // Extracted from ToolPanel so both callers use one implementation rather than each
    // carrying their own copy of the offset-and-rebuild dance.
    public static class PanelAnchor
    {
        // Moves panel to the anchor's world position, offset horizontally, then forces a
        // layout rebuild so ContentSizeFitter-driven panels report their final size in the
        // same frame they are shown — without this, a panel that grew to fit new text would
        // render one frame at its previous size.
        public static void AnchorTo(Transform panel, RectTransform rebuildTarget, Vector3 anchorWorldPosition, float xOffset)
        {
            if (panel == null) return;

            Vector3 finalPosition = anchorWorldPosition;
            finalPosition.x += xOffset;
            panel.position = finalPosition;

            if (rebuildTarget != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(rebuildTarget);
            }
        }

        // Where the player last touched or clicked, in world space. The sandbox canvas is
        // Screen Space - Overlay, so screen coordinates are world coordinates. Touch is read
        // first because Unity's simulated mouse position lags a lifted finger on Android.
        public static Vector3 PointerWorldPosition()
        {
            if (Input.touchCount > 0)
            {
                Vector2 touch = Input.GetTouch(0).position;
                return new Vector3(touch.x, touch.y, 0f);
            }

            Vector3 mouse = Input.mousePosition;
            mouse.z = 0f;
            return mouse;
        }

        // Positions the popup by insetting it from the two screen edges nearest the anchor,
        // rather than by offsetting from the anchor itself.
        //
        // Offsetting from the pointer measured the gap from the finger, not from the panel
        // under it, so a popup opened from a HUD panel still landed on top of that panel.
        // Insetting from the edges sidesteps that: every HUD element hugs a screen edge, so a
        // margin larger than the widest one guarantees clearance without the popup needing to
        // know anything about its neighbours. The anchor is used only to pick which corner
        // region to land in, keeping the popup on the same side as whatever was tapped.
        //
        // measureTarget is the visible rect (the panel background), which is not the same as
        // the transform being moved: a ContentSizeFitter can grow it well past its parent, and
        // it may sit at an offset within that parent. Measuring what the player actually sees
        // is what keeps the margins honest.
        public static void PlaceInsetFromEdges(Transform moveTarget, RectTransform measureTarget, Vector3 anchorWorldPosition, RectTransform bounds, float marginLeft, float marginRight)
        {
            if (moveTarget == null || measureTarget == null || bounds == null) return;

            LayoutRebuilder.ForceRebuildLayoutImmediate(measureTarget);

            Vector3[] panelCorners = new Vector3[4];
            Vector3[] boundsCorners = new Vector3[4];
            measureTarget.GetWorldCorners(panelCorners);
            bounds.GetWorldCorners(boundsCorners);

            float scale = Mathf.Max(0.0001f, bounds.lossyScale.x);

            Vector3 centre = (panelCorners[0] + panelCorners[2]) * 0.5f;
            float halfWidth = (panelCorners[2].x - panelCorners[0].x) * 0.5f;

            float left = boundsCorners[0].x;
            float right = boundsCorners[2].x;

            // Margins are per-side because the HUD columns aren't symmetric: the objectives
            // strip on the right is far wider than the player panel on the left, and using
            // one margin for both stranded the left-hand popups near the middle of the screen.
            bool towardRight = anchorWorldPosition.x > (left + right) * 0.5f;

            Vector3 desiredCentre = centre;
            desiredCentre.x = towardRight
                ? right - (marginRight * scale) - halfWidth
                : left + (marginLeft * scale) + halfWidth;

            // Vertically the popup just tracks the pointer, so it stays level with whatever was
            // tapped. ClampInside afterwards keeps it on screen near the top and bottom edges.
            desiredCentre.y = anchorWorldPosition.y;

            moveTarget.position += (desiredCentre - centre);
        }

        // Nudges panel back inside bounds when placement pushed it off-screen. Anchoring on a
        // pointer near a screen edge would otherwise put part of the popup outside the canvas.
        public static Vector2 ClampInside(Transform moveTarget, RectTransform measureTarget, RectTransform bounds, float padding = 8f)
        {
            if (moveTarget == null || measureTarget == null || bounds == null) return Vector2.zero;

            // GetWorldCorners order: 0 = bottom-left, 1 = top-left, 2 = top-right, 3 = bottom-right.
            Vector3[] panelCorners = new Vector3[4];
            Vector3[] boundsCorners = new Vector3[4];
            measureTarget.GetWorldCorners(panelCorners);
            bounds.GetWorldCorners(boundsCorners);

            float scale = bounds.lossyScale.x;
            float pad = padding * (Mathf.Approximately(scale, 0f) ? 1f : scale);

            float dx = 0f;
            float dy = 0f;

            if (panelCorners[0].x < boundsCorners[0].x + pad)
            {
                dx = (boundsCorners[0].x + pad) - panelCorners[0].x;
            }
            else if (panelCorners[2].x > boundsCorners[2].x - pad)
            {
                dx = (boundsCorners[2].x - pad) - panelCorners[2].x;
            }

            if (panelCorners[0].y < boundsCorners[0].y + pad)
            {
                dy = (boundsCorners[0].y + pad) - panelCorners[0].y;
            }
            else if (panelCorners[1].y > boundsCorners[1].y - pad)
            {
                dy = (boundsCorners[1].y - pad) - panelCorners[1].y;
            }

            if (dx != 0f || dy != 0f)
            {
                moveTarget.position += new Vector3(dx, dy, 0f);
            }

            return new Vector2(dx, dy);
        }
    }
}
