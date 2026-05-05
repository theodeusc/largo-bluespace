using System.Collections.Generic;
using Glitchers.EcoKnow.Sandbox.Grid;
using UnityEngine;

namespace Glitchers.EcoKnow.Sandbox.Terrain
{
    /// <summary>
    /// Shore-distance and water-classification map. Built once per scenario load.
    /// Drives shader-side transparency under freshwater (revealing the riverbed)
    /// and bathymetry-modulated seawater colour.
    ///
    /// Fixed-zones implementation: water classification reads
    /// <see cref="TerrainPriority.ZoneTerrainStacks"/> + <see cref="TilesetConstants.ToArchetype"/>
    /// (no connected-component sea detection needed — the zone definition is the SSOT).
    ///
    /// Extension hook: when dynamic zones return, replace
    /// <see cref="ClassifyCell"/> with a zone-elevation provider; the texture
    /// generation, shore-distance BFS, and shader contract stay unchanged.
    /// </summary>
    public sealed class ElevationMap
    {
        public const int TextureMultiplier = 8;       // Texels per grid cell for smooth gradients
        private const float ShoreFalloffCells = 2.5f;       // Width of shore alpha-blend band
        private const float FreshFalloffCells = 2.0f;       // Width of the freshwater-visibility fade away from fresh-only sources
        private const float DiffusionFalloffCells = 2.5f;   // Width of the sea-side diffusion plume away from overlap sources
        private const float DiffusionPerturbCells = 0.5f;   // Perlin amplitude (cells) for organic diffusion boundaries — mirrors shore-G
        private const string LogChannel = "[ElevationMap]";

        private bool[,] _isSea;
        private bool[,] _isFreshwater;
        private float[,] _altitudes;
        private float[,] _freshOnlyDistance;
        private float[,] _overlapDistance;
        private int _columns;
        private int _rows;

        private float _seedOffsetX;
        private float _seedOffsetY;

        private Texture2D _altitudeTexture;
        public Texture2D AltitudeTexture => _altitudeTexture;

        private WorldHeightSampler _heightSampler;
        public WorldHeightSampler HeightSampler => _heightSampler;

        public int Columns => _columns;
        public int Rows => _rows;

        public void Generate(GridManager gm, int seed)
        {
            if (gm == null) return;

            _seedOffsetX = (seed % 1000) * 0.7f;
            _seedOffsetY = (seed % 1000) * 1.3f;
            _heightSampler = new WorldHeightSampler(seed);

            Vector2 gridSize = gm.GridSize;
            _columns = (int)gridSize.x;
            _rows = (int)gridSize.y;

            _isSea = new bool[_columns, _rows];
            _isFreshwater = new bool[_columns, _rows];
            _altitudes = new float[_columns, _rows];

            ClassifyAllCells(gm);
            ComputeAltitudes();
            _freshOnlyDistance = ComputeFreshOnlyDistance();
            _overlapDistance = ComputeOverlapDistance();
            GenerateTexture();

            int seaCount = 0, freshCount = 0;
            for (int r = 0; r < _rows; r++)
                for (int c = 0; c < _columns; c++)
                {
                    if (_isSea[c, r]) seaCount++;
                    if (_isFreshwater[c, r]) freshCount++;
                }

            Debug.Log($"{LogChannel} Generated {_columns}x{_rows} altitude map " +
                      $"({seaCount} sea, {freshCount} freshwater).");
        }

        public bool IsSea(int col, int row) =>
            InBounds(col, row) && _isSea != null && _isSea[col, row];

        public bool IsFreshwater(int col, int row) =>
            InBounds(col, row) && _isFreshwater != null && _isFreshwater[col, row];

        public bool IsWater(int col, int row) => IsSea(col, row) || IsFreshwater(col, row);

        public float GetAltitude(int col, int row) =>
            InBounds(col, row) && _altitudes != null ? _altitudes[col, row] : 0f;

        public void Dispose()
        {
            if (_altitudeTexture != null)
            {
                Object.Destroy(_altitudeTexture);
                _altitudeTexture = null;
            }
            _isSea = null;
            _isFreshwater = null;
            _altitudes = null;
            _freshOnlyDistance = null;
            _overlapDistance = null;
        }

