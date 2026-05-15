using System;
using System.Collections.Generic;
using System.Linq;
using Glitchers.EcoKnow.Sandbox.Terrain;
using UnityEngine;

namespace Glitchers.EcoKnow.Sandbox.Grid.Regions
{
    // Static, one-shot region builder used when a scenario sets UseRegionWideCompute = true.
    // Groups cells by zone-id-based type rules, picks a single "compute" cell per region,
    // sums per-cell entity populations into the compute cell, zeros all visual cells, then
    // builds a region-adjacency graph. After Initialize the manager never mutates region
    // shape again — gameplay reads the static maps via ResolveCell / GetCellType.
    public class RegionComputeManager
    {
        private const string LogChannel = "[RegionComputeManager]";

        // Zone IDs as declared in the Map_Largo.csv #zones header.
        private const int ZONE_SEAWATER = 0;
        private const int ZONE_SAND = 1;
        private const int ZONE_GRASS = 2;
        private const int ZONE_FRESHWATER = 4;
        private const int ZONE_ESTUARY = 5;
        private const int ZONE_RIVERMOUTH = 6;
        private const int ZONE_CROPS = 7;
        private const int ZONE_OVERFLOW = 8;

        // 8-way for flood fill and inter-region adjacency. Matches StandardCalculator's
        // neighbour rule. Diagonal hops are gated by IsDiagonalBlocked using _cellPriority.
        private static readonly int[] DX8 = { 1, 1, 1, 0, 0, -1, -1, -1 };
        private static readonly int[] DY8 = { 1, 0, -1, 1, -1, 1, 0, -1 };

        // Larger than any plausible WithinLayerRank (TerrainPriority has at most 4 entries
        // per layer today). Used to invert the rank so "top of layer" becomes the highest
        // composite priority. Cheaper than threading max-rank through every call.
        private const int RANK_INVERSION_BASE = 100;

        private bool _isActive;
        private int _columns;
        private int _rows;
        private CellType[,] _cellTypes;
        private int[,] _cellToRegion;
        private int[,] _cellPriority;
        private int _nextRegionId;

        private readonly Dictionary<int, (int col, int row)> _computeCells = new Dictionary<int, (int col, int row)>();
        private readonly Dictionary<int, RegionType> _regionType = new Dictionary<int, RegionType>();
        private readonly Dictionary<int, HashSet<int>> _regionAdjacency = new Dictionary<int, HashSet<int>>();
        private readonly Dictionary<int, List<(int col, int row)>> _regionCells = new Dictionary<int, List<(int col, int row)>>();

        public bool IsActive => _isActive;
        public int RegionCount => _computeCells.Count;
        public int Columns => _columns;
        public int Rows => _rows;
        public IEnumerable<int> AllRegionIds => _computeCells.Keys;

        public void Initialize(GridManager gridManager, EntityManager entityManager)
        {
            if (gridManager == null || entityManager == null)
            {
                Debug.LogError($"{LogChannel} Initialize called with null GridManager or EntityManager. Region mode will not activate.");
                return;
            }

            Vector2 size = gridManager.GridSize;
            _columns = (int)size.x;
            _rows = (int)size.y;

            _cellTypes = new CellType[_columns, _rows];
            _cellToRegion = new int[_columns, _rows];
            _cellPriority = new int[_columns, _rows];
            for (int c = 0; c < _columns; c++)
            {
                for (int r = 0; r < _rows; r++)
                {
                    _cellTypes[c, r] = CellType.Visual;
                    _cellToRegion[c, r] = -1;
                    _cellPriority[c, r] = ComputeCellPriority(gridManager, c, r);
                }
            }

            bool[,] visited = new bool[_columns, _rows];

            // One pass per logical zone-group. Seawater (zone 0) and Beach (zone 1) are
            // separate regions even when adjacent — the sand band and seawater bay touch
            // along their boundary but each forms its own region. Estuary merges {5,6}
            // because RiverMouth is conceptually part of the estuary mix.
            FloodAllSeeds(gridManager, visited, RegionType.Seawater,
                seedPredicate: z => z == ZONE_SEAWATER,
                memberPredicate: z => z == ZONE_SEAWATER);

            FloodAllSeeds(gridManager, visited, RegionType.Beach,
                seedPredicate: z => z == ZONE_SAND,
                memberPredicate: z => z == ZONE_SAND);

            FloodAllSeeds(gridManager, visited, RegionType.Freshwater,
                seedPredicate: z => z == ZONE_FRESHWATER,
                memberPredicate: z => z == ZONE_FRESHWATER);

            FloodAllSeeds(gridManager, visited, RegionType.Estuary,
                seedPredicate: z => z == ZONE_ESTUARY || z == ZONE_RIVERMOUTH,
                memberPredicate: z => z == ZONE_ESTUARY || z == ZONE_RIVERMOUTH);

            FloodAllSeeds(gridManager, visited, RegionType.Golf,
                seedPredicate: z => z == ZONE_GRASS,
                memberPredicate: z => z == ZONE_GRASS);

            FloodAllSeeds(gridManager, visited, RegionType.Crops,
                seedPredicate: z => z == ZONE_CROPS,
                memberPredicate: z => z == ZONE_CROPS);

            FloodAllSeeds(gridManager, visited, RegionType.Overflow,
                seedPredicate: z => z == ZONE_OVERFLOW,
                memberPredicate: z => z == ZONE_OVERFLOW);

            // Catch-all so any unrecognized non-void zone still gets a region (same-zone-id grouping).
            FallbackFlood(gridManager, visited);

            AggregatePopulations(entityManager);
            BuildAdjacency();

            _isActive = true;
        }

