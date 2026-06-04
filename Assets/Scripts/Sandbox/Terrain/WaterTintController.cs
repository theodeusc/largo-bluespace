using Glitchers.EcoKnow.Sandbox.Grid;
using UnityEngine;

namespace Glitchers.EcoKnow.Sandbox.Terrain
{
    /// <summary>
    /// Runtime binder for the EcoKnow/Water shader. Owns per-instance clones of the
    /// seawater and freshwater materials so that colour / uniform changes (round
    /// transitions, future pollution-entity reactions, etc.) do not dirty the
    /// on-disk material assets.
    ///
    /// Lifecycle:
    ///   1. Awake — clone the seawater / freshwater materials referenced by the
    ///      injected <see cref="TerrainShaderConfig"/>.
    ///   2. <see cref="GetMaterialFor"/> — consulted by <see cref="DualGridTerrainRenderer"/>
    ///      so water tilemaps render through the cloned instances.
    ///   3. <see cref="BindElevationData"/> — called once per scenario load, immediately
    ///      after <see cref="ElevationMap.Generate"/>; binds the altitude texture and
    ///      bathymetry uniforms.
    ///   4. <see cref="SetSeawaterTint"/> / <see cref="SetFreshwaterTint"/> /
    ///      <see cref="ResetToDefaults"/> — runtime entry points for future
    ///      entity-count-driven tinting (e.g. pollution).
    /// </summary>
    public class WaterTintController : MonoBehaviour
    {
        public static WaterTintController Instance { get; private set; }

        [Header("Source")]
        [SerializeField] private TerrainShaderConfig _shaderConfig;

        // Asset references resolved from the config at Awake. Kept so ResetToDefaults
        // can re-read the shipped values without relying on a serialized snapshot.
        private Material _seaAsset;
        private Material _freshAsset;

        // Per-instance clones — what TilemapRenderers actually render through and
        // what BindElevationData / SetSeawaterTint / SetFreshwaterTint mutate.
        private Material _seaInstance;
        private Material _freshInstance;

        public Material SeawaterMaterial => _seaInstance;
        public Material FreshwaterMaterial => _freshInstance;

        // Asset-default colour accessors used by PollutantColorDriver as the "0% polluted"
        // anchor when lerping toward brown. Reading from _seaAsset / _freshAsset so the
        // anchor never drifts as we mutate the per-instance materials each round.
        public Color SeawaterDefaultShallow => _seaAsset != null ? _seaAsset.GetColor(IdShallowColor) : new Color(0.32f, 0.62f, 0.78f, 1f);
        public Color SeawaterDefaultDeep => _seaAsset != null ? _seaAsset.GetColor(IdDeepColor) : new Color(0.08f, 0.18f, 0.35f, 1f);
        public Color SeawaterDefaultBaseTint => _seaAsset != null ? _seaAsset.GetColor(IdBaseTint) : Color.white;
        public Color SeawaterDefaultCaustic => _seaAsset != null ? _seaAsset.GetColor(IdCausticColor) : new Color(0.45f, 0.74f, 0.77f, 0.5f);
        public Color FreshwaterDefaultShallow => _freshAsset != null ? _freshAsset.GetColor(IdShallowColor) : new Color(0.32f, 0.62f, 0.78f, 1f);
        public Color FreshwaterDefaultDeep => _freshAsset != null ? _freshAsset.GetColor(IdDeepColor) : new Color(0.08f, 0.18f, 0.35f, 1f);
        public Color FreshwaterDefaultBaseTint => _freshAsset != null ? _freshAsset.GetColor(IdBaseTint) : Color.white;
        public Color FreshwaterDefaultCaustic => _freshAsset != null ? _freshAsset.GetColor(IdCausticColor) : new Color(0.27f, 0.6f, 0.64f, 0.5f);
        public Color OverflowDefaultTint => _seaAsset != null ? _seaAsset.GetColor(IdOverflowTintColor) : new Color(0.45f, 0.28f, 0.1f, 1f);
        public float OverflowDefaultStrength => _seaAsset != null ? _seaAsset.GetFloat(IdOverflowTintStrength) : 1f;

