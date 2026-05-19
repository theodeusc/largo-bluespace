using Glitchers.EcoKnow.Sandbox.Grid.Regions;
using UnityEngine;

namespace Glitchers.EcoKnow.Sandbox.Terrain
{
    // Drives water-tint shaders from current pollutant compute-cell state.
    //
    // Per zone (freshwater, seawater, overflow):
    //   - For each tracked pollutant (eColi_total = sewage + agri, phosphate, sediment),
    //     average the compute-cell values across non-aliased regions of that zone type.
    //   - Normalise: norm = clamp01((avg - baseline) / addition), so baseline pollution
    //     reads as 0 (current default colour) and (baseline + addition) reads as 1 (full brown).
    //   - Combined "muddiness" = norm_eColi * norm_phosphate * norm_sediment (multiplication
    //     per user spec; small individual norms compound into a strongly subdued combined value).
    //   - Lerp the zone's authored default water colours toward BrownTint by the combined amount.
    //
    // Estuary regions are aliased to freshwater (RegionComputeManager.BuildEstuaryAliases) and
    // contribute no separate state — they render via the freshwater material instance, which is
    // already tinted by this driver.
    //
    // Called from SandboxManager.CalculateMaths (end of each round) and SetupAndRunScenario
    // (right after baselines apply). Safe to call when scenario.ZoneBaselines == null —
    // everything resolves to "no pollution" and the water keeps its authored defaults.
    public static class PollutantColorDriver
    {
        // Zone ids in the Map_Largo.csv #zones header; mirrored from RegionComputeManager
        // private constants. Duplicated here rather than exposed because the visual layer
        // legitimately knows which zone ids correspond to which water surfaces.
        private const int ZoneSeawater = 0;
        private const int ZoneFreshwater = 4;
        private const int ZoneOverflow = 8;

        // Muddy tan brown — full-pollution endpoint for the colour lerp. Tuned lighter than
        // a pure dark brown so even max-tinted freshwater reads as "dirty" rather than near-black.
        private static readonly Color BrownTint = new Color(0.58f, 0.45f, 0.30f, 1f);

        // Per-zone caps on the visible tint amount. The norm computation can return up to 1
        // (full lerp to brown), but visually a clean blue lake doesn't go all the way to mud
        // brown at the worst day, and the ocean's volume dilutes pollution far further. These
        // factors multiply the computed t so even full-pollution rounds keep the water mostly
        // in its authored blue/green range. Tune per scenario to taste.
        private const float MaxFreshwaterTint = 0.7f; // freshwater reads clearly muddy at peak pollution
        private const float MaxSeawaterTint = 0.2f;   // seawater stays mostly blue but tints noticeably
        private const float MaxOverflowTint = 0.7f;   // overflow plume tints similarly to freshwater

        // Pollutant entity IDs the driver looks up. Missing entities resolve to "no contribution"
        // so non-pollutant scenarios (no e_coli/etc defined) behave as no-ops.
        private const string IdSewage = "eColi_sewage";
        private const string IdAgri = "eColi_agri";
        private const string IdPhosphate = "phosphate";
        private const string IdSediment = "sediment";

        // Per-pollutant weights in the combined-tint weighted average. eColi values are
        // many orders of magnitude larger than phosphate/sediment, so the eColi norm
        // saturates at 1 almost any time pollution is present, while phos/sed norms swing
        // more meaningfully across rounds. Lowering eColi's weight stops it from dominating
        // the visible colour variation — phos and sediment retain full weight so their
        // changes drive most of the brown shift. All norms are still 0-1 (normalised against
        // each entity's zone-specific baseline+addition), so the weights only rebalance
        // contribution; max combined tint stays at 1.
        private const float WeightEColi = 0.5f;
        private const float WeightPhosphate = 1.0f;
        private const float WeightSediment = 1.0f;
        private const float TotalWeight = WeightEColi + WeightPhosphate + WeightSediment;

        public static void Refresh(Scenario scenario, EntityManager entityManager, RegionComputeManager regionManager, WaterTintController tintController)
        {
            if (scenario == null || entityManager == null || tintController == null) return;
            if (regionManager == null || !regionManager.IsActive) return;

            float freshT = ComputeZoneNorm(scenario, entityManager, regionManager, RegionType.Freshwater, ZoneFreshwater) * MaxFreshwaterTint;
            float seaT = ComputeZoneNorm(scenario, entityManager, regionManager, RegionType.Seawater, ZoneSeawater) * MaxSeawaterTint;
            float overflowT = ComputeZoneNorm(scenario, entityManager, regionManager, RegionType.Overflow, ZoneOverflow) * MaxOverflowTint;

            // Freshwater: blend shallow / deep / base toward brown by combined norm.
            Color freshShallow = Color.Lerp(tintController.FreshwaterDefaultShallow, BrownTint, freshT);
            Color freshDeep = Color.Lerp(tintController.FreshwaterDefaultDeep, BrownTint, freshT);
            Color freshBase = Color.Lerp(tintController.FreshwaterDefaultBaseTint, BrownTint, freshT);
            tintController.SetFreshwaterTint(freshShallow, freshDeep, freshBase);

            Color seaShallow = Color.Lerp(tintController.SeawaterDefaultShallow, BrownTint, seaT);
            Color seaDeep = Color.Lerp(tintController.SeawaterDefaultDeep, BrownTint, seaT);
            Color seaBase = Color.Lerp(tintController.SeawaterDefaultBaseTint, BrownTint, seaT);
            tintController.SetSeawaterTint(seaShallow, seaDeep, seaBase);

            // Overflow uses a single tint colour + strength uniform rather than shallow/deep.
            // Strength stays at its authored default when overflowT == 0, scaling up toward 1
            // as overflow regions accumulate pollutants. Colour blends similarly so the brown
            // sweep into seawater intensifies with pollution rather than being a fixed shade.
            Color overflowColor = Color.Lerp(tintController.OverflowDefaultTint, BrownTint, overflowT);
            float overflowStrength = Mathf.Lerp(tintController.OverflowDefaultStrength, 1f, overflowT);
            tintController.SetOverflowTint(overflowColor, overflowStrength);
        }

