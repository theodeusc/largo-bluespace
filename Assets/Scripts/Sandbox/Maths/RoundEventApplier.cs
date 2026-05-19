using Glitchers.EcoKnow.Sandbox.Grid.Regions;
using UnityEngine;

namespace Glitchers.EcoKnow.Sandbox
{
    // Applies the scenario's per-round addition / decline events to the relevant compute cells.
    // Additive semantics: Addition adds (multiplier * addition_value), Decline subtracts
    // (multiplier * decline_value). Nothing is a no-op. The pollutant colour formula reads
    // populations directly to compute its norms, so the visual response follows naturally.
    //
    // Estuary regions alias to freshwater (see RegionComputeManager.BuildEstuaryAliases) and
    // are skipped here so addition/decline never double-applies. Ignored land zones (Grass,
    // Crops, Golf, Sand) get no entries in AnnualAdditions/Declines and are no-ops.
    public static class RoundEventApplier
    {
        private const string LogChannel = "[RoundEventApplier]";

        public static void Apply(int round, Scenario scenario, EntityManager entityManager, RegionComputeManager regionManager)
        {
            if (scenario == null || entityManager == null) return;
            if (scenario.RoundSchedule == null || scenario.RoundSchedule.Length == 0) return;
            if (round < 0 || round >= scenario.RoundSchedule.Length)
            {
                if (round >= 0) Debug.LogWarning($"{LogChannel} Round {round} is outside RoundSchedule of length {scenario.RoundSchedule.Length}. Treating as Nothing.");
                return;
            }

            RoundEvent ev = scenario.RoundSchedule[round];
            if (ev == null || ev.Kind == EventKind.Nothing || ev.Multiplier == 0f) return;

            EntityRate[] rates = ev.Kind == EventKind.Addition ? scenario.AnnualAdditions : scenario.AnnualDeclines;
            if (rates == null || rates.Length == 0) return;

            double sign = ev.Kind == EventKind.Addition ? 1.0 : -1.0;
            double multiplier = ev.Multiplier;

            // The scenario data set is small (5 pollutants x ~3 water zones x N regions),
            // so the nested iteration here is cheap and clearer than building a lookup table.
            for (int i = 0; i < rates.Length; i++)
            {
                EntityRate rate = rates[i];
                if (rate == null) continue;
                int entityIndex = entityManager.GetEntityIndex(rate.EntityID);
                if (entityIndex < 0)
                {
                    Debug.LogWarning($"{LogChannel} Unknown entity ID '{rate.EntityID}' in rate table.");
                    continue;
                }

                double delta = sign * multiplier * rate.Value;
                if (delta == 0.0) continue;
                ApplyDeltaToZone(entityManager, regionManager, rate.ZoneID, entityIndex, delta);
            }
        }

        private static void ApplyDeltaToZone(EntityManager entityManager, RegionComputeManager regionManager, int zoneId, int entityIndex, double delta)
        {
            if (regionManager == null || !regionManager.IsActive) return;

            foreach (int regionId in regionManager.AllRegionIds)
            {
                if (regionManager.IsAliased(regionId)) continue;
                if (!regionManager.TryGetCenterCell(regionId, out var center)) continue;
                int centerZone = entityManager.GetZoneType(center.col, center.row);
                if (centerZone != zoneId) continue;
                long current = entityManager.RawGetPopulation(center.col, center.row, entityIndex);
                if (current < 0L) continue;
                long newValue = ClampToLong((double)current + delta);
                entityManager.RawSetPopulation(center.col, center.row, entityIndex, newValue);
            }
        }

        private static long ClampToLong(double v)
        {
            if (double.IsNaN(v) || v <= 0.0) return 0L;
            if (v >= (double)long.MaxValue) return long.MaxValue;
            return (long)System.Math.Floor(v);
        }
    }
}