        // -- internals --

        private bool InBounds(int col, int row) =>
            col >= 0 && row >= 0 && col < _columns && row < _rows;

        private void ClassifyAllCells(GridManager gm)
        {
            for (int row = 0; row < _rows; row++)
            {
                for (int col = 0; col < _columns; col++)
                {
                    if (gm.IsVoid(col, row))
                    {
                        continue;
                    }

                    int zoneId = gm.GetZoneType(col, row);
                    ClassifyCell(zoneId, out bool hasSea, out bool hasFresh);
                    _isSea[col, row] = hasSea;
                    _isFreshwater[col, row] = hasFresh;
                }
            }
        }

        // Reads the SSOT zone-stack mapping and reports which Liquid terrains the zone contains.
        // TODO(dynamic-zones): replace with zone-metadata lookup when dynamic zones return.
        private static void ClassifyCell(int zoneId, out bool hasSea, out bool hasFreshwater)
        {
            hasSea = false;
            hasFreshwater = false;
            if (!TerrainPriority.ZoneTerrainStacks.TryGetValue(zoneId, out string[] terrains))
            {
                return;
            }
            for (int i = 0; i < terrains.Length; i++)
            {
                string t = terrains[i];
                if (t == TilesetConstants.Sea) hasSea = true;
                else if (t == TilesetConstants.Freshwater) hasFreshwater = true;
            }
        }

        private void ComputeAltitudes()
        {
            for (int row = 0; row < _rows; row++)
            {
                for (int col = 0; col < _columns; col++)
                {
                    if (_isSea[col, row])
                    {
                        float depth = _heightSampler.SampleDepth(col, row);
                        _altitudes[col, row] = Mathf.Clamp(-depth, -1f, 0f);
                    }
                    else if (_isFreshwater[col, row])
                    {
                        // Shallow constant — freshwater is treated as flat for now.
                        _altitudes[col, row] = -0.1f;
                    }
                    // Land cells stay at 0 (no cliff/layer logic in fixed-zones mode).
                }
            }
        }

