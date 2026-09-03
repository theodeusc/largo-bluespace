using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Glitchers.EcoKnow.Sandbox.UI
{
    // Applies UIScale to every screen-space CanvasScaler in the game.
    //
    // Auto-bootstraps via [RuntimeInitializeOnLoadMethod] so no scene or prefab edits are
    // needed -- the six UI canvases live across two scenes and five of them are authored
    // inactive, so a per-prefab component would have meant six near-identical prefab
    // diffs and a sixth chance to forget one.
    public class UIScaleDriver : MonoBehaviour
    {
        private const string LogChannel = "[UIScaleDriver]";

        private static UIScaleDriver _instance;

        // Rescanned per scene load rather than per frame. Grid cells carry a CanvasScaler
        // each (prefab_Cell) and there are thousands of them, so a per-frame FindObjects
        // sweep would be ruinous; they are filtered out here anyway for being world-space
        // ConstantPixelSize, but the sweep itself is the cost worth avoiding.
        private readonly List<CanvasScaler> _scalers = new List<CanvasScaler>();

        private int _lastScreenWidth;
        private int _lastScreenHeight;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null) return;

            GameObject host = new GameObject(nameof(UIScaleDriver));
            host.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(host);

            _instance = host.AddComponent<UIScaleDriver>();
        }

        private void Awake()
        {
            _lastScreenWidth = Screen.width;
            _lastScreenHeight = Screen.height;

            UIScale.Evaluate();
            RefreshScalers();

            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (_instance == this) _instance = null;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            RefreshScalers();
        }

        // Two int compares per frame in the steady state. Screen size changes on desktop
        // window resize and on tablet rotation; the project ships with all four autorotate
        // orientations enabled, so this is not a hypothetical path.
        private void Update()
        {
            if (Screen.width == _lastScreenWidth && Screen.height == _lastScreenHeight) return;

            _lastScreenWidth = Screen.width;
            _lastScreenHeight = Screen.height;

            if (UIScale.Evaluate())
            {
                ApplyToScalers();
            }
        }

        // Re-collects the scalers in the newly loaded scene and applies the current scale to
        // all of them. Inactive objects are included: MainMenuUI, SelectScenarioUI,
        // PlayerSetupUI and LoadOtherScenarioUI are all authored SetActive(false) and are
        // switched on later by MainMenuController, at which point setting the reference
        // resolution would be too late to be free.
        private void RefreshScalers()
        {
            _scalers.Clear();

            CanvasScaler[] found = FindObjectsByType<CanvasScaler>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (CanvasScaler scaler in found)
            {
                if (!IsScreenSpaceUi(scaler)) continue;
                _scalers.Add(scaler);
            }

            ApplyToScalers();
        }

        // Only canvases that already opt into resolution scaling are touched. This
        // deliberately skips the two world-space canvases still on Unity's untouched
        // ConstantPixelSize 800x600 default -- TestCanvas in scene_Sandbox and the one on
        // prefab_Cell -- whose scale is a world-space concern and nothing to do with
        // readability.
        private static bool IsScreenSpaceUi(CanvasScaler scaler)
        {
            if (scaler == null) return false;
            if (scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize) return false;

            Canvas canvas = scaler.GetComponent<Canvas>();
            if (canvas == null) return false;

            return canvas.renderMode != RenderMode.WorldSpace;
        }

        private void ApplyToScalers()
        {
            Vector2 reference = UIScale.ReferenceResolution;
            bool applied = false;

            foreach (CanvasScaler scaler in _scalers)
            {
                if (scaler == null) continue;
                if (scaler.referenceResolution == reference) continue;

                scaler.referenceResolution = reference;
                applied = true;
            }

            if (!applied) return;

            // CanvasScaler only pushes its new scale in its own Update, and the canvas
            // RectTransform is not recomputed until the native canvas update at end of
            // frame. Force it now so anything reading canvas rects this frame -- the
            // briefing bubble's world-space placement in particular -- measures the new
            // size rather than the old one.
            Canvas.ForceUpdateCanvases();

            Debug.Log($"{LogChannel} UI scale {UIScale.Current:0.###} " +
                      $"(reference {reference.x:0}x{reference.y:0}, " +
                      $"screen {Screen.width}x{Screen.height}, dpi {Screen.dpi:0.#})");
        }
    }
}
