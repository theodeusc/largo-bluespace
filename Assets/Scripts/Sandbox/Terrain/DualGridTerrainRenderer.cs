using System;
using System.Collections.Generic;
using Glitchers.EcoKnow.Sandbox.Grid;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Glitchers.EcoKnow.Sandbox.Terrain
{
    /// <summary>
    /// Renders fixed (non-dynamic) terrain via dual-grid auto-tiling. One (data, visual) Tilemap pair
    /// is created per terrain type listed in <see cref="TerrainPriority.Layers"/>. Per-cell terrain
    /// composition comes from <see cref="TerrainPriority.ZoneTerrainStacks"/>; layer order and
    /// within-layer ordering come from <see cref="TerrainPriority.Layers"/>. The auto-tile decision
    /// is the standard 16-entry 4-corner marching-squares lookup.
    /// </summary>
    /// <remarks>
    /// The MonoBehaviour expects to live on a GameObject whose parent transform is unscaled and
    /// at the origin (e.g. the GridManager.cellContainer). Build() then sets localScale to
    /// (cellStep, cellStep, 1) so that one tilemap cell maps to one game-grid cell.
    /// </remarks>
    public sealed class DualGridTerrainRenderer : MonoBehaviour
    {
        // 16-entry marching-squares lookup. Tuple order: (TopLeft, TopRight, BottomLeft, BottomRight).
        // Index 12 represents the all-empty case (no tile drawn). Verbatim port of the table in
        // creator_old/Assets/Scripts/Sandbox/Terrain/DualGridTerrainRenderer.cs.
        private static readonly Dictionary<(bool, bool, bool, bool), int> NeighbourToTileIndex =
            new Dictionary<(bool, bool, bool, bool), int>
            {
                { (true,  true,  true,  true ),  6 },
                { (false, false, false, true ), 13 },
                { (false, false, true,  false),  0 },
                { (false, true,  false, false),  8 },
                { (true,  false, false, false), 15 },
                { (false, true,  false, true ),  1 },
                { (true,  false, true,  false), 11 },
                { (false, false, true,  true ),  3 },
                { (true,  true,  false, false),  9 },
                { (false, true,  true,  true ),  5 },
                { (true,  false, true,  true ),  2 },
                { (true,  true,  false, true ), 10 },
                { (true,  true,  true,  false),  7 },
                { (false, true,  true,  false), 14 },
                { (true,  false, false, true ),  4 },
                { (false, false, false, false), 12 }
            };

        private const int EmptyTileIndex = 12;
        private const int FullTileIndex = 6;

        // Within-layer z-step keeps adjacent terrains on the same sortingOrder from z-fighting.
        // Matches the sand sub-layer offset used in creator_old.
        private const float WithinLayerZStep = 0.005f;

        // Per-layer z-step. Strictly larger than (max-within-layer-rank) * WithinLayerZStep so
        // layers never overlap in z. Matches the per-priority offset used in creator_old.
        private const float LayerZStep = 0.01f;

        private GridCoords _coords;
        private int _columns;
        private int _rows;
        private bool _hierarchyBuilt;

        private Dictionary<string, TileBase[]> _tiles;
        private Dictionary<string, TileBase> _markerTiles;
        private Dictionary<string, Tilemap> _dataTilemaps;
        private Dictionary<string, Tilemap> _visualTilemaps;

        public void Build(int columns, int rows, float cellScale, float cellGap, Func<int, int, int> getZoneId)
        {
            if (getZoneId == null) throw new ArgumentNullException(nameof(getZoneId));

            _coords = new GridCoords(cellScale, cellGap);
            _columns = columns;
            _rows = rows;

            EnsureTilesLoaded();
            EnsureHierarchy();
            StampDataTilemaps(getZoneId);
            RefreshAllVisualTilemaps();
        }

        private void EnsureTilesLoaded()
        {
            if (_tiles != null) return;
            _tiles = new Dictionary<string, TileBase[]>();
            _markerTiles = new Dictionary<string, TileBase>();

            foreach (string terrain in TerrainPriority.AllTerrainNames())
            {
                if (!TilesetConstants.TilesetPaths.TryGetValue(terrain, out string path))
                {
                    Debug.LogWarning($"[DualGridTerrainRenderer] No resource path registered for terrain '{terrain}'.");
                    continue;
                }

                Texture2D texture = Resources.Load<Texture2D>(path);
                if (texture == null)
                {
                    Debug.LogWarning($"[DualGridTerrainRenderer] Missing tileset texture: Resources/{path}");
                    continue;
                }

                TileBase[] tiles = SliceTextureIntoTiles(texture, terrain);
                if (tiles == null) continue;

                _tiles[terrain] = tiles;
                _markerTiles[terrain] = tiles[FullTileIndex] ?? FirstNonNull(tiles);
            }
        }

        private static TileBase FirstNonNull(TileBase[] tiles)
        {
            for (int i = 0; i < tiles.Length; i++)
            {
                if (tiles[i] != null) return tiles[i];
            }
            return null;
        }

        private const int TileSheetColumns = 4;
        private const int TileSheetRows = 4;
        private const int TileCount = TileSheetColumns * TileSheetRows;

        // Slices the leftmost (TileSheetColumns × tileSize) region of the texture into a 4×4 grid
        // of 16 tiles. Index 0 = top-left, index 15 = bottom-right (row-major). Pixel-perfect
        // settings come from the .meta (filterMode=Point, textureCompression=None on default
        // platform); this method preserves them by referencing the texture as-is and using
        // PPU = tileSize so 1 world unit = 1 tile = exactly tileSize source pixels.
        //
        // Identical slicing logic to creator_old/Assets/Scripts/Sandbox/Terrain/DualGridTerrainRenderer.cs
        // (CreateTilesFromTexture, lines 454-484). For animated tilesets like sea (768×128 with
        // 6 horizontal frames), the old project routes through CreateAnimatedTilesFromTexture;
        // this minimal port deliberately takes only the leftmost frame and renders it static.
        private static TileBase[] SliceTextureIntoTiles(Texture2D texture, string terrain)
        {
            if (texture.height % TileSheetRows != 0)
            {
                Debug.LogError($"[DualGridTerrainRenderer] '{terrain}' tileset is {texture.width}x{texture.height} — height not divisible by {TileSheetRows}.");
                return null;
            }

            int tileSize = texture.height / TileSheetRows;
            if (tileSize <= 0)
            {
                Debug.LogError($"[DualGridTerrainRenderer] Invalid tile size for '{terrain}' (texture height {texture.height}).");
                return null;
            }

            if (texture.width % tileSize != 0)
            {
                Debug.LogError($"[DualGridTerrainRenderer] '{terrain}' tileset is {texture.width}x{texture.height} — width not divisible by tile size {tileSize}.");
                return null;
            }

            int frameCount = (texture.width / tileSize) / TileSheetColumns;
            if (frameCount > 1)
            {
                Debug.Log($"[DualGridTerrainRenderer] '{terrain}' tileset has {frameCount} animation frames; rendering frame 0 only (animation not yet supported in this minimal port).");
            }

            Tile[] tiles = new Tile[TileCount];
            Vector2 pivot = new Vector2(0.5f, 0.5f);
            float ppu = tileSize;

            for (int i = 0; i < TileCount; i++)
            {
                int col = i % TileSheetColumns;
                int row = i / TileSheetColumns;

                int spriteX = col * tileSize;                        // leftmost frame only
                int spriteY = (TileSheetRows - 1 - row) * tileSize;  // sprite Y is bottom-up; row 0 = top

                Sprite sprite = Sprite.Create(
                    texture,
                    new Rect(spriteX, spriteY, tileSize, tileSize),
                    pivot,
                    ppu
                );
                sprite.name = $"{terrain}_{i}";

                Tile tile = ScriptableObject.CreateInstance<Tile>();
                tile.sprite = sprite;
                tile.color = Color.white;
                tile.colliderType = Tile.ColliderType.None;
                tile.name = $"Tile_{terrain}_{i}";
                tiles[i] = tile;
            }
            return tiles;
        }

        private void EnsureHierarchy()
        {
            ApplyRootTransform();

            if (_hierarchyBuilt) return;

            if (!TryGetComponent(out UnityEngine.Grid grid))
            {
                grid = gameObject.AddComponent<UnityEngine.Grid>();
            }
            grid.cellSize = new Vector3(1f, 1f, 0f);

            _dataTilemaps = new Dictionary<string, Tilemap>();
            _visualTilemaps = new Dictionary<string, Tilemap>();

            foreach (string terrain in TerrainPriority.AllTerrainNames())
            {
                if (!_tiles.ContainsKey(terrain)) continue;

                int layer = TerrainPriority.LayerOf(terrain);
                if (layer < 0) continue;
                int withinRank = TerrainPriority.WithinLayerRank(terrain);
                int layerLength = TerrainPriority.Layers[layer].Length;

                _dataTilemaps[terrain] = CreateChildTilemap(
                    childName: $"Data_{terrain}",
                    localPos: new Vector3(0f, 0f, 1f),
                    sortingOrder: 0,
                    rendererEnabled: false
                );

                // Lower z = closer to camera in 2D. Each layer is shifted negatively by
                // LayerZStep, with within-layer rank 0 receiving the most-negative z so it
                // renders on top of higher-rank entries within the same layer.
                float z = -layer * LayerZStep - (layerLength - 1 - withinRank) * WithinLayerZStep;
                _visualTilemaps[terrain] = CreateChildTilemap(
                    childName: $"Visual_{terrain}",
                    localPos: GridCoords.VisualTilemapOffset(z),
                    sortingOrder: layer,
                    rendererEnabled: true
                );
            }

            _hierarchyBuilt = true;
        }

        // Aligns the renderer so data tile (col, -row) is concentric with the cell at (col, row).
        // Cell world center = (col * cellStep, -row * cellStep). Data tile (col, -row) intrinsic
        // center = (col + 0.5, -row + 0.5) in tilemap-local; with localScale = cellStep that's
        // ((col+0.5)*cellStep, (-row+0.5)*cellStep). Subtracting half a cellStep on both axes
        // brings them into alignment.
        private void ApplyRootTransform()
        {
            float step = _coords.CellStep;
            transform.localScale = new Vector3(step, step, 1f);
            transform.localPosition = step * GridCoords.DualGridOffset;
        }

        private Tilemap CreateChildTilemap(string childName, Vector3 localPos, int sortingOrder, bool rendererEnabled)
        {
            GameObject go = new GameObject(childName);
            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.localPosition = localPos;
            Tilemap tilemap = go.AddComponent<Tilemap>();
            TilemapRenderer tilemapRenderer = go.AddComponent<TilemapRenderer>();
            tilemapRenderer.sortingOrder = sortingOrder;
            tilemapRenderer.enabled = rendererEnabled;
            return tilemap;
        }

        private void StampDataTilemaps(Func<int, int, int> getZoneId)
        {
            foreach (var pair in _dataTilemaps)
            {
                pair.Value.ClearAllTiles();
            }

            for (int row = 0; row < _rows; row++)
            {
                for (int col = 0; col < _columns; col++)
                {
                    int zoneId = getZoneId(col, row);
                    if (!TerrainPriority.ZoneTerrainStacks.TryGetValue(zoneId, out string[] terrains)) continue;

                    Vector3Int dataPos = _coords.CellToData(col, row);
                    for (int t = 0; t < terrains.Length; t++)
                    {
                        string terrain = terrains[t];
                        if (!_dataTilemaps.TryGetValue(terrain, out Tilemap dataMap)) continue;
                        if (!_markerTiles.TryGetValue(terrain, out TileBase marker)) continue;
                        dataMap.SetTile(dataPos, marker);
                    }
                }
            }
        }

        private void RefreshAllVisualTilemaps()
        {
            foreach (var pair in _visualTilemaps)
            {
                RefreshVisualTilemap(pair.Key, pair.Value);
            }
        }

        // A C×R data grid needs (C+1)×(R+1) visual tiles due to the dual-grid half-cell offset.
        // With visual.localPosition = (-0.5, -0.5, z) and data stamped at (col, -row, 0), the
        // 4 data corners surrounding visual (vx, vy)'s center are at:
        //   TL = (vx-1, vy)    TR = (vx,   vy)
        //   BL = (vx-1, vy-1)  BR = (vx,   vy-1)
        // Visual range covers vx ∈ [0, C] and vy ∈ [-(R-1), 1]. Out-of-data lookups are clamped to
        // the grid edge so terrain reads as continuous at borders rather than fading out.
        private void RefreshVisualTilemap(string terrain, Tilemap visualTilemap)
        {
            visualTilemap.ClearAllTiles();
            if (!_dataTilemaps.TryGetValue(terrain, out Tilemap dataTilemap)) return;
            if (!_tiles.TryGetValue(terrain, out TileBase[] tiles)) return;

            for (int vy = -(_rows - 1); vy <= 1; vy++)
            {
                for (int vx = 0; vx <= _columns; vx++)
                {
                    int idx = SampleAndLookup(dataTilemap, vx, vy, _columns, _rows);
                    if (idx == EmptyTileIndex) continue;
                    visualTilemap.SetTile(new Vector3Int(vx, vy, 0), tiles[idx]);
                }
            }
        }

        private static int SampleAndLookup(Tilemap data, int vx, int vy, int columns, int rows)
        {
            bool tl = ClampedPresence(data, vx - 1, vy,     columns, rows);
            bool tr = ClampedPresence(data, vx,     vy,     columns, rows);
            bool bl = ClampedPresence(data, vx - 1, vy - 1, columns, rows);
            bool br = ClampedPresence(data, vx,     vy - 1, columns, rows);
            return NeighbourToTileIndex.TryGetValue((tl, tr, bl, br), out int idx) ? idx : EmptyTileIndex;
        }

        // Clamps a sample position to the data grid (X ∈ [0, C-1], Y ∈ [-(R-1), 0]) so visual
        // tiles at the grid edge see filled corners on all sides — matches creator_old's
        // hard-edge behaviour and prevents auto-tiles from fading out at the world boundary.
        private static bool ClampedPresence(Tilemap data, int x, int y, int columns, int rows)
        {
            int cx = Mathf.Clamp(x, 0, columns - 1);
            int cy = Mathf.Clamp(y, -(rows - 1), 0);
            return data.GetTile(new Vector3Int(cx, cy, 0)) != null;
        }
    }
}
