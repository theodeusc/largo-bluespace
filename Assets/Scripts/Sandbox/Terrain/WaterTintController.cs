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
            }
            if (_freshAsset != null)
            {
                _freshInstance = new Material(_freshAsset) { name = _freshAsset.name + " (Instance)" };
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

            // Octaves & depth envelope. Both materials receive seeded offsets so the
            // sub-cell noise stays correlated with WorldHeightSampler. Seawater uses the
            // sampler's amplitudes / depth envelope; freshwater keeps its asset-defined
            // values (typically zeroed amps for a flat look — matching the constant
            // -0.1 altitude that ElevationMap stamps into freshwater cells).
            ApplyBathymetry(_seaInstance, octaveFreqs, octaveAmps, off0, off1, off2,
                depthBase, depthAmp, depthCenter);
            ApplyOffsetsOnly(_freshInstance, off0, off1, off2);
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

        public void SetSeawaterTint(Color shallow, Color deep, Color baseTint)
        {
            if (_seaInstance == null) return;
            _seaInstance.SetColor(IdShallowColor, shallow);
            _seaInstance.SetColor(IdDeepColor, deep);
            _seaInstance.SetColor(IdBaseTint, baseTint);
        }

        public void SetFreshwaterTint(Color shallow, Color deep, Color baseTint)
        {
            if (_freshInstance == null) return;
            _freshInstance.SetColor(IdShallowColor, shallow);
            _freshInstance.SetColor(IdDeepColor, deep);
            _freshInstance.SetColor(IdBaseTint, baseTint);
        }

        /// <summary>
        /// Restores the colour properties on each instance to the values shipped on
        /// the underlying asset. Safe to call before <see cref="BindElevationData"/>.
        /// </summary>
        public void ResetToDefaults()
        {
            if (_seaInstance != null && _seaAsset != null)
            {
                _seaInstance.SetColor(IdShallowColor, _seaAsset.GetColor(IdShallowColor));
                _seaInstance.SetColor(IdDeepColor, _seaAsset.GetColor(IdDeepColor));
                _seaInstance.SetColor(IdBaseTint, _seaAsset.GetColor(IdBaseTint));
            }
            if (_freshInstance != null && _freshAsset != null)
            {
                _freshInstance.SetColor(IdShallowColor, _freshAsset.GetColor(IdShallowColor));
                _freshInstance.SetColor(IdDeepColor, _freshAsset.GetColor(IdDeepColor));
                _freshInstance.SetColor(IdBaseTint, _freshAsset.GetColor(IdBaseTint));
            }
        }
    }
}