        public (int col, int row) ResolveCell(int col, int row)
        {
            if (!_isActive) return (col, row);
            if (col < 0 || col >= _columns || row < 0 || row >= _rows) return (col, row);
            if (_cellTypes[col, row] == CellType.Compute) return (col, row);

            int regionId = _cellToRegion[col, row];
            if (regionId >= 0 && _computeCells.TryGetValue(regionId, out var center))
            {
                return center;
            }
            return (col, row);
        }

        public CellType GetCellType(int col, int row)
        {
            if (!_isActive) return CellType.Compute;
            if (col < 0 || col >= _columns || row < 0 || row >= _rows) return CellType.Compute;
            return _cellTypes[col, row];
        }

        public int GetRegionId(int col, int row)
        {
            if (!_isActive) return -1;
            if (col < 0 || col >= _columns || row < 0 || row >= _rows) return -1;
            return _cellToRegion[col, row];
        }

        public bool TryGetCenterCell(int regionId, out (int col, int row) center)
        {
            return _computeCells.TryGetValue(regionId, out center);
        }

        public RegionType GetRegionType(int regionId)
        {
            return _regionType.TryGetValue(regionId, out RegionType t) ? t : RegionType.Other;
        }

        public IEnumerable<int> GetAdjacentRegions(int regionId)
        {
            if (_regionAdjacency.TryGetValue(regionId, out HashSet<int> neighbours))
            {
                return neighbours;
            }
            return Enumerable.Empty<int>();
        }

        private void FloodAllSeeds(
            GridManager gridManager,
            bool[,] visited,
            RegionType type,
            Func<int, bool> seedPredicate,
            Func<int, bool> memberPredicate)
        {
            for (int c = 0; c < _columns; c++)
            {
                for (int r = 0; r < _rows; r++)
                {
                    if (visited[c, r]) continue;
                    if (gridManager.IsVoid(c, r)) continue;
                    int zone = gridManager.GetZoneType(c, r);
                    if (!seedPredicate(zone)) continue;

                    int regionId = _nextRegionId++;
                    _regionType[regionId] = type;
                    _regionCells[regionId] = new List<(int col, int row)>();

                    FloodFill(gridManager, visited, c, r, regionId, memberPredicate);

                    SelectAndMarkCenter(regionId);
                }
            }
        }

        private void FloodFill(
            GridManager gridManager,
            bool[,] visited,
            int seedCol,
            int seedRow,
            int regionId,
            Func<int, bool> memberPredicate)
        {
            Queue<(int c, int r)> queue = new Queue<(int c, int r)>();
            queue.Enqueue((seedCol, seedRow));
            visited[seedCol, seedRow] = true;

            while (queue.Count > 0)
            {
                var (c, r) = queue.Dequeue();
                _cellToRegion[c, r] = regionId;
                _regionCells[regionId].Add((c, r));

                for (int i = 0; i < 8; i++)
                {
                    int nc = c + DX8[i];
                    int nr = r + DY8[i];
                    if (nc < 0 || nc >= _columns || nr < 0 || nr >= _rows) continue;
                    if (visited[nc, nr]) continue;
                    if (gridManager.IsVoid(nc, nr)) continue;
                    int nzone = gridManager.GetZoneType(nc, nr);
                    if (!memberPredicate(nzone)) continue;

                    // Diagonal hops join the region only if at least one of the two "bridge"
                    // cells (the orthogonal neighbours that share an edge with both source
                    // and target) is NOT visually superseding the source. Mirrors the rule
                    // in creator_old/ComputeRegionManager.IsDiagonalBlocked so that e.g. two
                    // crops cells touching at a corner over void/dirt corners join into one
                    // region, but two cells separated at the corner by higher terrain stay split.
                    bool isDiagonal = DX8[i] != 0 && DY8[i] != 0;
                    if (isDiagonal && IsDiagonalBlocked(c, r, nc, nr)) continue;

                    visited[nc, nr] = true;
                    queue.Enqueue((nc, nr));
                }
            }
        }

