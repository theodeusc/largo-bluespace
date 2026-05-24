using System.Collections.Generic;
using Glitchers.EcoKnow.Sandbox.Grid;
using Glitchers.EcoKnow.Sandbox.Grid.Regions;
using Glitchers.EcoKnow.Sandbox.Terrain;
using UnityEngine;

namespace Glitchers.EcoKnow.Sandbox.Rendering
{
    // Test harness that places four groups of pixel-art sprites on the map:
    //   • one seal per Beach region
    //   • OystersPerSeaRegion oysters per Seawater region, biased toward shoreline
    //   • SeagrassPerSeaRegion seagrass clumps per Seawater region, biased toward shoreline
    //   • up to MaxDisplayedBottles bottles ("beach litter") across all Beach cells, count
    //     normalised from the summed `litter` populations of the Seawater + Freshwater +
    //     Beach compute cells
    //
    // Auto-bootstraps via [RuntimeInitializeOnLoadMethod] so no scene edits are needed.
    //
    // Placement is persistent: each round we compute a target count per group and only
    // ADD positions when the target rises or REMOVE the most-recently-added positions
    // when it falls. Existing sprites stay where they were placed — nothing gets
    // relocated round-to-round. For fixed-count groups (seal/oyster/seagrass) the delta
    // is always zero after the initial round, so RegisterGroup runs at most once and the
    // sprites never flicker.
    //
    // OnScenarioReady clears the persistent state (region IDs from the previous scenario
    // are invalid) and force-registers empty groups to wipe any leftover sprites; the
    // first OnRoundAdvanced after that populates the groups for the new scenario.
    public class PixelArtTestSpawner : MonoBehaviour
    {
        private const string LogChannel = "[PixelArtTestSpawner]";

        // Zone IDs (mirror RegionComputeManager's private constants for the two zones
        // this spawner targets; everything else is read via RegionComputeManager).
        private const int ZONE_SEAWATER = 0;
        private const int ZONE_SAND = 1;

        // Resources/ paths for the test sprites. Unity caches Resources.Load so re-loading
        // each round is cheap.
        private const string SealResource = "PixelArt/Entities/seal";
        private const string SeagrassResource = "PixelArt/Entities/seagrass";
        private const string OysterResource = "PixelArt/Entities/oyster";
        private const string BottleResource = "PixelArt/Entities/bottle";

        private const string SealGroupId = "test_seal";
        private const string SeagrassGroupId = "test_seagrass";
        private const string OysterGroupId = "test_oyster";
        private const string BottleGroupId = "test_bottle";

        // Visual sizes in world units (1 unit = 1 cell). Seals are big, oysters tiny.
        private const float SealWorldSize = 2.1f;
        private const float SeagrassWorldSize = 0.35f;
        private const float OysterWorldSize = 0.18f;
        private const float BottleWorldSize = 0.3f;

        // Per-region counts for fixed-count groups. One seal per beach region;
        // OystersPerSeaRegion / SeagrassPerSeaRegion per seawater region.
        private const int SealPerBeachRegion = 1;
        private const int OystersPerSeaRegion = 100;
        private const int SeagrassPerSeaRegion = 100;

        // Beach-litter normalisation. The total `litter` population summed across the
        // compute cells of every Seawater + Freshwater + Beach region is divided by
        // ExpectedMaxLitterTotal, clamped to [0,1], then multiplied by MaxDisplayedBottles
        // to give the global target bottle count for the current round. Pick
        // ExpectedMaxLitterTotal to match the "high litter" total you want to read as a
        // visually saturated beach — easy to retune by editing this constant.
        private const string LitterEntityId = "litter";
        private const int MaxDisplayedBottles = 50;
        private const float ExpectedMaxLitterTotal = 20.0f;

        // Max ±tilt for bottle sprites in degrees. Stays well below 90° so bottles read
        // as lying flat on the sand, never standing upright.
        private const float BottleMaxTiltDegrees = 40f;

        // Edge buffer (in cells) applied uniformly to every entity-placement Settings.
        // Keeps sprites pulled inward from the region boundary so e.g. bottles don't sit
        // on the sand-water boundary and oysters don't sit on the sea-sand boundary.
        // Falls back to no buffer when the region is too small to satisfy it.
        private const int EntityEdgeBufferCells = 1;

