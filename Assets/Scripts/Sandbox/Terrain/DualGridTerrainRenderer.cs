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
        // Per-terrain Material asset (soil/grass/sand) plus an opt-in runtime override
        // for the sea/freshwater materials via WaterTintController. Resolved per visual
        // tilemap as soon as the tilemap is created.
        [SerializeField] private TerrainShaderConfig _shaderConfig;

        // 16-entry marching-squares lookup. Tuple order: (TopLeft, TopRight, BottomLeft, BottomRight).
        // Index 12 represents the all-empty case (no tile drawn).
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

        // Cached shader property ID for _OverlayTex. Resolved once per process; cheaper
        // than the string-keyed GetTexture overload and lets us pair with HasProperty
        // to skip materials whose shader (e.g. EcoKnow/Water) doesn't declare it.
        private static readonly int OverlayTexPropertyId = Shader.PropertyToID("_OverlayTex");

        // Within-layer z-step keeps adjacent terrains on the same sortingOrder from z-fighting.
        // Per-layer z-step is strictly larger than (max-within-layer-rank) * WithinLayerZStep so
        // layers never overlap in z. Both constants live on TerrainPriority so non-terrain
        // renderers (e.g. PixelArtEntityRenderer) can compute matching z values.
        private const float WithinLayerZStep = TerrainPriority.WithinLayerZStep;
        private const float LayerZStep = TerrainPriority.LayerZStep;

        private GridCoords _coords;
        private int _columns;
        private int _rows;
        private bool _hierarchyBuilt;
        private Func<int, int, bool> _isVoid;

        private Dictionary<string, TileBase[]> _tiles;
        private Dictionary<string, TileBase[]> _voidMaskedTiles;
        private Dictionary<string, TileBase> _markerTiles;
        private Dictionary<string, Tilemap> _dataTilemaps;
        private Dictionary<string, Tilemap> _visualTilemaps;

        // Static (frame-0 only) variant of the sea tileset. Swapped in by RefreshVisualTilemap
        // at sea cells where the freshwater data layer is also present (the estuary), because
        // freshwater renders on top of sea using only frame 0 and cannot cover the extra
        // alpha that sea's later wave-extension frames paint past the frame-0 silhouette.
        private TileBase[] _staticSeaTiles;

        public void Build(int columns, int rows, float cellScale, float cellGap, Func<int, int, int> getZoneId, Func<int, int, bool> isVoid)
        {
            if (getZoneId == null) throw new ArgumentNullException(nameof(getZoneId));
            if (isVoid == null) throw new ArgumentNullException(nameof(isVoid));

            _coords = new GridCoords(cellScale, cellGap);
            _columns = columns;
            _rows = rows;
            _isVoid = isVoid;

            EnsureTilesLoaded();
            EnsureHierarchy();
            StampDataTilemaps(getZoneId);
            RefreshAllVisualTilemaps();
        }

        private void EnsureTilesLoaded()
        {
            if (_tiles != null) return;
            _tiles = new Dictionary<string, TileBase[]>();
            _voidMaskedTiles = new Dictionary<string, TileBase[]>();
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

                TileBase[] tiles = SliceTextureIntoTiles(texture, terrain, animate: terrain == TilesetConstants.Sea);
                if (tiles == null) continue;

                _tiles[terrain] = tiles;
                _markerTiles[terrain] = tiles[FullTileIndex] ?? FirstNonNull(tiles);

                if (terrain == TilesetConstants.Sea)
                {
                    _staticSeaTiles = SliceTextureIntoTiles(texture, "sea_static", animate: false);
                }
            }

            // Boundary variants ride on a main terrain's data + visual tilemaps; load their sprites
            // here so RefreshVisualTilemap can swap to them per-tile, but skip marker/data/visual setup.
            // The variant texture is expected to mark its "void" region with opaque black pixels (the
            // mask). At load time those black pixels are stripped to alpha 0 so the variant renders
            // cleanly, and the mask is stashed for use by VoidMasks below.
            Dictionary<string, bool[]> variantMasks = new Dictionary<string, bool[]>();
            Dictionary<string, int> variantPixelWidth = new Dictionary<string, int>();
            foreach (string variantName in TilesetConstants.VoidVariants.Values)
            {
                if (_tiles.ContainsKey(variantName)) continue;
                if (!TilesetConstants.TilesetPaths.TryGetValue(variantName, out string variantPath))
                {
                    Debug.LogWarning($"[DualGridTerrainRenderer] No resource path registered for void variant '{variantName}'.");
                    continue;
                }
                Texture2D variantTexture = Resources.Load<Texture2D>(variantPath);
                if (variantTexture == null)
                {
                    Debug.LogWarning($"[DualGridTerrainRenderer] Missing tileset texture: Resources/{variantPath}");
                    continue;
                }

                Color32[] srcPixels;
                try { srcPixels = variantTexture.GetPixels32(); }
                catch (UnityException e)
                {
                    Debug.LogWarning($"[DualGridTerrainRenderer] Cannot read '{variantName}' pixels (isReadable must be true): {e.Message}");
                    continue;
                }
                bool[] mask = new bool[srcPixels.Length];
                Color32[] cleaned = new Color32[srcPixels.Length];
                for (int i = 0; i < srcPixels.Length; i++)
                {
                    Color32 p = srcPixels[i];
                    bool isBlack = p.a >= 250 && p.r <= 16 && p.g <= 16 && p.b <= 16;
                    mask[i] = isBlack;
                    cleaned[i] = isBlack ? new Color32(0, 0, 0, 0) : p;
                }
                Texture2D cleanTex = new Texture2D(variantTexture.width, variantTexture.height, TextureFormat.RGBA32, mipChain: false)
                {
                    name = $"{variantName}_cleaned",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                cleanTex.SetPixels32(cleaned);
                cleanTex.Apply(updateMipmaps: false, makeNoLongerReadable: false);

                TileBase[] variantTiles = SliceTextureIntoTiles(cleanTex, variantName, animate: false);
                if (variantTiles == null) continue;
                _tiles[variantName] = variantTiles;
                variantMasks[variantName] = mask;
                variantPixelWidth[variantName] = variantTexture.width;
            }

            // VoidMasks: bake the variant's black-pixel mask into the target terrain's sprites by
            // setting alpha=0 wherever the mask is true. Used at boundary halos for terrains that
            // are stamped on water cells (e.g. sand) so their soft edges don't leak past the SeaVoid
            // seam. The masked sprites are stored separately and selected by RefreshVisualTilemap
            // when AnyCornerIsVoid is true.
            foreach (var pair in TilesetConstants.VoidMasks)
            {
                string targetTerrain = pair.Key;
                string maskSource = pair.Value;
                if (!variantMasks.TryGetValue(maskSource, out bool[] mask))
                {
                    Debug.LogWarning($"[DualGridTerrainRenderer] Void mask source '{maskSource}' has no mask data; cannot mask '{targetTerrain}'.");
                    continue;
                }
                if (!TilesetConstants.TilesetPaths.TryGetValue(targetTerrain, out string targetPath))
                {
                    Debug.LogWarning($"[DualGridTerrainRenderer] No resource path registered for void mask target '{targetTerrain}'.");
                    continue;
                }
                Texture2D targetTexture = Resources.Load<Texture2D>(targetPath);
                if (targetTexture == null)
                {
                    Debug.LogWarning($"[DualGridTerrainRenderer] Missing tileset texture: Resources/{targetPath}");
                    continue;
                }

                // For pixel-by-pixel masking the variant must be square (non-animated) and the same
                // dimensions as the target's leftmost-frame block (which the slicer reads). Target
                // block = (4 * tileSize) × (4 * tileSize) = targetTexture.height × targetTexture.height.
                int variantWidth = variantPixelWidth[maskSource];
                int variantHeight = mask.Length / variantWidth;
                int targetBlockSize = targetTexture.height;
                if (variantWidth != targetBlockSize || variantHeight != targetBlockSize)
                {
                    Debug.LogWarning($"[DualGridTerrainRenderer] Void mask geometry mismatch: '{maskSource}' is {variantWidth}x{variantHeight} but '{targetTerrain}' leftmost block is {targetBlockSize}x{targetBlockSize}. Skipping.");
                    continue;
                }

                Color32[] targetPixels;
                try { targetPixels = targetTexture.GetPixels32(); }
                catch (UnityException e)
                {
                    Debug.LogWarning($"[DualGridTerrainRenderer] Cannot read '{targetTerrain}' pixels: {e.Message}");
                    continue;
                }

                // Copy the target's leftmost block, applying alpha=0 wherever the mask is set.
                Color32[] maskedBlock = new Color32[targetBlockSize * targetBlockSize];
                int srcWidth = targetTexture.width;
                for (int y = 0; y < targetBlockSize; y++)
                {
                    int srcRowStart = y * srcWidth;
                    int dstRowStart = y * targetBlockSize;
                    for (int x = 0; x < targetBlockSize; x++)
                    {
                        Color32 px = targetPixels[srcRowStart + x];
                        if (mask[dstRowStart + x])
                        {
                            px.a = 0;
                        }
                        maskedBlock[dstRowStart + x] = px;
                    }
                }

                Texture2D maskedTex = new Texture2D(targetBlockSize, targetBlockSize, TextureFormat.RGBA32, mipChain: false)
                {
                    name = $"{targetTerrain}_voidMasked",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                maskedTex.SetPixels32(maskedBlock);
                maskedTex.Apply(updateMipmaps: false, makeNoLongerReadable: false);

                TileBase[] maskedTiles = SliceTextureIntoTiles(maskedTex, $"{targetTerrain}_voidMasked", animate: false);
                if (maskedTiles == null) continue;
                _voidMaskedTiles[targetTerrain] = maskedTiles;
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

        // Animation frames-per-second used for animated tilesets (currently sea only).
        private const float AnimatedTileSpeed = 1f;

        // Slices the texture into a 4×4 grid of 16 tiles. Index 0 = top-left, index 15 =
        // bottom-right (row-major). Pixel-perfect settings come from the .meta
        // (filterMode=Point, textureCompression=None on default platform); this method
        // preserves them by referencing the texture as-is and using PPU = tileSize so
        // 1 world unit = 1 tile = exactly tileSize source pixels.
        //
        // When `animate` is true and the texture is wider than one 4×4 block, emits
        // AnimatedTile per stamp with all frames. Otherwise — including void/masked
        // variants of sea, which need pixel-exact alpha masking on a single frame —
        // emits a static Tile from the leftmost frame.
        private static TileBase[] SliceTextureIntoTiles(Texture2D texture, string terrain, bool animate)
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
            bool shouldAnimate = animate && frameCount > 1;

            TileBase[] tiles = new TileBase[TileCount];
            Vector2 pivot = new Vector2(0.5f, 0.5f);
            float ppu = tileSize;

            for (int i = 0; i < TileCount; i++)
            {
                int col = i % TileSheetColumns;
                int row = i / TileSheetColumns;
                int spriteY = (TileSheetRows - 1 - row) * tileSize;  // sprite Y is bottom-up; row 0 = top

                if (shouldAnimate)
                {
                    Sprite[] frames = new Sprite[frameCount];
                    for (int f = 0; f < frameCount; f++)
                    {
                        int spriteX = (col + f * TileSheetColumns) * tileSize;
                        Sprite frameSprite = Sprite.Create(
                            texture,
                            new Rect(spriteX, spriteY, tileSize, tileSize),
                            pivot,
                            ppu
                        );
                        frameSprite.name = $"{terrain}_{i}_f{f}";
                        frames[f] = frameSprite;
                    }

                    AnimatedTile animTile = ScriptableObject.CreateInstance<AnimatedTile>();
                    animTile.m_AnimatedSprites = frames;
                    animTile.m_MinSpeed = AnimatedTileSpeed;
                    animTile.m_MaxSpeed = AnimatedTileSpeed;
                    animTile.m_TileColliderType = Tile.ColliderType.None;
                    animTile.name = $"AnimTile_{terrain}_{i}";
                    tiles[i] = animTile;
                }
                else
                {
                    int spriteX = col * tileSize;                    // leftmost frame only
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
                // Cell prefab in scene_Sandbox has SpriteRenderers at sortingOrder 1 and 2.
                // Offset tilemap rendering well above to ensure dual-grid visuals always
                // sit on top of any cell-level overlays (zone colour markers, highlights).
                _visualTilemaps[terrain] = CreateChildTilemap(
                    childName: $"Visual_{terrain}",
                    localPos: GridCoords.VisualTilemapOffset(z),
                    sortingOrder: TerrainPriority.TerrainSortingOrderBase + layer,
                    rendererEnabled: true
                );
                ApplyMaterialFor(terrain);
            }

            _hierarchyBuilt = true;
        }

        // Picks the right Material for a terrain. WaterTintController (when present in the
        // scene) wins for sea/freshwater so per-instance clones drive the visual; otherwise
        // the asset reference from TerrainShaderConfig is used directly. Returns null when
        // neither source has a binding — Tilemap then renders with Unity's default sprite material.
        private Material ResolveMaterial(string terrain)
        {
            WaterTintController tint = WaterTintController.Instance;
            if (tint != null)
            {
                Material runtime = tint.GetMaterialFor(terrain);
                if (runtime != null) return runtime;
            }
            return _shaderConfig != null ? _shaderConfig.GetMaterial(terrain) : null;
        }

        private void ApplyMaterialFor(string terrain)
        {
            if (!_visualTilemaps.TryGetValue(terrain, out Tilemap tm) || tm == null) return;
            Material mat = ResolveMaterial(terrain);
            if (mat == null) return;
            TilemapRenderer r = tm.GetComponent<TilemapRenderer>();
            if (r != null) r.sharedMaterial = mat;

            // Force pixel-perfect sampling on the overlay texture. The import meta
            // ships with these settings, but enforcing them at runtime guards against
            // drift if someone re-imports with different defaults. Water materials
            // (EcoKnow/Water shader) don't have _OverlayTex — gate the lookup so we
            // don't log a warning for them every frame the renderer rebuilds.
            if (mat.HasProperty(OverlayTexPropertyId))
            {
                Texture overlay = mat.GetTexture(OverlayTexPropertyId);
                if (overlay != null)
                {
                    overlay.wrapMode = TextureWrapMode.Repeat;
                    overlay.filterMode = FilterMode.Point;
                }
            }
        }

        // Re-resolves and re-assigns the Material on every visual tilemap. Safe to call
        // after WaterTintController.Awake (so the runtime water instances are picked up
        // even if the renderer's hierarchy was built first).
        public void RefreshMaterials()
        {
            if (_visualTilemaps == null) return;
            foreach (var pair in _visualTilemaps)
            {
                ApplyMaterialFor(pair.Key);
            }
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
                    if (_isVoid != null && _isVoid(col, row)) continue;
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
        // Visual range covers vx ∈ [0, C] and vy ∈ [-(R-1), 1]. Out-of-data corners read as absent
        // so the marching-squares lookup yields proper edge sprites at the world boundary. At
        // boundary halos (any corner void) the tile sprite is swapped: VoidVariants picks a sprite
        // from a separate variant tileset (e.g. sea→sea_void), VoidMasks picks an alpha-masked
        // variant of the terrain's own sprite (e.g. sand clipped to the SeaVoid silhouette).
        //
        // Sea has an extra swap to a static variant whenever the freshwater data layer is present
        // at any of the 4 corners (i.e. the estuary). See _staticSeaTiles for the reason.
        private void RefreshVisualTilemap(string terrain, Tilemap visualTilemap)
        {
            visualTilemap.ClearAllTiles();
            if (!_dataTilemaps.TryGetValue(terrain, out Tilemap dataTilemap)) return;
            if (!_tiles.TryGetValue(terrain, out TileBase[] tiles)) return;

            TileBase[] boundaryTiles = null;
            if (TilesetConstants.VoidVariants.TryGetValue(terrain, out string variantName))
            {
                _tiles.TryGetValue(variantName, out boundaryTiles);
            }
            else if (TilesetConstants.VoidMasks.ContainsKey(terrain))
            {
                _voidMaskedTiles.TryGetValue(terrain, out boundaryTiles);
            }

            TileBase[] estuaryStaticTiles = null;
            Tilemap freshwaterData = null;
            if (terrain == TilesetConstants.Sea && _staticSeaTiles != null)
            {
                _dataTilemaps.TryGetValue(TilesetConstants.Freshwater, out freshwaterData);
                if (freshwaterData != null) estuaryStaticTiles = _staticSeaTiles;
            }

            for (int vy = -(_rows - 1); vy <= 1; vy++)
            {
                for (int vx = 0; vx <= _columns; vx++)
                {
                    int idx = SampleAndLookup(dataTilemap, vx, vy, _columns, _rows);
                    if (idx == EmptyTileIndex) continue;

                    TileBase[] activeTiles;
                    if (boundaryTiles != null && AnyCornerIsVoid(vx, vy))
                    {
                        activeTiles = boundaryTiles;
                    }
                    else if (estuaryStaticTiles != null && AnyCornerHasMarker(freshwaterData, vx, vy))
                    {
                        activeTiles = estuaryStaticTiles;
                    }
                    else
                    {
                        activeTiles = tiles;
                    }
                    visualTilemap.SetTile(new Vector3Int(vx, vy, 0), activeTiles[idx]);
                }
            }
        }

        private bool AnyCornerHasMarker(Tilemap data, int vx, int vy)
        {
            return Presence(data, vx - 1, vy,     _columns, _rows)
                || Presence(data, vx,     vy,     _columns, _rows)
                || Presence(data, vx - 1, vy - 1, _columns, _rows)
                || Presence(data, vx,     vy - 1, _columns, _rows);
        }

        private static int SampleAndLookup(Tilemap data, int vx, int vy, int columns, int rows)
        {
            bool tl = Presence(data, vx - 1, vy,     columns, rows);
            bool tr = Presence(data, vx,     vy,     columns, rows);
            bool bl = Presence(data, vx - 1, vy - 1, columns, rows);
            bool br = Presence(data, vx,     vy - 1, columns, rows);
            return NeighbourToTileIndex.TryGetValue((tl, tr, bl, br), out int idx) ? idx : EmptyTileIndex;
        }

        private static bool Presence(Tilemap data, int x, int y, int columns, int rows)
        {
            if (x < 0 || x >= columns) return false;
            if (y > 0 || y < -(rows - 1)) return false;
            return data.GetTile(new Vector3Int(x, y, 0)) != null;
        }

        // True if any of the 4 data corners around visual (vx, vy) is a void cell.
        // Data coord (x, y) maps to grid cell (col=x, row=-y) — see CellToData in GridCoords.
        private bool AnyCornerIsVoid(int vx, int vy)
        {
            return IsVoidAtData(vx - 1, vy)
                || IsVoidAtData(vx,     vy)
                || IsVoidAtData(vx - 1, vy - 1)
                || IsVoidAtData(vx,     vy - 1);
        }

        private bool IsVoidAtData(int dataX, int dataY)
        {
            if (_isVoid == null) return false;
            return _isVoid(dataX, -dataY);
        }
    }
}