        private static int ComputeCellPriority(GridManager gridManager, int col, int row)
        {
            if (gridManager.IsVoid(col, row)) return -1;
            int zone = gridManager.GetZoneType(col, row);
            if (!TerrainPriority.ZoneTerrainStacks.TryGetValue(zone, out string[] terrains)) return -1;

            int best = int.MinValue;
            for (int i = 0; i < terrains.Length; i++)
            {
                int layer = TerrainPriority.LayerOf(terrains[i]);
                if (layer < 0) continue; // non-renderable marker (e.g. Overflow)
                int rank = TerrainPriority.WithinLayerRank(terrains[i]);
                // Composite priority where higher = more visually on top.
                // Layer contributes the thousands; within-layer index is inverted so
                // entry [0] (top of layer) outranks entry [1+].
                int p = layer * 1000 + (RANK_INVERSION_BASE - rank);
                if (p > best) best = p;
            }
            return best == int.MinValue ? -1 : best;
        }

        private bool IsDiagonalBlocked(int cx, int cy, int nx, int ny)
        {
            if (_cellPriority == null) return false;
            int myPriority = _cellPriority[cx, cy];
            // Bridge cells are always in-bounds here because callers pre-check (nx, ny).
            int bridge1 = _cellPriority[nx, cy];
            int bridge2 = _cellPriority[cx, ny];
            return bridge1 > myPriority && bridge2 > myPriority;
        }

        private void FallbackFlood(GridManager gridManager, bool[,] visited)
        {
            for (int c = 0; c < _columns; c++)
            {
                for (int r = 0; r < _rows; r++)
                {
                    if (visited[c, r]) continue;
                    if (gridManager.IsVoid(c, r)) continue;

                    int zone = gridManager.GetZoneType(c, r);
                    int regionId = _nextRegionId++;
                    _regionType[regionId] = RegionType.Other;
                    _regionCells[regionId] = new List<(int col, int row)>();

                    FloodFill(gridManager, visited, c, r, regionId, z => z == zone);
                    SelectAndMarkCenter(regionId);
                }
            }
        }

        private void SelectAndMarkCenter(int regionId)
        {
            List<(int col, int row)> cells = _regionCells[regionId];
            if (cells.Count == 0) return;

            float avgCol = 0f;
            float avgRow = 0f;
            for (int i = 0; i < cells.Count; i++)
            {
                avgCol += cells[i].col;
                avgRow += cells[i].row;
            }
            avgCol /= cells.Count;
            avgRow /= cells.Count;

            int bestIdx = 0;
            float bestDist = float.MaxValue;
            for (int i = 0; i < cells.Count; i++)
            {
                float dx = cells[i].col - avgCol;
                float dy = cells[i].row - avgRow;
                float d = dx * dx + dy * dy;
                if (d < bestDist)
                {
                    bestDist = d;
                    bestIdx = i;
                }
            }

            (int col, int row) center = cells[bestIdx];
            _computeCells[regionId] = center;
            _cellTypes[center.col, center.row] = CellType.Compute;
        }

        private void AggregatePopulations(EntityManager entityManager)
        {
            int entityCount = entityManager.EntityTypeCount;
            if (entityCount <= 0) return;

            foreach (var kv in _regionCells)
            {
                int regionId = kv.Key;
                List<(int col, int row)> cells = kv.Value;
                if (!_computeCells.TryGetValue(regionId, out var center)) continue;

                for (int e = 0; e < entityCount; e++)
                {
                    int sum = 0;
                    for (int i = 0; i < cells.Count; i++)
                    {
                        int pop = entityManager.RawGetPopulation(cells[i].col, cells[i].row, e);
                        if (pop > 0) sum += pop;
                    }
                    entityManager.RawSetPopulation(center.col, center.row, e, sum);
                }

                for (int i = 0; i < cells.Count; i++)
                {
                    if (cells[i].col == center.col && cells[i].row == center.row) continue;
                    for (int e = 0; e < entityCount; e++)
                    {
                        entityManager.RawSetPopulation(cells[i].col, cells[i].row, e, 0);
                    }
                }
            }
        }

        private void BuildAdjacency()
        {
            foreach (int id in _computeCells.Keys)
            {
                _regionAdjacency[id] = new HashSet<int>();
            }

            for (int c = 0; c < _columns; c++)
            {
                for (int r = 0; r < _rows; r++)
                {
                    int rid = _cellToRegion[c, r];
                    if (rid < 0) continue;

                    for (int i = 0; i < 8; i++)
                    {
                        int nc = c + DX8[i];
                        int nr = r + DY8[i];
                        if (nc < 0 || nc >= _columns || nr < 0 || nr >= _rows) continue;
                        int nrid = _cellToRegion[nc, nr];
                        if (nrid < 0 || nrid == rid) continue;

                        // Symmetric diagonal-block: regions are still considered adjacent if
                        // either side sees through the corner. Only block when BOTH endpoints
                        // are visually wedged off by higher-priority terrain at the bridges.
                        bool isDiagonal = DX8[i] != 0 && DY8[i] != 0;
                        if (isDiagonal && IsDiagonalBlocked(c, r, nc, nr) && IsDiagonalBlocked(nc, nr, c, r))
                        {
                            continue;
                        }

                        _regionAdjacency[rid].Add(nrid);
                    }
                }
            }
        }

    }
}
