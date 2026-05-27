using Glitchers.EcoKnow.Sandbox.Grid.Regions;
using UnityEngine;

namespace Glitchers.EcoKnow.Sandbox
{
    // Static service that owns the direct-action behaviour for the water gameplay loop.
    // Originally lived inside the placeholder IMGUI WaterGameActionPanel; extracted here so
    // canvas UI (ToolPanel via EntityActionRegistry) can invoke the same logic without
    // dragging an OnGUI MonoBehaviour into the scene. Single source of truth for:
    //   • SicknessInventoryId  — referenced by SandboxUI's sickness HUD + the LoseCondition
    //                            system. Preserved as the same string value ("sickness") so
    //                            PlayerInventory state survives the move.
    //   • DoFish / DoPickLitter — the AP-spending action handlers.
    //   • CanFish  / CanPickLitter — availability predicates used to grey out the buttons.
    public static class WaterGameActions
    {
        public const string OysterEntityId = "oyster";
        public const string LitterEntityId = "litter";
        public const string EColiSewageEntityId = "eColi_sewage";

        // Mirrors the entity ID so the inventory counter reuses the entity's pixel-art icon.
        // Tracked as a non-sellable counter on PlayerInventory.
        public const string LitterInventoryId = "litter";

        // Lives in PlayerInventory alongside currency / AP but is intentionally NOT registered
        // as an Item def, so it stays out of the sell modal and inventory panel. Read by the
        // LoseCondition system via TargetItemID and surfaced by the SicknessIndicator widget.
        public const string SicknessInventoryId = "sickness";

        // Per-cell eColi_sewage level above which fishing is "fishing in bad water" and the
        // catch is halved + sickness accumulates. Tuned against Largo's seawater addition
        // schedule so early rounds stay safe and late-game forces treatment investment.
        private const long ECOLI_SICKNESS_THRESHOLD = 50_000_000_000_000L;

        private const string LogChannel = "[WaterGameActions]";

        // ─── Availability predicates ────────────────────────────────────────

        public static bool CanFish()
        {
            if (!SandboxManager.Exists) return false;
            if (!SandboxManager.CanPerformAction()) return false;
            SandboxManager mgr = SandboxManager.Instance;
            if (mgr == null || mgr.EntityManager == null) return false;
            int oysterIdx = mgr.EntityManager.GetEntityIndex(OysterEntityId);
            return AnySeawaterRegionHasEntity(mgr, oysterIdx);
        }

        public static bool CanPickLitter()
        {
            if (!SandboxManager.Exists) return false;
            if (!SandboxManager.CanPerformAction()) return false;
            SandboxManager mgr = SandboxManager.Instance;
            if (mgr == null) return false;
            return !mgr.HasRoundAction(SandboxManager.ActionFlagLitter);
        }

        // ─── Action handlers ────────────────────────────────────────────────

        public static void DoFish()
        {
            if (!SandboxManager.Exists) return;
            SandboxManager mgr = SandboxManager.Instance;
            if (mgr == null || mgr.EntityManager == null) return;
            int oysterIdx = mgr.EntityManager.GetEntityIndex(OysterEntityId);
            int eColiIdx = mgr.EntityManager.GetEntityIndex(EColiSewageEntityId);

            if (oysterIdx < 0) return;
            if (!SandboxManager.CanPerformAction()) return;

            Entity oyster = mgr.EntityManager.GetEntityType(oysterIdx);
            int baseAmount = (oyster != null && oyster.HarvestLimit > 0) ? oyster.HarvestLimit : 1;

            RegionComputeManager regions = mgr.RegionComputeManager;
            if (regions == null)
            {
                Debug.LogWarning($"{LogChannel} Fish failed: scenario is not in region-compute mode.");
                return;
            }

            // First seawater compute cell with oyster > 0. Cycling round-robin would feel
            // fairer but a deterministic-first pass keeps the action predictable.
            foreach (int regionId in regions.AllRegionIds)
            {
                if (regions.GetRegionType(regionId) != RegionType.Seawater) continue;
                if (regions.IsAliased(regionId)) continue;
                if (!regions.TryGetCenterCell(regionId, out var c)) continue;
                long pop = mgr.EntityManager.RawGetPopulation(c.col, c.row, oysterIdx);
                if (pop <= 0L) continue;

                bool waterContaminated = false;
                if (eColiIdx >= 0)
                {
                    long eColi = mgr.EntityManager.RawGetPopulation(c.col, c.row, eColiIdx);
                    waterContaminated = eColi > ECOLI_SICKNESS_THRESHOLD;
                }

                int amount = waterContaminated ? Mathf.Max(1, baseAmount / 2) : baseAmount;

                if (mgr.EntityManager.TryHarvestEntityFromCell(c.col, c.row, oysterIdx, amount))
                {
                    if (waterContaminated && mgr.PlayerInventory != null)
                    {
                        mgr.PlayerInventory.AddItem(SicknessInventoryId, 1);
                        Debug.Log($"{LogChannel} Fishing in contaminated water — yield halved and sickness +1.");
                    }
                    SandboxManager.SpendActionPoint();
                    SandboxManager.OnActionCompleted();
                    return;
                }
            }

            Debug.Log($"{LogChannel} Fish: no seawater compute cell currently holds harvestable oysters.");
        }

