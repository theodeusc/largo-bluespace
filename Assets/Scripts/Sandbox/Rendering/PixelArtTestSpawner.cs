using System.Collections.Generic;
using Glitchers.EcoKnow.Sandbox.Grid;
using Glitchers.EcoKnow.Sandbox.Grid.Regions;
using Glitchers.EcoKnow.Sandbox.Terrain;
using UnityEngine;

namespace Glitchers.EcoKnow.Sandbox.Rendering
{
    // One-off test harness that, on every scenario start, places three groups of
    // pixel-art sprites on the map:
    //   • one seal per Beach region (snapped near the region centre)
    //   • 100 oysters scattered across every Seawater region, biased toward shoreline
    //   • 100 seagrass clumps scattered across every Seawater region, biased toward shoreline
    //
    // Auto-bootstraps via [RuntimeInitializeOnLoadMethod] so no scene edits are needed.
    // Once SandboxManager exists it subscribes to OnScenarioReady and runs its placement
    // each time a scenario loads.
    //
    // This is intentionally a TEST harness — it does not register simulated entities
    // with EntityManager. Populations don't change at runtime, the sprites are static
    // visual props. The same renderer can later be driven by real Entity records.
    public class PixelArtTestSpawner : MonoBehaviour
    {
        private const string LogChannel = "[PixelArtTestSpawner]";

        // Zone IDs (mirror RegionComputeManager's private constants for the two zones
        // this spawner targets; everything else is read via RegionComputeManager).
        private const int ZONE_SEAWATER = 0;
        private const int ZONE_SAND = 1;

        // Resources/ paths for the three test sprites. Loaded once per scenario start.
        private const string SealResource = "PixelArt/Entities/seal";
        private const string SeagrassResource = "PixelArt/Entities/seagrass";
        private const string OysterResource = "PixelArt/Entities/oyster";

        private const string SealGroupId = "test_seal";
        private const string SeagrassGroupId = "test_seagrass";
        private const string OysterGroupId = "test_oyster";

        // Visual sizes in world units (1 unit = 1 cell). Seals are big, oysters tiny.
        private const float SealWorldSize = 0.7f;
        private const float SeagrassWorldSize = 0.35f;
        private const float OysterWorldSize = 0.18f;

        // Sprite counts per region. Seal is 1-per-region; the others are total per
        // seawater region (distributed across cells via the sampler).
        private const int OystersPerSeaRegion = 100;
        private const int SeagrassPerSeaRegion = 100;

        // Distribution tuning. BeachAttraction = 0 is uniform; larger values pull
        // entities toward the shore. BeachDistanceScale sets the falloff reach in cells.
        private static readonly PixelArtDistributionSampler.Settings OysterSettings = new PixelArtDistributionSampler.Settings
        {
            PerlinScale = 1.8f,
            BeachAttraction = 2.5f,
            BeachDistanceScale = 4f
        };
        private static readonly PixelArtDistributionSampler.Settings SeagrassSettings = new PixelArtDistributionSampler.Settings
        {
            PerlinScale = 1.2f,
            BeachAttraction = 1.5f,
            BeachDistanceScale = 6f
        };

        // Tracks the specific SandboxManager our delegate is attached to so we can detect
        // scene-reload cases where the previous manager was destroyed and a fresh one took
        // its place. C# delegates hold strong refs but Unity-destroyed components evaluate
        // as null via `==`, which is what we test for on every scene load.
        private SandboxManager _subscribedTo;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            // Skip in scenes that don't host SandboxManager (e.g. scene_Start). The
            // spawner re-bootstraps via SceneManager.sceneLoaded so subsequent loads of
            // scene_Sandbox still wire up.
            if (FindFirstObjectByType<PixelArtTestSpawner>() != null) return;
            GameObject go = new GameObject(nameof(PixelArtTestSpawner));
            go.AddComponent<PixelArtTestSpawner>();
            DontDestroyOnLoad(go);
        }