        private void GenerateTexture()
        {
            int texW = _columns * TextureMultiplier;
            int texH = _rows * TextureMultiplier;

            _altitudeTexture = new Texture2D(texW, texH, TextureFormat.RGBA32, false);
            _altitudeTexture.filterMode = FilterMode.Bilinear;
            _altitudeTexture.wrapMode = TextureWrapMode.Clamp;
            _altitudeTexture.name = "EcoKnow.AltitudeMap";

            float[,] landDist = ComputeLandDistanceField();
            float maxDist = ComputeMaxWaterDistance(landDist);

            Color[] pixels = new Color[texW * texH];
            for (int ty = 0; ty < texH; ty++)
            {
                for (int tx = 0; tx < texW; tx++)
                {
                    // Map texel to grid-space coordinate (center of texel).
                    float gc = (tx + 0.5f) / TextureMultiplier - 0.5f;
                    float gr = (ty + 0.5f) / TextureMultiplier - 0.5f;
                    // Flip Y: texel row 0 = grid row (_rows-1).
                    gr = (_rows - 1) - gr;

                    float dist = SampleDistanceField(landDist, gc, gr);

                    // Smooth-noise perturbation so the shoreline isn't rectangular.
                    float shoreNoise = Mathf.PerlinNoise(
                        gc * 0.3f + _seedOffsetX + 300f,
                        gr * 0.3f + _seedOffsetY + 300f
                    );
                    float distPerturb = Mathf.Lerp(-0.5f, 0.5f, shoreNoise);
                    float distNorm = Mathf.Clamp01((dist + distPerturb) / maxDist);

                    float alphaNoise = Mathf.PerlinNoise(
                        gc * 0.4f + _seedOffsetX + 500f,
                        gr * 0.4f + _seedOffsetY + 500f
                    );
                    float alphaPerturb = Mathf.Lerp(-0.5f, 0.5f, alphaNoise);
                    float tAlpha = Mathf.Clamp01((dist + alphaPerturb) / ShoreFalloffCells);
                    float alphaBlend = tAlpha * tAlpha * tAlpha * (tAlpha * (tAlpha * 6f - 15f) + 10f);

                    // Diffusion R/A — same Perlin+smootherstep machinery as shore-G above,
                    // but the fade only lives in sea cells. Fresh-only cells, land, and
                    // sand are pinned to their override values so the bilinear distance
                    // sample can't leak the fade across fresh↔land boundaries (shore-G
                    // already handles those). Inside sea cells the BFS distance + Perlin
                    // perturbation produces the organic wavy boundary the user wants.
                    int cellCol = Mathf.Clamp(Mathf.RoundToInt(gc), 0, _columns - 1);
                    int cellRow = Mathf.Clamp(Mathf.RoundToInt(gr), 0, _rows - 1);
                    bool cellIsSea = _isSea != null && _isSea[cellCol, cellRow];
                    bool cellIsFresh = _isFreshwater != null && _isFreshwater[cellCol, cellRow];

                    float diffNoise = Mathf.PerlinNoise(
                        gc * 0.3f + _seedOffsetX + 700f,
                        gr * 0.3f + _seedOffsetY + 700f
                    );
                    float diffPerturb = Mathf.Lerp(-DiffusionPerturbCells, DiffusionPerturbCells, diffNoise);

                    // R — freshwater visibility multiplier consumed by the fresh shader.
                    float freshR;
                    if (!cellIsSea)
                    {
                        // Fresh-only and land/sand: no diffusion fade. Shore-G handles
                        // fresh-vs-land at the actual water shore.
                        freshR = 1f;
                    }
                    else
                    {
                        float fDist = _freshOnlyDistance != null
                            ? SampleDistanceField(_freshOnlyDistance, gc, gr)
                            : float.MaxValue;
                        if (fDist >= float.MaxValue)
                        {
                            freshR = 0f;
                        }
                        else
                        {
                            float t = Mathf.Clamp01((fDist + diffPerturb) / FreshFalloffCells);
                            float s = t * t * t * (t * (t * 6f - 15f) + 10f);
                            freshR = 1f - s;
                        }
                    }

                    // A — "freshwater is on top" signal + sea-side halo plume. Sea-side
                    // shader masks (sprite-edge, foam, procedural alpha) read this.
                    float seaA;
                    if (cellIsFresh)
                    {
                        // Any fresh cell — fresh on top, hide sea-side artefacts here.
                        seaA = 1f;
                    }
                    else if (cellIsSea)
                    {
                        float oDist = _overlapDistance != null
                            ? SampleDistanceField(_overlapDistance, gc, gr)
                            : float.MaxValue;
                        if (oDist >= float.MaxValue)
                        {
                            seaA = 0f;
                        }
                        else
                        {
                            float t = Mathf.Clamp01((oDist + diffPerturb) / DiffusionFalloffCells);
                            float s = t * t * t * (t * (t * 6f - 15f) + 10f);
                            seaA = 1f - s;
                        }
                    }
                    else
                    {
                        // Land/sand: 0 so sea sprite edges and foam don't fade at sea-land.
                        seaA = 0f;
                    }

                    pixels[ty * texW + tx] = new Color(freshR, alphaBlend, distNorm, seaA);
                }
            }

            _altitudeTexture.SetPixels(pixels);
            _altitudeTexture.Apply();
        }

        private float[,] ComputeLandDistanceField()
        {
            bool[,] landSource = new bool[_columns, _rows];
            for (int r = 0; r < _rows; r++)
                for (int c = 0; c < _columns; c++)
                    landSource[c, r] = !_isSea[c, r] && !_isFreshwater[c, r];
            return ComputeDistanceField(landSource);
        }