        // Distribution tuning. Oysters and seagrass attract toward the beach (zone 1);
        // seals and bottles use uniform distribution within their region (attraction off).
        // All four use the same EntityEdgeBufferCells so no sprite sits on a region edge.
        private static readonly PixelArtDistributionSampler.Settings OysterSettings = new PixelArtDistributionSampler.Settings
        {
            PerlinScale = 1.8f,
            BeachAttraction = 2.5f,
            BeachDistanceScale = 4f,
            AttractionZoneId = ZONE_SAND,
            EdgeBufferCells = EntityEdgeBufferCells
        };
        private static readonly PixelArtDistributionSampler.Settings SeagrassSettings = new PixelArtDistributionSampler.Settings
        {
            PerlinScale = 1.2f,
            BeachAttraction = 1.5f,
            BeachDistanceScale = 6f,
            AttractionZoneId = ZONE_SAND,
            EdgeBufferCells = EntityEdgeBufferCells
        };
        private static readonly PixelArtDistributionSampler.Settings SealSettings = new PixelArtDistributionSampler.Settings
        {
            PerlinScale = 1.8f,
            BeachAttraction = 0f,
            EdgeBufferCells = EntityEdgeBufferCells
        };
        private static readonly PixelArtDistributionSampler.Settings BottleSettings = new PixelArtDistributionSampler.Settings
        {
            PerlinScale = 1.5f,
            BeachAttraction = 0f,
            EdgeBufferCells = EntityEdgeBufferCells
        };

        // Persistent placement state. Per-region groups (seal/oyster/seagrass) keep their
        // positions in a dictionary keyed by region id; the bottle group is global. We
        // never overwrite an existing position — only append on growth and trim the tail
        // on shrinkage — so sprite GameObjects survive across rounds without flicker.
        private readonly Dictionary<int, List<Vector3>> _sealPositions = new Dictionary<int, List<Vector3>>();
        private readonly Dictionary<int, List<Vector3>> _oysterPositions = new Dictionary<int, List<Vector3>>();
        private readonly Dictionary<int, List<float>> _oysterRotations = new Dictionary<int, List<float>>();
        private readonly Dictionary<int, List<Vector3>> _seagrassPositions = new Dictionary<int, List<Vector3>>();
        private readonly List<Vector3> _bottlePositions = new List<Vector3>();
        private readonly List<float> _bottleRotations = new List<float>();