        // Combined per-zone norm in [0, 1]. Returns 0 when there are no regions of the given
        // type, no pollutant entries for the zone, or all pollutants are at/below baseline.
        private static float ComputeZoneNorm(Scenario scenario, EntityManager entityManager, RegionComputeManager regionManager, RegionType regionType, int zoneId)
        {
            // Sum of per-entity baseline and addition across the (zone, entity) pairs that
            // contribute to e_coli. Sewage and agri are kept as two simulation entities and
            // combined here at the colour stage per user spec.
            double baselineSewage = SumValue(scenario.ZoneBaselines, zoneId, IdSewage);
            double baselineAgri = SumValue(scenario.ZoneBaselines, zoneId, IdAgri);
            double additionSewage = SumValue(scenario.AnnualAdditions, zoneId, IdSewage);
            double additionAgri = SumValue(scenario.AnnualAdditions, zoneId, IdAgri);
            double baselineEColi = baselineSewage + baselineAgri;
            double additionEColi = additionSewage + additionAgri;

            double baselinePhos = SumValue(scenario.ZoneBaselines, zoneId, IdPhosphate);
            double additionPhos = SumValue(scenario.AnnualAdditions, zoneId, IdPhosphate);

            double baselineSed = SumValue(scenario.ZoneBaselines, zoneId, IdSediment);
            double additionSed = SumValue(scenario.AnnualAdditions, zoneId, IdSediment);

            if (additionEColi <= 0.0 && additionPhos <= 0.0 && additionSed <= 0.0) return 0f;

            int idxSewage = entityManager.GetEntityIndex(IdSewage);
            int idxAgri = entityManager.GetEntityIndex(IdAgri);
            int idxPhos = entityManager.GetEntityIndex(IdPhosphate);
            int idxSed = entityManager.GetEntityIndex(IdSediment);

            double sumEColi = 0.0;
            double sumPhos = 0.0;
            double sumSed = 0.0;
            int regionCount = 0;

            foreach (int regionId in regionManager.AllRegionIds)
            {
                if (regionManager.IsAliased(regionId)) continue;
                if (regionManager.GetRegionType(regionId) != regionType) continue;
                if (!regionManager.TryGetCenterCell(regionId, out var c)) continue;
                regionCount++;
                if (idxSewage >= 0) sumEColi += System.Math.Max(0L, entityManager.RawGetPopulation(c.col, c.row, idxSewage));
                if (idxAgri >= 0) sumEColi += System.Math.Max(0L, entityManager.RawGetPopulation(c.col, c.row, idxAgri));
                if (idxPhos >= 0) sumPhos += System.Math.Max(0L, entityManager.RawGetPopulation(c.col, c.row, idxPhos));
                if (idxSed >= 0) sumSed += System.Math.Max(0L, entityManager.RawGetPopulation(c.col, c.row, idxSed));
            }

            if (regionCount == 0) return 0f;

            double avgEColi = sumEColi / regionCount;
            double avgPhos = sumPhos / regionCount;
            double avgSed = sumSed / regionCount;

            float normEColi = additionEColi > 0.0 ? (float)Clamp01((avgEColi - baselineEColi) / additionEColi) : 0f;
            float normPhos = additionPhos > 0.0 ? (float)Clamp01((avgPhos - baselinePhos) / additionPhos) : 0f;
            float normSed = additionSed > 0.0 ? (float)Clamp01((avgSed - baselineSed) / additionSed) : 0f;

            // Weighted average of the per-pollutant norms. eColi is dampened so its
            // perpetual saturation (huge raw values, easy to reach norm=1) doesn't drown
            // out the more meaningful phos/sed variation across rounds. Mirrors across
            // all water zones — each zone normalises against its own baseline+addition.
            return (normEColi * WeightEColi + normPhos * WeightPhosphate + normSed * WeightSediment) / TotalWeight;
        }

        private static double SumValue(System.Collections.Generic.IEnumerable<ZoneBaseline> table, int zoneId, string entityId)
        {
            if (table == null) return 0.0;
            double total = 0.0;
            foreach (ZoneBaseline z in table)
            {
                if (z == null) continue;
                if (z.ZoneID == zoneId && z.EntityID == entityId) total += z.Value;
            }
            return total;
        }

        private static double SumValue(System.Collections.Generic.IEnumerable<EntityRate> table, int zoneId, string entityId)
        {
            if (table == null) return 0.0;
            double total = 0.0;
            foreach (EntityRate r in table)
            {
                if (r == null) continue;
                if (r.ZoneID == zoneId && r.EntityID == entityId) total += r.Value;
            }
            return total;
        }

        private static double Clamp01(double v)
        {
            if (v <= 0.0) return 0.0;
            if (v >= 1.0) return 1.0;
            return v;
        }
    }
}
