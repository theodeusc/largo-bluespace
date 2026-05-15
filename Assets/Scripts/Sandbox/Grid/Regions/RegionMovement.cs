using System.Collections.Generic;
using UnityEngine;

namespace Glitchers.EcoKnow.Sandbox.Grid.Regions
{
    // Region-to-region entity movement. Used when EntityManager.IsRegionMode is true,
    // replacing per-cell 8-neighbour movement with hops along the static region adjacency
    // graph gated by RegionMovementPolicy. With an empty policy table no entity moves;
    // populating the table (e.g. RegionMovementPolicy.AllowBidirectional(waterPollutionId,
    // RegionType.Freshwater, RegionType.Estuary, RegionType.Seawater, RegionType.Overflow))
    // turns on specific flows without touching this code.
    public static class RegionMovement
    {
        public static void Run(EntityManager entityManager, RegionComputeManager regions)
        {
            if (entityManager == null || regions == null || !regions.IsActive) return;

            int entityCount = entityManager.EntityTypeCount;
            if (entityCount <= 0) return;

            List<int> regionIds = new List<int>(regions.AllRegionIds);
            Dictionary<int, int[]> deltas = new Dictionary<int, int[]>();
            for (int i = 0; i < regionIds.Count; i++)
            {
                deltas[regionIds[i]] = new int[entityCount];
            }

            for (int e = 0; e < entityCount; e++)
            {
                Entity entity = entityManager.GetEntityType(e);
                if (entity == null) continue;
                float globalRate = entity.MovementRate;
                bool hasZoneOverride = entity.ZoneInformation != null;

                for (int rIdx = 0; rIdx < regionIds.Count; rIdx++)
                {
                    int regionId = regionIds[rIdx];
                    if (!regions.TryGetCenterCell(regionId, out var center)) continue;

                    RegionType fromType = regions.GetRegionType(regionId);

                    int centerZone = entityManager.GetZoneType(center.col, center.row);
                    float rate = globalRate;
                    if (hasZoneOverride)
                    {
                        for (int i = 0; i < entity.ZoneInformation.Length; i++)
                        {
                            if (entity.ZoneInformation[i].ZoneID == centerZone)
                            {
                                rate = entity.ZoneInformation[i].MovementRate;
                                break;
                            }
                        }
                    }
                    if (rate <= 0f) continue;

                    List<int> targets = null;
                    foreach (int adjId in regions.GetAdjacentRegions(regionId))
                    {
                        RegionType toType = regions.GetRegionType(adjId);
                        if (RegionMovementPolicy.CanMove(e, fromType, toType))
                        {
                            if (targets == null) targets = new List<int>();
                            targets.Add(adjId);
                        }
                    }
                    if (targets == null) continue;

                    int currentPop = entityManager.RawGetPopulation(center.col, center.row, e);
                    if (currentPop <= 0) continue;

                    int movers = SampleBinomial(currentPop, rate);
                    if (movers <= 0) continue;

                    int[] distribution = SampleMultinomial(movers, targets.Count);

                    deltas[regionId][e] -= movers;
                    for (int i = 0; i < targets.Count; i++)
                    {
                        deltas[targets[i]][e] += distribution[i];
                    }
                }
            }

            for (int rIdx = 0; rIdx < regionIds.Count; rIdx++)
            {
                int regionId = regionIds[rIdx];
                if (!regions.TryGetCenterCell(regionId, out var center)) continue;
                int[] regionDeltas = deltas[regionId];
                for (int e = 0; e < entityCount; e++)
                {
                    int delta = regionDeltas[e];
                    if (delta == 0) continue;
                    int pop = entityManager.RawGetPopulation(center.col, center.row, e);
                    if (pop < 0) continue;
                    entityManager.RawSetPopulation(center.col, center.row, e, Mathf.Max(0, pop + delta));
                }
            }
        }

        private static int SampleBinomial(int n, float p)
        {
            if (n <= 0 || p <= 0f) return 0;
            if (p >= 1f) return n;

            if (n >= 1000)
            {
                float mean = n * p;
                float stdDev = Mathf.Sqrt(n * p * (1f - p));
                float sample = SampleNormal(mean, stdDev);
                return Mathf.Clamp(Mathf.RoundToInt(sample), 0, n);
            }

            int successes = 0;
            for (int i = 0; i < n; i++)
            {
                if (Random.Range(0f, 1f) <= p) successes++;
            }
            return successes;
        }

        private static float SampleNormal(float mean, float stdDev)
        {
            float u1 = 1f - Random.Range(0f, 1f);
            float u2 = 1f - Random.Range(0f, 1f);
            float standardNormal = Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Sin(2f * Mathf.PI * u2);
            return mean + stdDev * standardNormal;
        }

        private static int[] SampleMultinomial(int totalMovers, int categories)
        {
            int[] results = new int[categories];
            if (categories == 1)
            {
                results[0] = totalMovers;
                return results;
            }
            for (int i = 0; i < totalMovers; i++)
            {
                results[Random.Range(0, categories)]++;
            }
            return results;
        }
    }
}