        // Tracks the specific SandboxManager our delegates are attached to so we can
        // detect scene-reload cases where the previous manager was destroyed and a fresh
        // one took its place. C# delegates hold strong refs but Unity-destroyed components
        // evaluate as null via `==`, which is what we test for on every scene load.
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
                _subscribedTo.OnRoundAdvanced -= HandleRoundAdvanced;
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
                _subscribedTo.OnRoundAdvanced -= HandleRoundAdvanced;
            }
            _subscribedTo = mgr;
            if (mgr != null)
            {
                mgr.OnScenarioReady += HandleScenarioReady;
                mgr.OnRoundAdvanced += HandleRoundAdvanced;
                Debug.Log($"{LogChannel} Subscribed to SandboxManager.OnScenarioReady + OnRoundAdvanced.");
            }
        }

        private void HandleScenarioReady()
        {
            // Region IDs from the previous scenario (if any) are invalid in the new one —
            // wipe the per-region position maps and the global bottle list. The first
            // OnRoundAdvanced after this populates them from scratch.
            _sealPositions.Clear();
            _oysterPositions.Clear();
            _oysterRotations.Clear();
            _seagrassPositions.Clear();
            _bottlePositions.Clear();
            _bottleRotations.Clear();

            // Force-register empty groups so any leftover sprites from the previous
            // scenario disappear immediately. Without this, a smaller new scenario
            // could leave stale sprites visible until the first delta>0 RegisterGroup.
            Sprite seal = Resources.Load<Sprite>(SealResource);
            Sprite oyster = Resources.Load<Sprite>(OysterResource);
            Sprite seagrass = Resources.Load<Sprite>(SeagrassResource);
            Sprite bottle = Resources.Load<Sprite>(BottleResource);
            if (seal != null) ClearGroup(SealGroupId, seal, SealWorldSize, renderAboveAllTerrain: true, renderAboveTerrain: null);
            if (oyster != null) ClearGroup(OysterGroupId, oyster, OysterWorldSize, renderAboveAllTerrain: false, renderAboveTerrain: TilesetConstants.Sand);
            if (seagrass != null) ClearGroup(SeagrassGroupId, seagrass, SeagrassWorldSize, renderAboveAllTerrain: false, renderAboveTerrain: TilesetConstants.Sand);
            if (bottle != null) ClearGroup(BottleGroupId, bottle, BottleWorldSize, renderAboveAllTerrain: true, renderAboveTerrain: null);
        }

        private void HandleRoundAdvanced()
        {
            SandboxManager sandbox = SandboxManager.Instance;
            GridManager grid = sandbox != null ? sandbox.GridManager : null;
            RegionComputeManager regions = sandbox != null ? sandbox.RegionComputeManager : null;
            EntityManager entities = sandbox != null ? sandbox.EntityManager : null;
            if (grid == null)
            {
                Debug.LogWarning($"{LogChannel} OnRoundAdvanced fired without a GridManager — skipping placement.");
                return;
            }
            if (regions == null || !regions.IsActive)
            {
                Debug.LogWarning($"{LogChannel} Active scenario has no RegionComputeManager (UseRegionWideCompute is off). Placement skipped.");
                return;
            }

            Sprite sealSprite = Resources.Load<Sprite>(SealResource);
            Sprite seagrassSprite = Resources.Load<Sprite>(SeagrassResource);
            Sprite oysterSprite = Resources.Load<Sprite>(OysterResource);
            Sprite bottleSprite = Resources.Load<Sprite>(BottleResource);
            if (sealSprite == null || seagrassSprite == null || oysterSprite == null || bottleSprite == null)
            {
                Debug.LogError($"{LogChannel} Failed to load one or more pixel-art sprites from Resources/PixelArt/Entities/. seal={sealSprite}, seagrass={seagrassSprite}, oyster={oysterSprite}, bottle={bottleSprite}.");
                return;
            }

            // Fresh System.Random per round. Decoupled from gameplay's UnityEngine.Random
            // sequence. Only consumed when a group's target count grew this round —
            // existing positions never get re-rolled.
            System.Random rng = new System.Random(Random.Range(int.MinValue, int.MaxValue));

            AdjustSeals(regions, grid, sealSprite, rng);
            AdjustOysters(regions, grid, oysterSprite, rng);
            AdjustSeagrass(regions, grid, seagrassSprite, rng);
            AdjustBottles(regions, grid, entities, bottleSprite, rng);
        }

        private void AdjustSeals(RegionComputeManager regions, GridManager grid, Sprite sprite, System.Random rng)
        {
            bool changed = AdjustPerRegionGroup(
                regions, grid, rng,
                targetType: RegionType.Beach,
                targetCountPerRegion: SealPerBeachRegion,
                settings: SealSettings,
                rotationsByRegion: null,
                positionsByRegion: _sealPositions,
                out List<Vector3> allPositions, out _);
            if (!changed) return;

            PixelArtEntityRenderer.Instance.RegisterGroup(new PixelArtEntityRenderer.GroupRequest
            {
                GroupId = SealGroupId,
                Sprite = sprite,
                Positions = allPositions,
                // Seal renders above every terrain layer so it isn't occluded by sand,
                // grass, or water as it wanders across the shoreline.
                RenderAboveAllTerrain = true,
                WorldSize = SealWorldSize,
                Tint = Color.white
            });
        }

        private void AdjustOysters(RegionComputeManager regions, GridManager grid, Sprite sprite, System.Random rng)
        {
            bool changed = AdjustPerRegionGroup(
                regions, grid, rng,
                targetType: RegionType.Seawater,
                targetCountPerRegion: OystersPerSeaRegion,
                settings: OysterSettings,
                rotationsByRegion: _oysterRotations,
                positionsByRegion: _oysterPositions,
                out List<Vector3> allPositions, out List<float> allRotations);
            if (!changed) return;

            PixelArtEntityRenderer.Instance.RegisterGroup(new PixelArtEntityRenderer.GroupRequest
            {
                GroupId = OysterGroupId,
                Sprite = sprite,
                Positions = allPositions,
                Rotations = allRotations,
                RenderAboveTerrain = TilesetConstants.Sand,
                WorldSize = OysterWorldSize,
                Tint = Color.white
            });
        }

        private void AdjustSeagrass(RegionComputeManager regions, GridManager grid, Sprite sprite, System.Random rng)
        {
            bool changed = AdjustPerRegionGroup(
                regions, grid, rng,
                targetType: RegionType.Seawater,
                targetCountPerRegion: SeagrassPerSeaRegion,
                settings: SeagrassSettings,
                rotationsByRegion: null,
                positionsByRegion: _seagrassPositions,
                out List<Vector3> allPositions, out _);
            if (!changed) return;

            PixelArtEntityRenderer.Instance.RegisterGroup(new PixelArtEntityRenderer.GroupRequest
            {
                GroupId = SeagrassGroupId,
                Sprite = sprite,
                Positions = allPositions,
                RenderAboveTerrain = TilesetConstants.Sand,
                WorldSize = SeagrassWorldSize,
                Tint = Color.white
            });
        }

        // Adjusts up to MaxDisplayedBottles bottle sprites distributed across all Beach
        // cells. Count is driven by the summed `litter` populations of every Seawater +
        // Freshwater + Beach compute cell, normalised against ExpectedMaxLitterTotal.
        // Tilt is randomised in [-BottleMaxTiltDegrees, +BottleMaxTiltDegrees] for new
        // bottles only; existing bottles keep their original tilt across rounds.
        private void AdjustBottles(
            RegionComputeManager regions,
            GridManager grid,
            EntityManager entities,
            Sprite sprite,
            System.Random rng)
        {
            if (entities == null)
            {
                Debug.LogWarning($"{LogChannel} EntityManager unavailable — leaving bottles unchanged this round.");
                return;
            }

            int idxLitter = entities.GetEntityIndex(LitterEntityId);
            if (idxLitter < 0)
            {
                if (_bottlePositions.Count > 0)
                {
                    _bottlePositions.Clear();
                    _bottleRotations.Clear();
                    ClearGroup(BottleGroupId, sprite, BottleWorldSize, renderAboveAllTerrain: true, renderAboveTerrain: null);
                }
                return;
            }

            // Sum litter across the compute cell of every Seawater + Freshwater + Beach
            // region in a single pass, also collecting all Beach cells for placement.
            // Aliased regions (e.g. estuary -> freshwater) are skipped so their population
            // isn't double-counted via their alias target.
            long totalLitter = 0L;
            List<(int col, int row)> allBeachCells = new List<(int col, int row)>();
            foreach (int regionId in regions.AllRegionIds)
            {
                RegionType t = regions.GetRegionType(regionId);
                bool isSampledRegion = t == RegionType.Seawater || t == RegionType.Freshwater || t == RegionType.Beach;
                if (isSampledRegion && !regions.IsAliased(regionId) && regions.TryGetCenterCell(regionId, out var c))
                {
                    totalLitter += System.Math.Max(0L, entities.RawGetPopulation(c.col, c.row, idxLitter));
                }
                if (t == RegionType.Beach)
                {
                    IReadOnlyList<(int col, int row)> cells = regions.GetRegionCells(regionId);
                    for (int i = 0; i < cells.Count; i++) allBeachCells.Add(cells[i]);
                }
            }

            float normalised = ExpectedMaxLitterTotal > 0f
                ? Mathf.Clamp01(totalLitter / ExpectedMaxLitterTotal)
                : 0f;
            int targetCount = Mathf.RoundToInt(normalised * MaxDisplayedBottles);
            int currentCount = _bottlePositions.Count;
            int delta = targetCount - currentCount;

            Debug.Log($"{LogChannel} Bottles: litterTotal={totalLitter}, normalised={normalised:F2}, target={targetCount}, current={currentCount}, delta={delta:+#;-#;0}.");

            if (delta == 0) return;

            if (delta > 0 && allBeachCells.Count > 0)
            {
                List<Vector3> newPositions = PixelArtDistributionSampler.Sample(
                    allBeachCells, delta, grid, BottleSettings, rng);
                for (int i = 0; i < newPositions.Count; i++)
                {
                    _bottlePositions.Add(newPositions[i]);
                    _bottleRotations.Add((float)((rng.NextDouble() * 2.0 - 1.0) * BottleMaxTiltDegrees));
                }
            }
            else if (delta < 0)
            {
                // LIFO removal: trim the tail. Existing bottles' positions are untouched.
                int toRemove = System.Math.Min(-delta, currentCount);
                _bottlePositions.RemoveRange(currentCount - toRemove, toRemove);
                _bottleRotations.RemoveRange(currentCount - toRemove, toRemove);
            }

            PixelArtEntityRenderer.Instance.RegisterGroup(new PixelArtEntityRenderer.GroupRequest
            {
                GroupId = BottleGroupId,
                Sprite = sprite,
                Positions = _bottlePositions,
                Rotations = _bottleRotations,
                // Render above every terrain layer (like seals) so grass / freshwater /
                // sea pixels that overlap into beach cell halos can't occlude bottles.
                RenderAboveAllTerrain = true,
                WorldSize = BottleWorldSize,
                Tint = Color.white
            });
        }

        // Per-region adjustment helper used by seal, oyster, and seagrass. For each region
        // of `targetType`, brings that region's position list to exactly `targetCountPerRegion`
        // entries by appending new samples or trimming the tail (never relocating existing
        // entries). Also prunes any region ids that no longer exist (defensive).
        //
        // Returns true if any region's position set changed, so the caller knows whether
        // it needs to call RegisterGroup. The aggregated `allPositions` / `allRotations`
        // lists are always produced (so a single RegisterGroup call covers every region).
        private bool AdjustPerRegionGroup(
            RegionComputeManager regions,
            GridManager grid,
            System.Random rng,
            RegionType targetType,
            int targetCountPerRegion,
            PixelArtDistributionSampler.Settings settings,
            Dictionary<int, List<float>> rotationsByRegion,
            Dictionary<int, List<Vector3>> positionsByRegion,
            out List<Vector3> allPositions,
            out List<float> allRotations)
        {
            bool changed = false;
            HashSet<int> liveRegions = new HashSet<int>();

            foreach (int regionId in regions.AllRegionIds)
            {
                if (regions.GetRegionType(regionId) != targetType) continue;
                liveRegions.Add(regionId);

                IReadOnlyList<(int col, int row)> regionCells = regions.GetRegionCells(regionId);
                if (regionCells.Count == 0) continue;

                if (!positionsByRegion.TryGetValue(regionId, out var positions))
                {
                    positions = new List<Vector3>();
                    positionsByRegion[regionId] = positions;
                }
                List<float> rotations = null;
                if (rotationsByRegion != null)
                {
                    if (!rotationsByRegion.TryGetValue(regionId, out rotations))
                    {
                        rotations = new List<float>();
                        rotationsByRegion[regionId] = rotations;
                    }
                }

                int delta = targetCountPerRegion - positions.Count;
                if (delta > 0)
                {
                    List<Vector3> newPos = PixelArtDistributionSampler.Sample(
                        regionCells, delta, grid, settings, rng);
                    positions.AddRange(newPos);
                    if (rotations != null)
                    {
                        for (int i = 0; i < newPos.Count; i++)
                        {
                            rotations.Add((float)(rng.NextDouble() * 360.0));
                        }
                    }
                    changed = true;
                }
                else if (delta < 0)
                {
                    int toRemove = System.Math.Min(-delta, positions.Count);
                    positions.RemoveRange(positions.Count - toRemove, toRemove);
                    if (rotations != null) rotations.RemoveRange(rotations.Count - toRemove, toRemove);
                    changed = true;
                }
            }

            // Drop any regions that vanished (defensive — region layout shouldn't change
            // mid-scenario, but stale entries would otherwise stay in the aggregated lists).
            List<int> stale = null;
            foreach (int rid in positionsByRegion.Keys)
            {
                if (!liveRegions.Contains(rid))
                {
                    if (stale == null) stale = new List<int>();
                    stale.Add(rid);
                }
            }
            if (stale != null)
            {
                for (int i = 0; i < stale.Count; i++)
                {
                    positionsByRegion.Remove(stale[i]);
                    rotationsByRegion?.Remove(stale[i]);
                }
                changed = true;
            }

            allPositions = new List<Vector3>();
            allRotations = rotationsByRegion != null ? new List<float>() : null;
            foreach (var kvp in positionsByRegion)
            {
                allPositions.AddRange(kvp.Value);
                if (allRotations != null && rotationsByRegion.TryGetValue(kvp.Key, out var rots))
                {
                    allRotations.AddRange(rots);
                }
            }
            return changed;
        }

        // Re-registers a group with an empty positions list — the renderer's ClearChildren
        // path runs on every RegisterGroup, so this wipes any sprites left from a previous
        // scenario without leaving them on screen until the next delta>0 register.
        private void ClearGroup(string groupId, Sprite sprite, float worldSize, bool renderAboveAllTerrain, string renderAboveTerrain)
        {
            PixelArtEntityRenderer.Instance.RegisterGroup(new PixelArtEntityRenderer.GroupRequest
            {
                GroupId = groupId,
                Sprite = sprite,
                Positions = new List<Vector3>(),
                RenderAboveAllTerrain = renderAboveAllTerrain,
                RenderAboveTerrain = renderAboveTerrain,
                WorldSize = worldSize,
                Tint = Color.white
            });
        }
    }
}