        public static void DoPickLitter()
        {
            if (!SandboxManager.Exists) return;
            SandboxManager mgr = SandboxManager.Instance;
            if (mgr == null || mgr.EntityManager == null) return;
            Scenario scenario = mgr.CurrentScenario;
            if (scenario == null) return;

            if (!SandboxManager.CanPerformAction()) return;
            if (mgr.HasRoundAction(SandboxManager.ActionFlagLitter)) return;

            int litterIdx = mgr.EntityManager.GetEntityIndex(LitterEntityId);
            int litterReductionPerRegion = scenario.PickLitterReductionPerAction;

            RegionComputeManager regions = mgr.RegionComputeManager;
            if (regions == null)
            {
                Debug.LogWarning($"{LogChannel} Pick Litter failed: scenario is not in region-compute mode.");
                return;
            }

            // Trim litter from any shore/bay region's compute cell that still has any. No
            // water-quality side-effect on purpose: that lever lives with the Water Treatment
            // Facility. Picking is still worth the AP for the Litter+Fundraise combo bonus
            // and for the bottle-count beach visual. Track the total removed so it can be
            // mirrored into the player's inventory as a non-sellable counter.
            long totalRemoved = 0L;
            if (litterIdx >= 0 && litterReductionPerRegion > 0)
            {
                foreach (int regionId in regions.AllRegionIds)
                {
                    if (regions.IsAliased(regionId)) continue;
                    if (!regions.TryGetCenterCell(regionId, out var c)) continue;
                    RegionType t = regions.GetRegionType(regionId);
                    bool litterApplies = t == RegionType.Seawater || t == RegionType.Freshwater || t == RegionType.Beach;
                    if (!litterApplies) continue;

                    long pop = mgr.EntityManager.RawGetPopulation(c.col, c.row, litterIdx);
                    if (pop > 0L)
                    {
                        long newPop = System.Math.Max(0L, pop - litterReductionPerRegion);
                        mgr.EntityManager.RawSetPopulation(c.col, c.row, litterIdx, newPop);
                        totalRemoved += pop - newPop;
                    }
                }
            }

            if (totalRemoved > 0L && mgr.PlayerInventory != null)
            {
                int delta = totalRemoved > int.MaxValue ? int.MaxValue : (int)totalRemoved;
                mgr.PlayerInventory.AddItem(LitterInventoryId, delta);
            }

            mgr.MarkRoundAction(SandboxManager.ActionFlagLitter);
            SandboxManager.SpendActionPoint();
            SandboxManager.OnActionCompleted();
        }

        // ─── Availability helpers ───────────────────────────────────────────

        private static bool AnySeawaterRegionHasEntity(SandboxManager mgr, int entityIdx)
        {
            if (entityIdx < 0) return false;
            RegionComputeManager regions = mgr.RegionComputeManager;
            if (regions == null) return false;
            foreach (int regionId in regions.AllRegionIds)
            {
                if (regions.GetRegionType(regionId) != RegionType.Seawater) continue;
                if (regions.IsAliased(regionId)) continue;
                if (!regions.TryGetCenterCell(regionId, out var c)) continue;
                if (mgr.EntityManager.RawGetPopulation(c.col, c.row, entityIdx) > 0L) return true;
            }
            return false;
        }
    }
}