        // Shader property IDs — resolved once for cheaper SetX calls.
        private static readonly int IdShallowColor = Shader.PropertyToID("_ShallowColor");
        private static readonly int IdDeepColor = Shader.PropertyToID("_DeepColor");
        private static readonly int IdBaseTint = Shader.PropertyToID("_BaseTint");
        private static readonly int IdAltitudeTex = Shader.PropertyToID("_AltitudeTex");
        private static readonly int IdGridOrigin = Shader.PropertyToID("_GridOrigin");
        private static readonly int IdGridWorldSize = Shader.PropertyToID("_GridWorldSize");
        private static readonly int IdAltitudeTexSize = Shader.PropertyToID("_AltitudeTexSize");
        private static readonly int IdOctaveFreqs = Shader.PropertyToID("_OctaveFreqs");
        private static readonly int IdOctaveAmps = Shader.PropertyToID("_OctaveAmps");
        private static readonly int IdOctaveOffset0 = Shader.PropertyToID("_OctaveOffset0");
        private static readonly int IdOctaveOffset1 = Shader.PropertyToID("_OctaveOffset1");
        private static readonly int IdOctaveOffset2 = Shader.PropertyToID("_OctaveOffset2");
        private static readonly int IdDepthBase = Shader.PropertyToID("_DepthBase");
        private static readonly int IdDepthAmplitude = Shader.PropertyToID("_DepthAmplitude");
        private static readonly int IdDepthCenter = Shader.PropertyToID("_DepthCenter");
        private static readonly int IdPixelization = Shader.PropertyToID("_Pixelization");
        private static readonly int IdWaterType = Shader.PropertyToID("_WaterType");
        private static readonly int IdDiffusionFalloffCells = Shader.PropertyToID("_DiffusionFalloffCells");
        private static readonly int IdDiffusionTintColor = Shader.PropertyToID("_DiffusionTintColor");
        private static readonly int IdDiffusionTintStrength = Shader.PropertyToID("_DiffusionTintStrength");
        private static readonly int IdOverflowMap = Shader.PropertyToID("_OverflowMap");
        private static readonly int IdOverflowTintColor = Shader.PropertyToID("_OverflowTintColor");
        private static readonly int IdOverflowTintStrength = Shader.PropertyToID("_OverflowTintStrength");
        private static readonly int IdFreshSandMap = Shader.PropertyToID("_FreshSandMap");
        private static readonly int IdCausticColor = Shader.PropertyToID("_CausticColor");

        // Caustic pixel-snap density per cell. Matches the original water rendering pipeline.
        private const float PatchPixels = 32f;

        private void Awake()
        {
            Instance = this;
            CloneFromConfig();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_seaInstance != null) Destroy(_seaInstance);
            if (_freshInstance != null) Destroy(_freshInstance);
        }

        private void CloneFromConfig()
        {
            if (_shaderConfig == null)
            {
                Debug.LogWarning("[WaterTintController] No TerrainShaderConfig assigned; water materials will not be cloned.");
                return;
            }

            _seaAsset = _shaderConfig.GetMaterial(TilesetConstants.Sea);
            _freshAsset = _shaderConfig.GetMaterial(TilesetConstants.Freshwater);

            if (_seaAsset != null)
            {
                _seaInstance = new Material(_seaAsset) { name = _seaAsset.name + " (Instance)" };
                _seaInstance.SetFloat(IdWaterType, 0f);
            }
            if (_freshAsset != null)
            {
                _freshInstance = new Material(_freshAsset) { name = _freshAsset.name + " (Instance)" };
                _freshInstance.SetFloat(IdWaterType, 1f);
            }
        }

        /// <summary>
        /// Returns the per-instance material to use for the given terrain, or null if the
        /// terrain is not water (caller should fall back to <see cref="TerrainShaderConfig.GetMaterial"/>).
        /// </summary>
        public Material GetMaterialFor(string terrain)
        {
            if (terrain == TilesetConstants.Sea) return _seaInstance;
            if (terrain == TilesetConstants.Freshwater) return _freshInstance;
            return null;
        }

