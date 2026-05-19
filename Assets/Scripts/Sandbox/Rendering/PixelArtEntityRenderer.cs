using System.Collections.Generic;
using Glitchers.EcoKnow.Sandbox.Grid;
using Glitchers.EcoKnow.Sandbox.Terrain;
using UnityEngine;

namespace Glitchers.EcoKnow.Sandbox.Rendering
{
    // Spawns pixel-art SpriteRenderers in the world according to caller-supplied
    // placement data. Owns the GameObject hierarchy under GridManager.CellContainerTransform
    // and recycles its children on each Register call so replays/scenario-restarts don't
    // accumulate stale sprites.
    //
    // Layering: each group declares a "render above" terrain name. The renderer derives
    // sortingOrder + z from TerrainPriority so sprites slot above that terrain but still
    // sit beneath whatever terrain occupies the next-higher layer (e.g. oysters above
    // sand, but under seawater).
    //
    // This renderer is the "visual-only" backend used by PixelArtTestSpawner today. The
    // same API is intentionally usable by a future entity-driven mode that reads Entity
    // records flagged with a pixel-art sprite and recomputes positions on population change.
    public class PixelArtEntityRenderer : MonoBehaviour
    {
        private const string LogChannel = "[PixelArtEntityRenderer]";
        private const string RootName = "_PixelArtEntities";

        // DEBUG: when true, every registered group is forced to a very high sortingOrder
        // and z = 0 so sprites render on top of everything in the scene regardless of the
        // group's declared RenderAboveTerrain. Useful for verifying placement and import
        // before fixing real layering. Flip back to false once visuals are confirmed.
        private const bool DebugDrawOnTop = false;
        private const int DebugSortingOrder = 1000;

        private static PixelArtEntityRenderer _instance;
        public static PixelArtEntityRenderer Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<PixelArtEntityRenderer>();
                    if (_instance == null)
                    {
                        GameObject host = new GameObject(nameof(PixelArtEntityRenderer));
                        _instance = host.AddComponent<PixelArtEntityRenderer>();
                    }
                }
                return _instance;
            }
        }

        // One container Transform per registered group ID. Lets a later Register call for
        // the same ID wipe the previous sprites without touching other groups.
        private readonly Dictionary<string, Transform> _groupContainers = new Dictionary<string, Transform>();
        private Transform _root;

        public struct GroupRequest
        {
            // Unique key for this group — re-registering with the same ID clears prior
            // sprites first so callers don't have to deduplicate.
            public string GroupId;
            // Sprite to render. Must be imported with point filter for pixel-perfect art.
            public Sprite Sprite;
            // Continuous world-space placements (sub-cell precision). One sprite per entry.
            public IReadOnlyList<Vector3> Positions;
            // Per-sprite Z-axis rotation in degrees, parallel to Positions. Null = no
            // rotation. When non-null, length must match Positions.Count.
            public IReadOnlyList<float> Rotations;
            // Terrain whose visual tilemap this group draws ABOVE. The next-higher layer
            // still occludes — e.g. "sand" puts sprites above sand but below sea.
            // Ignored when RenderAboveAllTerrain is true.
            public string RenderAboveTerrain;
            // When true, the group renders above every terrain layer regardless of
            // RenderAboveTerrain. Use for entities that should never be occluded.
            public bool RenderAboveAllTerrain;
            // World-space size of the sprite's longest edge. Sprites stay pixel-perfect
            // (no filtering) and are uniformly scaled to hit this size.
            public float WorldSize;
            // Optional tint applied to the SpriteRenderer. Identity = white opaque.
            public Color Tint;
        }

        public void RegisterGroup(GroupRequest request)
        {
            if (string.IsNullOrEmpty(request.GroupId))
            {
                Debug.LogError($"{LogChannel} RegisterGroup called with empty GroupId — ignored.");
                return;
            }
            if (request.Sprite == null)
            {
                Debug.LogError($"{LogChannel} RegisterGroup '{request.GroupId}' missing sprite — ignored.");
                return;
            }

            Transform container = GetOrCreateContainer(request.GroupId);
            ClearChildren(container);

            int sortingOrder;
            float z;
            if (DebugDrawOnTop)
            {
                sortingOrder = DebugSortingOrder;
                z = 0f;
            }
            else if (request.RenderAboveAllTerrain)
            {
                sortingOrder = TerrainPriority.AboveAllTerrainSortingOrder;
                z = 0f;
            }
            else
            {
                sortingOrder = TerrainPriority.SortingOrderOf(request.RenderAboveTerrain);
                z = TerrainPriority.ZAbove(request.RenderAboveTerrain);
            }
            // Sprite's native world size at default scale = pixels / PPU. We rescale so
            // the larger of width/height matches request.WorldSize, preserving aspect.
            float spriteMax = Mathf.Max(request.Sprite.bounds.size.x, request.Sprite.bounds.size.y);
            float scale = spriteMax > 0f ? request.WorldSize / spriteMax : 1f;
            Color tint = request.Tint.a > 0f ? request.Tint : Color.white;

            int positionCount = request.Positions != null ? request.Positions.Count : 0;
            Debug.Log($"{LogChannel} RegisterGroup '{request.GroupId}': {positionCount} sprites, sortingOrder={sortingOrder}, z={z:F4}, scale={scale:F3}, sprite='{request.Sprite.name}'");

            int count = request.Positions != null ? request.Positions.Count : 0;
            for (int i = 0; i < count; i++)
            {
                Vector3 pos = request.Positions[i];
                pos.z = z;
                GameObject spriteGO = new GameObject($"{request.GroupId}_{i}");
                spriteGO.transform.SetParent(container, false);
                spriteGO.transform.localPosition = pos;
                spriteGO.transform.localScale = new Vector3(scale, scale, 1f);
                if (request.Rotations != null)
                {
                    spriteGO.transform.localRotation = Quaternion.Euler(0f, 0f, request.Rotations[i]);
                }

                SpriteRenderer sr = spriteGO.AddComponent<SpriteRenderer>();
                sr.sprite = request.Sprite;
                sr.sortingOrder = sortingOrder;
                sr.color = tint;
            }
        }

        public void ClearGroup(string groupId)
        {
            if (_groupContainers.TryGetValue(groupId, out Transform container) && container != null)
            {
                ClearChildren(container);
            }
        }

        public void ClearAll()
        {
            foreach (Transform container in _groupContainers.Values)
            {
                if (container != null) ClearChildren(container);
            }
        }

        private Transform GetOrCreateContainer(string groupId)
        {
            EnsureRoot();
            if (_groupContainers.TryGetValue(groupId, out Transform existing) && existing != null)
            {
                return existing;
            }
            GameObject go = new GameObject(groupId);
            go.transform.SetParent(_root, false);
            _groupContainers[groupId] = go.transform;
            return go.transform;
        }

        private void EnsureRoot()
        {
            if (_root != null) return;

            Transform parent = null;
            if (SandboxManager.Exists && SandboxManager.Instance != null && SandboxManager.Instance.GridManager != null)
            {
                parent = SandboxManager.Instance.GridManager.CellContainerTransform;
            }

            GameObject rootGO = new GameObject(RootName);
            rootGO.transform.SetParent(parent, false);
            _root = rootGO.transform;
        }

        private static void ClearChildren(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                Transform child = t.GetChild(i);
                if (Application.isPlaying)
                {
                    Destroy(child.gameObject);
                }
                else
                {
                    DestroyImmediate(child.gameObject);
                }
            }
        }
    }
}
