using System.Collections.Generic;
using Glitchers.EcoKnow.Sandbox.Grid;
using UnityEngine;

namespace Glitchers.EcoKnow.Sandbox.Rendering
{
    // Static helper for stochastic, continuous-position placement of pixel-art entities
    // within a region. Combines:
    //   • A per-cell weight that optionally biases selection toward cells adjacent to a
    //     configured attraction zone (Settings.AttractionZoneId). When Settings.BeachAttraction
    //     is 0 the BFS distance field is skipped entirely and the distribution is uniform
    //     across the supplied cells; as attraction grows the distribution collapses onto
    //     the shoreline of that zone.
    //   • Sub-cell jitter so multiple sprites in the same cell each land at unique
    //     world-space positions rather than stacking on the cell centre.
    //   • Perlin-noise density masking so sprites form natural clumps and bare patches
    //     instead of pure white-noise scatter.
    // Determinism: the supplied System.Random is the sole entropy source. Construct it from
    // scenario.Seed at the call site and the scatter will replay identically.
    public static class PixelArtDistributionSampler
    {
        // How aggressively to fall off beach-attraction weight with shoreline distance.
        // 1.0 means weight halves roughly every distanceScale cells from the shore.
        private const float BeachWeightFalloff = 0.7f;

        // Max attempts to satisfy the perlin density mask before accepting a position
        // anyway. Keeps the sampler from looping forever in pathological cases.
        private const int MaxPerlinAttempts = 6;

        // Perlin samples below this threshold are treated as "empty space" and rejected
        // (subject to MaxPerlinAttempts). Higher value = sparser, tighter clumps.
        private const float PerlinAcceptThreshold = 0.45f;

        public struct Settings
        {
            public float PerlinScale;       // Noise frequency. Larger = finer/tighter clumps.
            public float BeachAttraction;   // 0 = uniform (BFS skipped), larger = stronger shoreline bias.
            public float BeachDistanceScale;// Cells of "reach" for the attraction falloff. Larger = entities far from shore still attracted. Unused when BeachAttraction <= 0.
            public int AttractionZoneId;    // Zone id whose adjacent cells the bias pulls toward. Unused when BeachAttraction <= 0.
            public int EdgeBufferCells;     // Cells closer than this to the region boundary are excluded from selection. 0 = no buffer (entities can sit on the boundary). 1 = exclude only the boundary cells. Falls back to no buffer if the region is too small to satisfy the buffer.
        }