        /// <summary>
        /// Binds the altitude texture, grid bounds, and bathymetry parameters onto the
        /// seawater and freshwater material instances. Idempotent — call once per scenario load.
        /// </summary>
        public void BindElevationData(ElevationMap map, GridManager gm)
        {
            if (map == null || gm == null) return;
            if (_seaInstance == null && _freshInstance == null) return;

            Texture2D altitudeTex = map.AltitudeTexture;
            int cols = map.Columns;
            int rows = map.Rows;
            float cellStep = gm.CellScale.x + gm.CellGap;

            // Match the historical formula: the dual-grid renderer parents the visual
            // tilemaps under CellContainerTransform with the renderer root offset by
            // step*(-0.5,-0.5,0). The grid's world-space bottom-left lands at
            // container + step*(-0.5, -(rows-0.5), 0); world-space size is
            // (cols*step, rows*step). Drift here misaligns altitude UV.
            Transform container = gm.CellContainerTransform;
            Vector3 originLocal = new Vector3(
                -cellStep * 0.5f,
                -cellStep * (rows - 0.5f),
                0f
            );
            Vector3 originWorld = container != null
                ? container.TransformPoint(originLocal)
                : originLocal;
            Vector4 gridOrigin = new Vector4(originWorld.x, originWorld.y, 0f, 0f);
            Vector4 gridWorldSize = new Vector4(cols * cellStep, rows * cellStep, 0f, 0f);

            // _AltitudeTexSize is the grid size in CELLS (not texels). The shader uses it
            // to scale gridPos for octave-noise sampling — passing a texel-count value here
            // collapses the shelves/ridges/sediment hierarchy into high-frequency noise.
            Vector4 altitudeTexSize = new Vector4(cols, rows, 0f, 0f);

            // Caustic pixel-snap density per cell. Old project: PatchPixels / cellStep.
            float pixelization = cellStep > 0.0001f ? PatchPixels / cellStep : PatchPixels;

            WorldHeightSampler sampler = map.HeightSampler;
            Vector4 octaveFreqs = Vector4.zero;
            Vector4 octaveAmps = Vector4.zero;
            Vector4 off0 = Vector4.zero, off1 = Vector4.zero, off2 = Vector4.zero;
            float depthBase = 0f, depthAmp = 0f, depthCenter = 0f;
            if (sampler != null)
            {
                WorldHeightSampler.NoiseOctave[] octs = sampler.Octaves;
                octaveFreqs = new Vector4(octs[0].Frequency, octs[1].Frequency, octs[2].Frequency, 0f);
                octaveAmps = new Vector4(octs[0].Amplitude, octs[1].Amplitude, octs[2].Amplitude, 0f);
                off0 = new Vector4(octs[0].OffsetX, octs[0].OffsetY, 0f, 0f);
                off1 = new Vector4(octs[1].OffsetX, octs[1].OffsetY, 0f, 0f);
                off2 = new Vector4(octs[2].OffsetX, octs[2].OffsetY, 0f, 0f);
                depthBase = sampler.DepthBase;
                depthAmp = sampler.DepthAmplitude;
                depthCenter = sampler.DepthCenter;
            }

            ApplyGeometry(_seaInstance, altitudeTex, gridOrigin, gridWorldSize, altitudeTexSize, pixelization);
            ApplyGeometry(_freshInstance, altitudeTex, gridOrigin, gridWorldSize, altitudeTexSize, pixelization);

            // Bind the overflow distance texture to the seawater instance — only
            // the sea branch of EK_Water samples it, but the binding lives here so
            // the texture follows the same scenario-load lifecycle as _AltitudeTex.
            Texture2D overflowTex = map.OverflowTexture;
            if (_seaInstance != null && overflowTex != null)
            {
                _seaInstance.SetTexture(IdOverflowMap, overflowTex);
            }

            // Bind the fresh-on-sand mask to the freshwater instance — only the
            // fresh branch samples it (drives the sandbed-show-through alpha
            // drop in zones where fresh and sand are stacked).
            Texture2D freshSandTex = map.FreshSandTexture;
            if (_freshInstance != null && freshSandTex != null)
            {
                _freshInstance.SetTexture(IdFreshSandMap, freshSandTex);
            }

            // Octaves & depth envelope. Both materials receive seeded offsets so the
            // sub-cell noise stays correlated with WorldHeightSampler. Seawater uses the
            // sampler's amplitudes / depth envelope; freshwater keeps its asset-defined
            // values (typically zeroed amps for a flat look — matching the constant
            // -0.1 altitude that ElevationMap stamps into freshwater cells).
            ApplyBathymetry(_seaInstance, octaveFreqs, octaveAmps, off0, off1, off2,
                depthBase, depthAmp, depthCenter);
            ApplyOffsetsOnly(_freshInstance, off0, off1, off2);
            SyncDiffusionTint();
        }

        private static void ApplyGeometry(Material mat, Texture2D altitudeTex,
            Vector4 gridOrigin, Vector4 gridWorldSize, Vector4 altitudeTexSize, float pixelization)
        {
            if (mat == null) return;
            if (altitudeTex != null) mat.SetTexture(IdAltitudeTex, altitudeTex);
            mat.SetVector(IdGridOrigin, gridOrigin);
            mat.SetVector(IdGridWorldSize, gridWorldSize);
            mat.SetVector(IdAltitudeTexSize, altitudeTexSize);
            mat.SetFloat(IdPixelization, pixelization);
        }

