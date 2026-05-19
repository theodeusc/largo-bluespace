using System;
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
        // Above this total, the per-mover multinomial loop is replaced with a deterministic
        // proportional split — looping 1e14 times would freeze the game.
        private const long PerMoverLoopThreshold = 1_000_000L;

        public static void Run(EntityManager entityManager, RegionComputeManager regions)
        {
            if (entityManager == null || regions == null || !regions.IsActive) return;

            int entityCount = entityManager.EntityTypeCount;
            if (entityCount <= 0) return;

            List<int> regionIds = new List<int>(regions.AllRegionIds);
            Dictionary<int, long[]> deltas = new Dictionary<int, long[]>();
            for (int i = 0; i < regionIds.Count; i++)
            {
                deltas[regionIds[i]] = new long[entityCount];
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

                    long currentPop = entityManager.RawGetPopulation(center.col, center.row, e);
                    if (currentPop <= 0L) continue;

                    long movers = SampleBinomial(currentPop, rate);
                    if (movers <= 0L) continue;

                    long[] distribution = SampleMultinomial(movers, targets.Count);

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
                long[] regionDeltas = deltas[regionId];
                for (int e = 0; e < entityCount; e++)
                {
                    long delta = regionDeltas[e];
                    if (delta == 0L) continue;
                    long pop = entityManager.RawGetPopulation(center.col, center.row, e);
                    if (pop < 0L) continue;
                    entityManager.RawSetPopulation(center.col, center.row, e, Math.Max(0L, pop + delta));
                }
            }
        }

        private static long SampleBinomial(long n, float p)
        {
            if (n <= 0L || p <= 0f) return 0L;
            if (p >= 1f) return n;

            if (n >= 1000L)
            {
                double mean = (double)n * p;
                double stdDev = Math.Sqrt((double)n * p * (1.0 - p));
                double sample = SampleNormal(mean, stdDev);
                if (sample <= 0.0) return 0L;
                if (sample >= (double)n) return n;
                return (long)Math.Round(sample);
            }

            int successes = 0;
            int small = (int)n;
            for (int i = 0; i < small; i++)
            {
                if (UnityEngine.Random.Range(0f, 1f) <= p) successes++;
            }
            return successes;
        }

        private static double SampleNormal(double mean, double stdDev)
        {
            double u1 = 1.0 - UnityEngine.Random.Range(0f, 1f);
            double u2 = 1.0 - UnityEngine.Random.Range(0f, 1f);
            double standardNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
            return mean + stdDev * standardNormal;
        }

        private static long[] SampleMultinomial(long totalMovers, int categories)
        {
            long[] results = new long[categories];
            if (categories == 1)
            {
                results[0] = totalMovers;
                return results;
            }
            if (totalMovers > PerMoverLoopThreshold)
            {
                long perCat = totalMovers / categories;
                long remainder = totalMovers - (perCat * categories);
                for (int i = 0; i < categories; i++) results[i] = perCat;
                results[0] += remainder;
                return results;
            }
            int loopCount = (int)totalMovers;
            for (int i = 0; i < loopCount; i++)
            {
                results[UnityEngine.Random.Range(0, categories)]++;
            }
            return results;
        }
    }
}