        private void OnEnable()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
            TrySubscribe();
        }

        private void OnDisable()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            if (_subscribedTo != null)
            {
                _subscribedTo.OnScenarioReady -= HandleScenarioReady;
                _subscribedTo = null;
            }
        }

        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            TrySubscribe();
        }

        private void TrySubscribe()
        {
            // Skip SandboxManager.Instance — its lazy init is gated on `instance` being
            // non-null, but the static field isn't populated until something else first
            // touches the property. sceneLoaded fires before any such access, so we'd
            // miss the live manager. Going through FindFirstObjectByType also avoids the
            // error log Instance prints when no manager is in the scene (e.g. scene_Start).
            SandboxManager mgr = FindFirstObjectByType<SandboxManager>();
            if (mgr == _subscribedTo) return; // Already attached to the live manager.

            // Detach from the previous manager (no-op if null or Unity-destroyed).
            if (_subscribedTo != null)
            {
                _subscribedTo.OnScenarioReady -= HandleScenarioReady;
            }
            _subscribedTo = mgr;
            if (mgr != null)
            {
                mgr.OnScenarioReady += HandleScenarioReady;
                Debug.Log($"{LogChannel} Subscribed to SandboxManager.OnScenarioReady.");
            }
        }

        private void HandleScenarioReady()
        {
            SandboxManager sandbox = SandboxManager.Instance;
            GridManager grid = sandbox != null ? sandbox.GridManager : null;
            RegionComputeManager regions = sandbox != null ? sandbox.RegionComputeManager : null;
            if (grid == null)
            {
                Debug.LogWarning($"{LogChannel} OnScenarioReady fired without a GridManager — skipping pixel-art test placement.");
                return;
            }
            if (regions == null || !regions.IsActive)
            {
                Debug.LogWarning($"{LogChannel} Active scenario has no RegionComputeManager (UseRegionWideCompute is off). Pixel-art test placement skipped.");
                return;
            }

            Sprite sealSprite = Resources.Load<Sprite>(SealResource);
            Sprite seagrassSprite = Resources.Load<Sprite>(SeagrassResource);
            Sprite oysterSprite = Resources.Load<Sprite>(OysterResource);
            if (sealSprite == null || seagrassSprite == null || oysterSprite == null)
            {
                Debug.LogError($"{LogChannel} Failed to load one or more pixel-art sprites from Resources/PixelArt/Entities/. seal={sealSprite}, seagrass={seagrassSprite}, oyster={oysterSprite}. Skipping placement.");
                return;
            }

            int beachRegions = 0;
            int seaRegions = 0;
            foreach (int rid in regions.AllRegionIds)
            {
                RegionType t = regions.GetRegionType(rid);
                if (t == RegionType.Beach) beachRegions++;
                else if (t == RegionType.Seawater) seaRegions++;
            }
            Debug.Log($"{LogChannel} OnScenarioReady: {beachRegions} Beach region(s), {seaRegions} Seawater region(s). Placing seal/oyster/seagrass.");

            // Single deterministic RNG sourced from the scenario seed (Random.InitState
            // already ran in SandboxManager). Using a fresh System.Random keeps our
            // jitter draws decoupled from gameplay's UnityEngine.Random sequence.
            System.Random rng = new System.Random(Random.Range(int.MinValue, int.MaxValue));

            PlaceSeals(regions, grid, sealSprite, rng);
            PlaceWaterEntity(
                regions, grid, oysterSprite,
                groupId: OysterGroupId,
                renderAboveTerrain: TilesetConstants.Sand,
                count: OystersPerSeaRegion,
                worldSize: OysterWorldSize,
                settings: OysterSettings,
                rng: rng);
            PlaceWaterEntity(
                regions, grid, seagrassSprite,
                groupId: SeagrassGroupId,
                renderAboveTerrain: TilesetConstants.Sand,
                count: SeagrassPerSeaRegion,
                worldSize: SeagrassWorldSize,
                settings: SeagrassSettings,
                rng: rng);
        }

        private void PlaceSeals(RegionComputeManager regions, GridManager grid, Sprite sprite, System.Random rng)
        {
            List<Vector3> positions = new List<Vector3>();
            GridCoords coords = grid.Coords;
            float cellStep = coords.CellStep;

            foreach (int regionId in regions.AllRegionIds)
            {
                if (regions.GetRegionType(regionId) != RegionType.Beach) continue;
                if (!regions.TryGetCenterCell(regionId, out (int col, int row) center)) continue;

                // Seal sits roughly at the region centre but with a small jitter so
                // repeated runs aren't visually identical even though the centre cell is.
                Vector3 cellCenter = coords.CellToWorld(center.col, center.row);
                float jitterX = (float)(rng.NextDouble() - 0.5) * cellStep * 0.4f;
                float jitterY = (float)(rng.NextDouble() - 0.5) * cellStep * 0.4f;
                positions.Add(new Vector3(cellCenter.x + jitterX, cellCenter.y + jitterY, 0f));
            }

            PixelArtEntityRenderer.Instance.RegisterGroup(new PixelArtEntityRenderer.GroupRequest
            {
                GroupId = SealGroupId,
                Sprite = sprite,
                Positions = positions,
                // Seal sits on beach: above sand, no overlapping water layer in beach regions.
                RenderAboveTerrain = TilesetConstants.Sand,
                WorldSize = SealWorldSize,
                Tint = Color.white
            });
        }

        private void PlaceWaterEntity(
            RegionComputeManager regions,
            GridManager grid,
            Sprite sprite,
            string groupId,
            string renderAboveTerrain,
            int count,
            float worldSize,
            PixelArtDistributionSampler.Settings settings,
            System.Random rng)
        {
            List<Vector3> allPositions = new List<Vector3>();
            foreach (int regionId in regions.AllRegionIds)
            {
                if (regions.GetRegionType(regionId) != RegionType.Seawater) continue;
                IReadOnlyList<(int col, int row)> regionCells = regions.GetRegionCells(regionId);
                if (regionCells.Count == 0) continue;

                List<Vector3> regionPositions = PixelArtDistributionSampler.Sample(
                    regionCells, count, grid, ZONE_SAND, settings, rng);
                allPositions.AddRange(regionPositions);
            }

            PixelArtEntityRenderer.Instance.RegisterGroup(new PixelArtEntityRenderer.GroupRequest
            {
                GroupId = groupId,
                Sprite = sprite,
                Positions = allPositions,
                RenderAboveTerrain = renderAboveTerrain,
                WorldSize = worldSize,
                Tint = Color.white
            });
        }
    }
}