        private static void ApplyBathymetry(Material mat,
            Vector4 octFreqs, Vector4 octAmps, Vector4 off0, Vector4 off1, Vector4 off2,
            float depthBase, float depthAmp, float depthCenter)
        {
            if (mat == null) return;
            mat.SetVector(IdOctaveFreqs, octFreqs);
            mat.SetVector(IdOctaveAmps, octAmps);
            mat.SetVector(IdOctaveOffset0, off0);
            mat.SetVector(IdOctaveOffset1, off1);
            mat.SetVector(IdOctaveOffset2, off2);
            mat.SetFloat(IdDepthBase, depthBase);
            mat.SetFloat(IdDepthAmplitude, depthAmp);
            mat.SetFloat(IdDepthCenter, depthCenter);
        }

        private static void ApplyOffsetsOnly(Material mat, Vector4 off0, Vector4 off1, Vector4 off2)
        {
            if (mat == null) return;
            mat.SetVector(IdOctaveOffset0, off0);
            mat.SetVector(IdOctaveOffset1, off1);
            mat.SetVector(IdOctaveOffset2, off2);
        }

        private void SyncDiffusionTint()
        {
            if (_seaInstance == null) return;
            Color tint;
            if (_freshInstance != null)
            {
                tint = _freshInstance.GetColor(IdShallowColor);
            }
            else if (_freshAsset != null)
            {
                tint = _freshAsset.GetColor(IdShallowColor);
            }
            else
            {
                return;
            }
            _seaInstance.SetColor(IdDiffusionTintColor, tint);
        }

        /// <summary>
        /// Sets the seawater tint as the END point of a smooth lerp. The current material
        /// colours become the lerp's start point; the new values are reached after
        /// <see cref="ColorLerpDuration"/> seconds (advanced in <see cref="Update"/>).
        /// Caustic ride-alongs the same lerp so the sparkle warms toward muddy water in
        /// step with the shallow/deep transition.
        /// </summary>
        public void SetSeawaterTint(Color shallow, Color deep, Color baseTint, Color caustic)
        {
            if (_seaInstance == null) return;
            BeginColorLerp(ref _seaLerp, _seaInstance, shallow, deep, baseTint, caustic, ColorLerpDuration);
        }

        /// <summary>
        /// Sets the freshwater tint as the END point of a smooth lerp. The sea diffusion
        /// tint is re-synced each frame from the freshwater shallow colour, so it follows
        /// the lerp without an extra explicit hook.
        /// </summary>
        public void SetFreshwaterTint(Color shallow, Color deep, Color baseTint, Color caustic)
        {
            if (_freshInstance == null) return;
            BeginColorLerp(ref _freshLerp, _freshInstance, shallow, deep, baseTint, caustic, ColorLerpDuration);
        }

        /// <summary>
        /// Runtime entry point for entity-driven brown overflow tint. Mutates the
        /// seawater instance's _OverflowTintColor and _OverflowTintStrength via the
        /// same lerp mechanism as the shallow/deep tints; the shader only applies the
        /// tint where the overflow distance field is non-zero.
        /// </summary>
        public void SetOverflowTint(Color tintColor, float strength)
        {
            if (_seaInstance == null) return;
            _overflowLerp.ColorStart = _seaInstance.GetColor(IdOverflowTintColor);
            _overflowLerp.StrengthStart = _seaInstance.GetFloat(IdOverflowTintStrength);
            _overflowLerp.ColorEnd = tintColor;
            _overflowLerp.StrengthEnd = Mathf.Clamp01(strength);
            _overflowLerp.StartTime = Time.time;
            _overflowLerp.Duration = ColorLerpDuration;
            _overflowLerp.Active = true;
        }

        /// <summary>
        /// Restores the colour properties on each instance to the values shipped on
        /// the underlying asset. Cancels any in-flight lerps so the snap takes effect.
        /// Safe to call before <see cref="BindElevationData"/>.
        /// </summary>
        public void ResetToDefaults()
        {
            // Cancel any active lerps so the snap below isn't overridden by Update.
            _seaLerp.Active = false;
            _freshLerp.Active = false;
            _overflowLerp.Active = false;

            if (_seaInstance != null && _seaAsset != null)
            {
                _seaInstance.SetColor(IdShallowColor, _seaAsset.GetColor(IdShallowColor));
                _seaInstance.SetColor(IdDeepColor, _seaAsset.GetColor(IdDeepColor));
                _seaInstance.SetColor(IdBaseTint, _seaAsset.GetColor(IdBaseTint));
                _seaInstance.SetColor(IdCausticColor, _seaAsset.GetColor(IdCausticColor));
                _seaInstance.SetColor(IdOverflowTintColor, _seaAsset.GetColor(IdOverflowTintColor));
                _seaInstance.SetFloat(IdOverflowTintStrength, _seaAsset.GetFloat(IdOverflowTintStrength));
            }
            if (_freshInstance != null && _freshAsset != null)
            {
                _freshInstance.SetColor(IdShallowColor, _freshAsset.GetColor(IdShallowColor));
                _freshInstance.SetColor(IdDeepColor, _freshAsset.GetColor(IdDeepColor));
                _freshInstance.SetColor(IdBaseTint, _freshAsset.GetColor(IdBaseTint));
                _freshInstance.SetColor(IdCausticColor, _freshAsset.GetColor(IdCausticColor));
            }
            SyncDiffusionTint();
        }

