using System.Collections.Generic;
using Glitchers.EcoKnow.Sandbox.Grid;
using UnityEngine;

namespace Glitchers.EcoKnow.Sandbox.Rendering
{
    // Static helper for stochastic, continuous-position placement of pixel-art entities
    // within a region. Combines:
    //   • A per-cell weight that biases selection toward beach-adjacent cells (configurable
    //     via beachAttraction). When attraction is 0 the distribution is uniform over the
    //     region; as it grows the distribution collapses onto the shoreline.
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
            public float BeachAttraction;   // 0 = uniform, larger = stronger shoreline bias.
            public float BeachDistanceScale;// Cells of "reach" for the attraction falloff. Larger = entities far from shore still attracted.
        }

        // Produces `count` world-space positions distributed across the given region
        // cells. Positions are continuous (not snapped to cell centres) — see file
        // header for the placement strategy.
        public static List<Vector3> Sample(
            IReadOnlyList<(int col, int row)> regionCells,
            int count,
            GridManager grid,
            int beachZoneId,
            Settings settings,
            System.Random rng)
        {
            List<Vector3> positions = new List<Vector3>(Mathf.Max(0, count));
            if (regionCells == null || regionCells.Count == 0 || count <= 0 || grid == null || rng == null)
            {
                return positions;
            }

            float[] beachDistances = ComputeBeachDistances(regionCells, grid, beachZoneId);
            float[] cumulativeWeights = BuildCumulativeWeights(beachDistances, settings);
            float totalWeight = cumulativeWeights[cumulativeWeights.Length - 1];
            if (totalWeight <= 0f)
            {
                // Degenerate (e.g. attraction set without any beach neighbours): fall back
                // to uniform so the caller still gets `count` placements.
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

        // Builds a cumulative-weight array so cell selection is a single binary search
        // per sprite. Each entry is `base_weight * exp(-distance * falloff * attraction)`,
        // collapsing to uniform when attraction is 0.
        private static float[] BuildCumulativeWeights(float[] beachDistances, Settings settings)
        {
            float[] cumulative = new float[beachDistances.Length];
            float scale = Mathf.Max(0.001f, settings.BeachDistanceScale);
            float falloff = BeachWeightFalloff * Mathf.Max(0f, settings.BeachAttraction);
            float running = 0f;
            for (int i = 0; i < beachDistances.Length; i++)
            {
                float d = beachDistances[i];
                float weight;
                if (float.IsPositiveInfinity(d) || falloff <= 0f)
                {
                    weight = 1f;
                }
                else
                {
                    weight = Mathf.Exp(-(d / scale) * falloff);
                }
                running += weight;
                cumulative[i] = running;
            }
            return cumulative;
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
