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
    //   - Lerp the zone's authored default water colours toward the dirty palette
    //     (DirtyShallowTint / DirtyDeepTint / DirtyCausticTint) by the combined amount.
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

        // Peak-pollution palette — these ARE the colours the water reaches at full lerp.
        // Authored as direct hex targets per component (shallow, deep, caustic) so the
        // dirty water has a consistent, designable end-state rather than emerging from
        // a single mud constant lerped into blue defaults.
        //   Shallow #8E5E23 — warm muddy ochre at the surface
        //   Deep    #503515 — darker rust where the seabed reads through
        //   Caustic #B76C25 — bright rust sparkle (alpha preserved from default caustic)
        private static readonly Color DirtyShallowTint = new Color(0.557f, 0.369f, 0.137f, 1f);
        private static readonly Color DirtyDeepTint = new Color(0.314f, 0.208f, 0.082f, 1f);
        private static readonly Color DirtyCausticTint = new Color(0.718f, 0.424f, 0.145f, 1f);

        // Caustic sparkle moves toward the given target's RGB but keeps its asset-authored
        // alpha, so the sparkle stays translucent — only the colour warms with pollution.
        private static Color CausticTarget(Color defaultCaustic, Color brown) =>
            new Color(brown.r, brown.g, brown.b, defaultCaustic.a);

        // Per-zone caps on the visible water-body tint amount. Sea and freshwater both
        // commit fully to the dirty palette at peak so the authored hex colours land
        // exactly. Overflow stays slightly under 1 because its baseline tint already
        // sits in the mud range — capping below 1 gives an asymptotic-feeling sweep.
        private const float MaxFreshwaterTint = 1.0f;
        private const float MaxSeawaterTint = 1.0f;
        private const float MaxOverflowTint = 0.85f;

        // Caustic and water-body share caps now that both target the same dirty palette.
        private const float MaxFreshwaterCausticTint = 1.0f;
        private const float MaxSeawaterCausticTint = 1.0f;

        // Curve exponent for both freshwater and seawater pollution lerps. Below 1 pushes
        // harder at low pollution so the water clearly tints early instead of dwelling in
        // the awkward blue→brown midpoint, which in RGB passes through a desaturated
        // violet-grey when the channels cross. 0.35 jumps past that zone fast.
        private const float TintCurveExponent = 0.35f;

        // Pollutant entity IDs the driver looks up. Missing entities resolve to "no contribution"
        // so non-pollutant scenarios (no e_coli/etc defined) behave as no-ops.
        private const string IdSewage = "eColi_sewage";
        private const string IdAgri = "eColi_agri";
        private const string IdPhosphate = "phosphate";
        private const string IdSediment = "sediment";
        // Marker entity for the Water Treatment Facility. When any population of this entity
        // exists in the simulation, the brown sewage-overflow tint is suppressed visually so
        // the overflow patches blend back into clean seawater. SSOT for "treatment installed"
        // is the entity population itself — no separate flag.
        private const string IdTreatment = "WaterTreatmentFacility";

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

            // Curve exponent (<1) amplifies low-pollutant response so the water clearly
            // tints early. With a linear norm the lerp dwells in the awkward blue→orange
            // midpoint where channels cross and the result reads as a desaturated violet-
            // grey. pow(x, 0.35) jumps past that zone fast. Norms are reused for caustic
            // so caps scale the same underlying signal.
            float freshNorm = ComputeZoneNorm(scenario, entityManager, regionManager, RegionType.Freshwater, ZoneFreshwater);
            float freshCurve = Mathf.Pow(freshNorm, TintCurveExponent);
            float freshT = freshCurve * MaxFreshwaterTint;
            float freshCausticT = freshCurve * MaxFreshwaterCausticTint;
            float seaNorm = ComputeZoneNorm(scenario, entityManager, regionManager, RegionType.Seawater, ZoneSeawater);
            float seaCurve = Mathf.Pow(seaNorm, TintCurveExponent);
            float seaT = seaCurve * MaxSeawaterTint;
            float seaCausticT = seaCurve * MaxSeawaterCausticTint;
            float overflowT = ComputeZoneNorm(scenario, entityManager, regionManager, RegionType.Overflow, ZoneOverflow) * MaxOverflowTint;

            // Freshwater: lerp each surface property to its dirty-palette target.
            // BaseTint is held at its asset default (multiplicative; lerping it would
            // double-tint with shallow/deep). Caustic preserves its asset alpha so the
            // sparkle stays translucent — only the RGB warms.
            Color freshCausticDefault = tintController.FreshwaterDefaultCaustic;
            Color freshCausticEnd = CausticTarget(freshCausticDefault, DirtyCausticTint);
            Color freshShallow = Color.Lerp(tintController.FreshwaterDefaultShallow, DirtyShallowTint, freshT);
            Color freshDeep = Color.Lerp(tintController.FreshwaterDefaultDeep, DirtyDeepTint, freshT);
            Color freshBase = tintController.FreshwaterDefaultBaseTint;
            Color freshCaustic = Color.Lerp(freshCausticDefault, freshCausticEnd, freshCausticT);
            tintController.SetFreshwaterTint(freshShallow, freshDeep, freshBase, freshCaustic);

            // Seawater: same palette as freshwater. Same caps; the only meaningful
            // difference at peak is that the two water bodies started from different
            // clean colours and converge here.
            Color seaCausticDefault = tintController.SeawaterDefaultCaustic;
            Color seaCausticEnd = CausticTarget(seaCausticDefault, DirtyCausticTint);
            Color seaShallow = Color.Lerp(tintController.SeawaterDefaultShallow, DirtyShallowTint, seaT);
            Color seaDeep = Color.Lerp(tintController.SeawaterDefaultDeep, DirtyDeepTint, seaT);
            Color seaBase = tintController.SeawaterDefaultBaseTint;
            Color seaCaustic = Color.Lerp(seaCausticDefault, seaCausticEnd, seaCausticT);
            tintController.SetSeawaterTint(seaShallow, seaDeep, seaBase, seaCaustic);

            // Overflow uses a single tint colour + strength uniform rather than shallow/deep.
            // Strength stays at its authored default when overflowT == 0, scaling up toward 1
            // as overflow regions accumulate pollutants. Colour blends toward DirtyDeepTint
            // so overflow patches read as the darkest mud — the rawest sewage shade in the
            // palette. Once the Water Treatment Facility is online anywhere on the map, the
            // brown sweep is suppressed visually — strength clamps to 0 so overflow patches
            // blend back into clean seawater via the existing 2 s WaterTintController lerp.
            Color overflowColor = Color.Lerp(tintController.OverflowDefaultTint, DirtyDeepTint, overflowT);
            float overflowStrength = Mathf.Lerp(tintController.OverflowDefaultStrength, 1f, overflowT);
            if (TreatmentActive(entityManager))
            {
                overflowColor = tintController.SeawaterDefaultShallow;
                overflowStrength = 0f;
            }
            tintController.SetOverflowTint(overflowColor, overflowStrength);
        }

        // True when the WaterTreatmentFacility entity holds any population anywhere in the
        // simulation. Mirrors the "alreadyBuilt" check WaterGameActionPanel uses so the
        // visual treatment-online state and the gameplay one-shot purchase gate can never
        // disagree — both read the same SSOT (the entity population).
        private static bool TreatmentActive(EntityManager entityManager)
        {
            if (entityManager == null) return false;
            int idx = entityManager.GetEntityIndex(IdTreatment);
            if (idx < 0) return false;
            return entityManager.GetTotalPopulationOfEntityType(idx) > 0;
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