        // --- Smooth colour transitions ---------------------------------------------------
        // Tint updates are routed through a per-frame lerp rather than applied instantly,
        // so when a new round establishes a different pollution state the water shifts
        // gradually toward the new target rather than snapping. The lerp restarts from
        // the current (possibly mid-animation) value on every SetX call, so repeated
        // changes chain cleanly without visible jumps.

        // Seconds to ease from the previous water colour to the latest target.
        // Not serialized — scene-baked overrides would otherwise persist across code changes.
        private const float ColorLerpDuration = 2f;

        private struct WaterColorLerp
        {
            public bool Active;
            public float StartTime;
            public float Duration;
            public Color ShallowStart, ShallowEnd;
            public Color DeepStart, DeepEnd;
            public Color BaseStart, BaseEnd;
            public Color CausticStart, CausticEnd;
        }

        private struct OverflowColorLerp
        {
            public bool Active;
            public float StartTime;
            public float Duration;
            public Color ColorStart, ColorEnd;
            public float StrengthStart, StrengthEnd;
        }

        private WaterColorLerp _seaLerp;
        private WaterColorLerp _freshLerp;
        private OverflowColorLerp _overflowLerp;

        private void BeginColorLerp(ref WaterColorLerp s, Material m, Color shallowEnd, Color deepEnd, Color baseEnd, Color causticEnd, float duration)
        {
            // Capture the material's current values as the lerp start; this handles
            // restart-mid-animation correctly (start = current, not previous target).
            s.ShallowStart = m.GetColor(IdShallowColor);
            s.DeepStart = m.GetColor(IdDeepColor);
            s.BaseStart = m.GetColor(IdBaseTint);
            s.CausticStart = m.GetColor(IdCausticColor);
            s.ShallowEnd = shallowEnd;
            s.DeepEnd = deepEnd;
            s.BaseEnd = baseEnd;
            s.CausticEnd = causticEnd;
            s.StartTime = Time.time;
            s.Duration = duration <= 0f ? 0.001f : duration;
            s.Active = true;
        }

        private void Update()
        {
            bool freshWasActive = _freshLerp.Active;
            AdvanceWaterLerp(ref _seaLerp, _seaInstance);
            AdvanceWaterLerp(ref _freshLerp, _freshInstance);
            AdvanceOverflowLerp(ref _overflowLerp, _seaInstance);

            // Sea's diffusion-tint binding mirrors freshwater's shallow colour. Sync each
            // frame while freshwater is animating, plus one extra frame after it completes
            // (freshWasActive but now inactive) so the final frame's value lands in the
            // diffusion tint too.
            if (freshWasActive) SyncDiffusionTint();
        }

        private void AdvanceWaterLerp(ref WaterColorLerp s, Material m)
        {
            if (!s.Active || m == null) return;
            float t = Mathf.Clamp01((Time.time - s.StartTime) / s.Duration);
            m.SetColor(IdShallowColor, Color.Lerp(s.ShallowStart, s.ShallowEnd, t));
            m.SetColor(IdDeepColor, Color.Lerp(s.DeepStart, s.DeepEnd, t));
            m.SetColor(IdBaseTint, Color.Lerp(s.BaseStart, s.BaseEnd, t));
            m.SetColor(IdCausticColor, Color.Lerp(s.CausticStart, s.CausticEnd, t));
            if (t >= 1f) s.Active = false;
        }

        private void AdvanceOverflowLerp(ref OverflowColorLerp s, Material m)
        {
            if (!s.Active || m == null) return;
            float t = Mathf.Clamp01((Time.time - s.StartTime) / s.Duration);
            m.SetColor(IdOverflowTintColor, Color.Lerp(s.ColorStart, s.ColorEnd, t));
            m.SetFloat(IdOverflowTintStrength, Mathf.Lerp(s.StrengthStart, s.StrengthEnd, t));
            if (t >= 1f) s.Active = false;
        }
    }
}