        // Produces `count` world-space positions distributed across the given region
        // cells. Positions are continuous (not snapped to cell centres) — see file
        // header for the placement strategy.
        public static List<Vector3> Sample(
            IReadOnlyList<(int col, int row)> regionCells,
            int count,
            GridManager grid,
            Settings settings,
            System.Random rng)
        {
            List<Vector3> positions = new List<Vector3>(Mathf.Max(0, count));
            if (regionCells == null || regionCells.Count == 0 || count <= 0 || grid == null || rng == null)
            {
                return positions;
            }

            // Optional edge-buffer mask: cells closer than EdgeBufferCells to the region
            // boundary get zero weight (and so won't be selected). Built once per call and
            // shared by both the attraction and uniform weighting paths.
            bool[] validMask = settings.EdgeBufferCells > 0
                ? ComputeInteriorMask(regionCells, settings.EdgeBufferCells)
                : null;

            // BFS distance field is only built when attraction is requested. With attraction
            // off, every cell weighs the same and the cumulative array degenerates to 1..N
            // (modulo the buffer mask).
            float[] cumulativeWeights = BuildWeightedCumulative(regionCells, grid, settings, validMask);
            float totalWeight = cumulativeWeights[cumulativeWeights.Length - 1];

            // Buffer was so aggressive it excluded every cell (region too small): retry with
            // no mask so the caller still gets `count` placements somewhere inside the region.
            if (totalWeight <= 0f && validMask != null)
            {
                cumulativeWeights = BuildWeightedCumulative(regionCells, grid, settings, null);
                totalWeight = cumulativeWeights[cumulativeWeights.Length - 1];
            }
            if (totalWeight <= 0f)
            {
                // Still degenerate (e.g. attraction set without any matching neighbours):
                // fall back to fully uniform so the caller still gets `count` placements.
                for (int i = 0; i < cumulativeWeights.Length; i++) cumulativeWeights[i] = i + 1;
                totalWeight = cumulativeWeights.Length;
            }

            GridCoords coords = grid.Coords;
            float cellStep = coords.CellStep;
            float seedOffsetX = (float)(rng.NextDouble() * 1000.0);
            float seedOffsetY = (float)(rng.NextDouble() * 1000.0);

            for (int i = 0; i < count; i++)
            {
                int cellIdx = WeightedPick(cumulativeWeights, totalWeight, rng);
                (int col, int row) = regionCells[cellIdx];
                Vector3 cellCenter = coords.CellToWorld(col, row);

                Vector3 placement = cellCenter;
                for (int attempt = 0; attempt < MaxPerlinAttempts; attempt++)
                {
                    float jitterX = (float)(rng.NextDouble() - 0.5);
                    float jitterY = (float)(rng.NextDouble() - 0.5);
                    Vector3 candidate = new Vector3(
                        cellCenter.x + jitterX * cellStep,
                        cellCenter.y + jitterY * cellStep,
                        0f);

                    float noise = Mathf.PerlinNoise(
                        candidate.x * settings.PerlinScale + seedOffsetX,
                        candidate.y * settings.PerlinScale + seedOffsetY);

                    if (noise >= PerlinAcceptThreshold || attempt == MaxPerlinAttempts - 1)
                    {
                        placement = candidate;
                        break;
                    }
                }
                positions.Add(placement);
            }

            return positions;
        }

        // Per-cell BFS distance (in cells) to the nearest cell that touches a beach
        // neighbour. Cells that are themselves beach-adjacent get distance 0; isolated
        // cells get large values. Used to weight selection toward the shoreline.
        private static float[] ComputeBeachDistances(
            IReadOnlyList<(int col, int row)> regionCells,
            GridManager grid,
            int beachZoneId)
        {
            float[] distances = new float[regionCells.Count];
            Dictionary<(int, int), int> cellIndex = new Dictionary<(int, int), int>(regionCells.Count);
            for (int i = 0; i < regionCells.Count; i++)
            {
                distances[i] = float.PositiveInfinity;
                cellIndex[(regionCells[i].col, regionCells[i].row)] = i;
            }

            Queue<int> frontier = new Queue<int>();
            for (int i = 0; i < regionCells.Count; i++)
            {
                (int col, int row) = regionCells[i];
                if (TouchesZone(grid, col, row, beachZoneId))
                {
                    distances[i] = 0f;
                    frontier.Enqueue(i);
                }
            }

            int[] dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
            int[] dy = { 0, 0, 1, -1, 1, -1, 1, -1 };
            while (frontier.Count > 0)
            {
                int idx = frontier.Dequeue();
                (int col, int row) = regionCells[idx];
                float nextDist = distances[idx] + 1f;
                for (int n = 0; n < 8; n++)
                {
                    int nc = col + dx[n];
                    int nr = row + dy[n];
                    if (!cellIndex.TryGetValue((nc, nr), out int neighbourIdx)) continue;
                    if (distances[neighbourIdx] <= nextDist) continue;
                    distances[neighbourIdx] = nextDist;
                    frontier.Enqueue(neighbourIdx);
                }
            }

            return distances;
        }

        private static bool TouchesZone(GridManager grid, int col, int row, int zoneId)
        {
            int[] dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
            int[] dy = { 0, 0, 1, -1, 1, -1, 1, -1 };
            for (int i = 0; i < 8; i++)
            {
                int nc = col + dx[i];
                int nr = row + dy[i];
                if (grid.GetZoneType(nc, nr) == zoneId) return true;
            }
            return false;
        }