        // 8-connected BFS distance field. Sources are cells where source[c,r] is true.
        // Returns float[columns,rows] with float.MaxValue at unreached cells.
        // Diagonal step cost = sqrt(2). Matches the existing shore-distance algorithm.
        // When traversable is non-null, the BFS only expands into cells where
        // traversable[c,r] is true (source cells are still seeded regardless).
        private float[,] ComputeDistanceField(bool[,] source, bool[,] traversable = null)
        {
            float[,] dist = new float[_columns, _rows];
            Queue<(int, int)> queue = new Queue<(int, int)>();
            for (int r = 0; r < _rows; r++)
            {
                for (int c = 0; c < _columns; c++)
                {
                    if (source[c, r])
                    {
                        dist[c, r] = 0f;
                        queue.Enqueue((c, r));
                    }
                    else
                    {
                        dist[c, r] = float.MaxValue;
                    }
                }
            }
            while (queue.Count > 0)
            {
                var (cc, cr) = queue.Dequeue();
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nc = cc + dx, nr = cr + dy;
                        if (nc < 0 || nc >= _columns || nr < 0 || nr >= _rows) continue;
                        if (traversable != null && !traversable[nc, nr]) continue;
                        float step = (dx != 0 && dy != 0) ? 1.4142136f : 1f;
                        float newDist = dist[cc, cr] + step;
                        if (newDist < dist[nc, nr])
                        {
                            dist[nc, nr] = newDist;
                            queue.Enqueue((nc, nr));
                        }
                    }
                }
            }
            return dist;
        }

        // BFS distance field from fresh-only cells (cells where _isFreshwater
        // is true and _isSea is false — zone 4, 5 in Largo). Sampled bilinearly
        // and perturbed at texture-bake time to drive the freshwater visibility
        // gradient: 1 deep inside fresh-only cells, smoothly fading toward 0
        // through overlap and adjacent sea cells.
        private float[,] ComputeFreshOnlyDistance()
        {
            if (_isFreshwater == null || _isSea == null) return new float[_columns, _rows];
            bool[,] source = new bool[_columns, _rows];
            for (int r = 0; r < _rows; r++)
                for (int c = 0; c < _columns; c++)
                    source[c, r] = _isFreshwater[c, r] && !_isSea[c, r];
            return ComputeDistanceField(source);
        }

        // BFS distance field from overlap cells (cells where _isFreshwater AND
        // _isSea both hold — zone 6 in Largo). Drives the sea-side halo plume
        // and acts as the "freshwater is on top" signal for sea sprite-edge,
        // foam, and procedural-alpha gating. Returns float.MaxValue everywhere
        // when no overlap exists (so smoothstep collapses to 0).
        private float[,] ComputeOverlapDistance()
        {
            float[,] empty = new float[_columns, _rows];
            if (_isFreshwater == null || _isSea == null) return empty;

            bool[,] source = new bool[_columns, _rows];
            bool any = false;
            for (int r = 0; r < _rows; r++)
            {
                for (int c = 0; c < _columns; c++)
                {
                    if (_isFreshwater[c, r] && _isSea[c, r])
                    {
                        source[c, r] = true;
                        any = true;
                    }
                }
            }
            if (!any)
            {
                for (int r = 0; r < _rows; r++)
                    for (int c = 0; c < _columns; c++)
                        empty[c, r] = float.MaxValue;
                return empty;
            }
            return ComputeDistanceField(source);
        }

        private float ComputeMaxWaterDistance(float[,] landDist)
        {
            float maxDist = 0f;
            for (int r = 0; r < _rows; r++)
                for (int c = 0; c < _columns; c++)
                    if ((_isSea[c, r] || _isFreshwater[c, r]) && landDist[c, r] > maxDist)
                        maxDist = landDist[c, r];
            return maxDist < 0.001f ? 1f : maxDist;
        }

        private float SampleDistanceField(float[,] field, float gc, float gr)
        {
            float fx = Mathf.Clamp(gc, 0f, _columns - 1f);
            float fy = Mathf.Clamp(gr, 0f, _rows - 1f);

            int x0 = Mathf.Clamp(Mathf.FloorToInt(fx), 0, _columns - 1);
            int x1 = Mathf.Min(x0 + 1, _columns - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(fy), 0, _rows - 1);
            int y1 = Mathf.Min(y0 + 1, _rows - 1);

            float sx = fx - x0;
            float sy = fy - y0;

            float v00 = field[x0, y0];
            float v10 = field[x1, y0];
            float v01 = field[x0, y1];
            float v11 = field[x1, y1];

            return Mathf.Lerp(Mathf.Lerp(v00, v10, sx), Mathf.Lerp(v01, v11, sx), sy);
        }
    }
}
