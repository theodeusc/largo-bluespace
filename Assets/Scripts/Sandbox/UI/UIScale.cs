using System;
using UnityEngine;

namespace Glitchers.EcoKnow.Sandbox.UI
{
    // Single source of truth for how much larger the UI renders than its authored
    // 1920x1080 baseline on a given display.
    //
    // The problem this solves: every UI canvas is a CanvasScaler in ScaleWithScreenSize
    // with matchWidthOrHeight = 1, so UI scale tracks *pixel height only*. A 27" and a
    // 15" 1080p display therefore both scale at exactly 1.0 even though the text on the
    // 15" panel is physically half the size, and a 1366x768 laptop scales to 0.71 and
    // shrinks text further. Nothing in the project has ever read a physical-size signal.
    //
    // The fix is to compare the physical size of one *reference unit* against the size it
    // has on the display the layout was authored against, and shrink the canvas reference
    // resolution to compensate. Scaling the reference resolution (rather than font sizes)
    // grows text, icons, padding and legacy UI.Text together, so relative layout is
    // unchanged by construction -- which is what keeps the 25-odd fixed-height rects with
    // truncating overflow modes from clipping.
    public static class UIScale
    {
        #region Tuning

        // The resolution every prefab in the project is authored against. Also the
        // numerator for the runtime reference resolution: refRes = Base / Current.
        private const float BaseReferenceWidth = 1920f;
        private const float BaseReferenceHeight = 1080f;

        public static readonly Vector2 BaseReferenceResolution =
            new Vector2(BaseReferenceWidth, BaseReferenceHeight);

        // Pixel density the layout is implicitly tuned for -- a ~23" 1080p desktop panel,
        // which is what "looks right on a big computer screen" meant in the playtest
        // feedback. Scale is 1.0 exactly when the display matches this.
        private const float ReferenceDpi = 96f;

        // Screen.dpi returns 0 on most standalone desktop builds. Falling back to the
        // reference DPI degrades the formula into a pure resolution heuristic, which still
        // corrects the low-resolution laptop case (1366x768 -> ~1.19) because the Unity
        // scale factor alone carries that signal. Only the same-resolution/smaller-panel
        // case needs a real DPI reading.
        private const float FallbackDpi = 96f;

        // Fraction of the physical difference to correct for. Full correction (1.0) is too
        // aggressive: a small screen is also viewed from closer, so the angular size of the
        // text falls by much less than its physical size does. Half correction tracks the
        // perceived difference without letting the HUD swallow the map.
        private const float CompensationExponent = 0.5f;

        // Never scale below the authored baseline -- a large desktop display should render
        // exactly what was authored, so this change can never regress the case that already
        // works.
        private const float MinScale = 1f;

        // Ceiling, derived rather than picked. The left HUD column is the only thing in the
        // game that constrains UI scale: its six always-visible panels are fixed-height and
        // its layout group finishes laying them out 1016px down the 1080 reference height,
        // with no ScrollRect or mask anywhere in the column to catch anything past that.
        //
        // Making the panels content-driven was tried and reclaimed nothing -- their content
        // measures 1044, slightly MORE than the authored heights, so the authored values
        // were already tight. Raising this ceiling therefore needs the column to carry less
        // content, which is a design change rather than a layout one.
        private const float ColumnContentBottom = 1016f;

        // Clear space kept below the last panel. Without it the ceiling lands exactly where
        // the column stops: verified on 1366x768, where a 1.04 cap left ~22 reference units
        // (~16 physical px) and the bottom panel visibly grazed the screen edge.
        private const float ColumnBottomMargin = 48f;

        private const float MaxScale = BaseReferenceHeight / (ColumnContentBottom + ColumnBottomMargin);

        #endregion

        private static float _current = 1f;
        private static bool _evaluated;

        // Raised after Current changes. Subscribers re-run any layout maths they cached in
        // absolute pixels; see SandboxUI, whose briefing bubble is positioned in world space.
        public static event Action Changed;

        // Multiplier applied to the authored layout. 1.0 means "render as authored".
        public static float Current
        {
            get
            {
                if (!_evaluated) Evaluate();
                return _current;
            }
        }

        // The reference resolution a CanvasScaler should be given. Shrinking the reference
        // resolution is what makes the UI bigger: with matchWidthOrHeight = 1 the canvas
        // scale factor is screenHeight / referenceHeight.
        public static Vector2 ReferenceResolution => BaseReferenceResolution / Current;

        // Recomputes from current screen metrics. Returns true if the value moved, so
        // callers can skip the (comparatively expensive) reapply when nothing changed.
        public static bool Evaluate()
        {
            float scale = Compute();
            _evaluated = true;

            if (Mathf.Approximately(scale, _current)) return false;

            _current = scale;
            Changed?.Invoke();
            return true;
        }

        private static float Compute()
        {
            float screenHeight = Screen.height;
            if (screenHeight <= 0f) return _current;

            float dpi = Screen.dpi > 0f ? Screen.dpi : FallbackDpi;

            // Unity's own scale factor under matchWidthOrHeight = 1.
            float canvasScaleFactor = screenHeight / BaseReferenceResolution.y;

            // Physical size of one reference unit, in inches, here and on the display the
            // layout was authored for. One unit is one authored pixel, so this is the
            // quantity a player actually perceives as "text size".
            float actualUnitInches = canvasScaleFactor / dpi;
            if (actualUnitInches <= 0f) return _current;

            float referenceUnitInches = 1f / ReferenceDpi;

            float shortfall = referenceUnitInches / actualUnitInches;
            return Mathf.Clamp(Mathf.Pow(shortfall, CompensationExponent), MinScale, MaxScale);
        }
    }
}