        // Single entry point for building the cumulative-weight array used by WeightedPick.
        // Branches internally on Settings.BeachAttraction so callers don't have to mirror
        // the branch + degenerate-fallback logic. validMask (when non-null) zeroes out the
        // weight of any cell flagged invalid by the edge-buffer pass.
        private static float[] BuildWeightedCumulative(
            IReadOnlyList<(int col, int row)> regionCells,
            GridManager grid,
            Settings settings,
            bool[] validMask)
        {
            int n = regionCells.Count;
            float[] cumulative = new float[n];
            float running = 0f;

            if (settings.BeachAttraction > 0f)
            {
                float[] beachDistances = ComputeBeachDistances(regionCells, grid, settings.AttractionZoneId);
                float scale = Mathf.Max(0.001f, settings.BeachDistanceScale);
                float falloff = BeachWeightFalloff * settings.BeachAttraction;
                for (int i = 0; i < n; i++)
                {
                    if (validMask != null && !validMask[i]) { cumulative[i] = running; continue; }
                    float d = beachDistances[i];
                    float weight = float.IsPositiveInfinity(d) ? 1f : Mathf.Exp(-(d / scale) * falloff);
                    running += weight;
                    cumulative[i] = running;
                }
            }
            else
            {
                for (int i = 0; i < n; i++)
                {
                    if (validMask != null && !validMask[i]) { cumulative[i] = running; continue; }
                    running += 1f;
                    cumulative[i] = running;
                }
            }
            return cumulative;
        }

        // Per-cell BFS distance (in cells) to the nearest cell on the region boundary —
        // a cell with at least one 8-neighbour that is NOT in the supplied region. Cells
        // that themselves sit on the boundary get distance 0. The returned mask is true
        // for cells whose distance is at least `bufferCells`, i.e. cells safely inside the
        // interior of the region.
        private static bool[] ComputeInteriorMask(
            IReadOnlyList<(int col, int row)> regionCells,
            int bufferCells)
        {
            int n = regionCells.Count;
            bool[] valid = new bool[n];
            HashSet<(int, int)> cellSet = new HashSet<(int, int)>(n);
            Dictionary<(int, int), int> cellIndex = new Dictionary<(int, int), int>(n);
            for (int i = 0; i < n; i++)
            {
                cellSet.Add(regionCells[i]);
                cellIndex[regionCells[i]] = i;
            }

            int[] dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
            int[] dy = { 0, 0, 1, -1, 1, -1, 1, -1 };

            float[] dist = new float[n];
            for (int i = 0; i < n; i++) dist[i] = float.PositiveInfinity;

            Queue<int> frontier = new Queue<int>();
            for (int i = 0; i < n; i++)
            {
                (int col, int row) = regionCells[i];
                bool isBoundary = false;
                for (int k = 0; k < 8; k++)
                {
                    if (!cellSet.Contains((col + dx[k], row + dy[k]))) { isBoundary = true; break; }
                }
                if (isBoundary)
                {
                    dist[i] = 0f;
                    frontier.Enqueue(i);
                }
            }

            while (frontier.Count > 0)
            {
                int idx = frontier.Dequeue();
                (int col, int row) = regionCells[idx];
                float nextDist = dist[idx] + 1f;
                for (int k = 0; k < 8; k++)
                {
                    int nc = col + dx[k];
                    int nr = row + dy[k];
                    if (!cellIndex.TryGetValue((nc, nr), out int neighbourIdx)) continue;
                    if (dist[neighbourIdx] <= nextDist) continue;
                    dist[neighbourIdx] = nextDist;
                    frontier.Enqueue(neighbourIdx);
                }
            }

            for (int i = 0; i < n; i++) valid[i] = dist[i] >= bufferCells;
            return valid;
        }

        private static int WeightedPick(float[] cumulative, float total, System.Random rng)
        {
            float target = (float)(rng.NextDouble() * total);
            int lo = 0;
            int hi = cumulative.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (cumulative[mid] < target) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }
    }
}
