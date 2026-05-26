using Glitchers.EcoKnow.Sandbox.Grid.Regions;
using Glitchers.EcoKnow.Sandbox.Terrain;
using UnityEngine;

namespace Glitchers.EcoKnow.Sandbox.UI
{
    // Placeholder right-side action panel for the water gameplay loop. Renders via IMGUI
    // (OnGUI) so it ships with zero scene wiring and zero prefab YAML — the brief explicitly
    // calls for "simple placeholder buttons for every action we can take" and notes visuals
    // will be revisited later. Auto-bootstraps via [RuntimeInitializeOnLoadMethod] for the
    // same reason PixelArtTestSpawner does: avoids per-scene wiring while we iterate.
    //
    // Actions:
    //   • Fish         — harvests up to oyster.HarvestLimit oysters from the first seawater
    //                    region whose compute cell holds oyster > 0. Routes through the
    //                    existing EntityManager.TryHarvestEntityFromCell path so HarvestQuantities
    //                    deposit oyster items into the inventory.
    //   • Pick Litter  — removes Scenario.PickLitterReductionPerAction units of litter from
    //                    every Seawater/Freshwater/Beach region's compute cell. One-shot per
    //                    turn (combo flag).
    //   • Fundraise    — credits Scenario.FundraisingIncome currency. One-shot per turn
    //                    (combo flag).
    //   • Buy WTF      — once per game, gated to CurrentRound >= Scenario.TreatmentMinRound
    //                    and currency >= Scenario.TreatmentCost. Writes WaterTreatmentFacility
    //                    population = 1 to every water-region compute cell (seawater,
    //                    freshwater, overflow) so the shared matrix's treatment-row
    //                    coefficients drive pollutant decay in all bodies of water each
    //                    subsequent round. Also clears the overflow brown tint so the
    //                    sewage-overflow patches blend back into clean seawater blue.
    //
    // Selling oysters reuses the existing InventoryModal path (left untouched).
    public class WaterGameActionPanel : MonoBehaviour
    {
        private const string LogChannel = "[WaterGameActionPanel]";

        private const string OysterEntityId = "oyster";
        private const string LitterEntityId = "litter";
        private const string TreatmentEntityId = "WaterTreatmentFacility";
        private const string EColiSewageEntityId = "eColi_sewage";

        // (Pick Litter intentionally does NOT touch phosphate any more. Per design feedback:
        // litter is litter, water quality is a separate dimension only meaningfully moved by
        // the Water Treatment Facility. Picking still earns its keep via the combo bonus
        // and the bottle-count visual on the beach.)

        // Internal inventory key used to track "got sick from fishing in polluted water"
        // events. Lives in the PlayerInventory dictionary alongside currency / AP but is
        // intentionally NOT registered as an Item def, so it stays out of the sell modal and
        // inventory panel. Read by the LoseCondition system via TargetItemID.
        public const string SicknessInventoryId = "sickness";

        // Per-cell eColi_sewage level above which fishing is considered "fishing in bad water"
        // and triggers the sickness penalty. Tuned against Largo's ramping seawater addition
        // schedule: R0/R1/R2 cell pop stays below ~4e13 (safe), R3+ adds push it past 5e13
        // (dirty) and the player must stop fishing or accumulate sickness toward the lose
        // threshold. Treatment in seawater drains the cell back below threshold within one
        // LV cycle, re-opening late-game safe fishing once the facility is online.
        private const long ECOLI_SICKNESS_THRESHOLD = 50_000_000_000_000L;

        // Layout constants. RightMargin keeps the panel from sitting flush against the
        // screen edge. Padding spaces out each action's button + subtitle pair so the italic
        // descriptive text has room to breathe between buttons.
        private const float PanelWidth = 220f;
        private const float ButtonHeight = 48f;
        private const float StatusHeight = 20f;
        private const float Padding = 24f;
        private const float RightMargin = 55f;
        private const float TopMargin = 140f;

        private GUIStyle _buttonStyle;
        private GUIStyle _statusStyle;
        private GUIStyle _statusOutlineStyle;
        private bool _stylesReady;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            // Mirror PixelArtTestSpawner: skip when something else has already spawned an
            // instance (e.g. a hand-placed scene component, scene reload).
            if (FindFirstObjectByType<WaterGameActionPanel>() != null) return;
            GameObject go = new GameObject(nameof(WaterGameActionPanel));
            go.AddComponent<WaterGameActionPanel>();
            DontDestroyOnLoad(go);
        }

        private void OnGUI()
        {
            if (!SandboxManager.Exists) return;
            SandboxManager mgr = SandboxManager.Instance;
            if (mgr == null) return;
            Scenario scenario = mgr.CurrentScenario;
            if (scenario == null) return;

            // Hide the panel for scenarios that don't declare the water-game entities. Keeps
            // the placeholder from polluting other (non-water) scenarios while everything is
            // still data-driven from a single scenario JSON.
            EntityManager entities = mgr.EntityManager;
            if (entities == null) return;
            int oysterIdx = entities.GetEntityIndex(OysterEntityId);
            int litterIdx = entities.GetEntityIndex(LitterEntityId);
            int treatmentIdx = entities.GetEntityIndex(TreatmentEntityId);
            if (oysterIdx < 0 && litterIdx < 0 && treatmentIdx < 0) return;

            EnsureStyles();

            float x = Screen.width - PanelWidth - RightMargin;
            float y = TopMargin;

            // Compute the readouts once — used by both the in-panel header and the
            // prominent top-of-screen badge.
            int sickness = mgr.PlayerInventory != null
                ? mgr.PlayerInventory.GetAmountHeld(SicknessInventoryId)
                : 0;
            int eColiIdx = mgr.EntityManager.GetEntityIndex(EColiSewageEntityId);
            bool waterContaminatedForFishing = AnySeawaterRegionHasPopulationAbove(mgr, eColiIdx, ECOLI_SICKNESS_THRESHOLD);
            string qualityLabel = ClassifyWaterQuality(mgr);
            Color qualityColor = PollutionTier.Colour(qualityLabel);

            // In-panel header — water-pollution line (coloured) followed by the sickness +
            // fishing status line (plain white outlined). Split into two labels so just the
            // tier text picks up the green/amber/red tint while the rest stays readable.
            float waterLineY = y;
            DrawColoredOutlinedLabel(
                new Rect(x, waterLineY, PanelWidth, StatusHeight),
                $"Water Pollution: {qualityLabel}",
                _statusStyle, _statusOutlineStyle, qualityColor);
            float sicknessLineY = waterLineY + StatusHeight + 2f;
            DrawOutlinedLabel(
                new Rect(x, sicknessLineY, PanelWidth, StatusHeight),
                $"Sickness: {sickness} / 3    Fishing: {(waterContaminatedForFishing ? "RISKY" : "safe")}",
                _statusStyle, _statusOutlineStyle);
            y = sicknessLineY + StatusHeight + Padding;

            // Fish
            bool fishCan = SandboxManager.CanPerformAction() && AnySeawaterRegionHasEntity(mgr, oysterIdx);
            string fishSubtitle = waterContaminatedForFishing
                ? "Catch halved + sickness risk in contaminated water"
                : "Harvest oysters from the bay";
            DrawAction(ref y, x, "Fish (1 AP)", fishSubtitle, fishCan, () => DoFish(mgr, oysterIdx, eColiIdx));

            // Pick Litter — always available (once per turn). The action removes some litter
            // where there is any AND reduces phosphate per region, so tidying the beach is a
            // meaningful sustaining action even when bay litter has already been cleared.
            bool litterAlreadyPicked = mgr.HasRoundAction(SandboxManager.ActionFlagLitter);
            bool litterCan = SandboxManager.CanPerformAction() && !litterAlreadyPicked;
            string litterSubtitle = litterAlreadyPicked
                ? "Already picked this turn"
                : "Tidies shore litter";
            DrawAction(ref y, x, "Pick Litter (1 AP)", litterSubtitle, litterCan,
                () => DoPickLitter(mgr, litterIdx, scenario.PickLitterReductionPerAction));

            // Fundraise
            bool fundraiseAlreadyDone = mgr.HasRoundAction(SandboxManager.ActionFlagFundraise);
            bool fundraiseCan = SandboxManager.CanPerformAction() && !fundraiseAlreadyDone;
            string fundraiseSubtitle = fundraiseAlreadyDone
                ? "Already fundraised this turn"
                : $"+{scenario.FundraisingIncome} currency";
            DrawAction(ref y, x, "Fundraise (1 AP)", fundraiseSubtitle, fundraiseCan,
                () => DoFundraise(mgr, scenario.FundraisingIncome));

            // Buy Water Treatment Facility
            bool alreadyBuilt = treatmentIdx >= 0 && entities.GetTotalPopulationOfEntityType(treatmentIdx) > 0;
            bool roundUnlocked = mgr.CurrentRound >= scenario.TreatmentMinRound;
            int currency = mgr.PlayerInventory != null
                ? mgr.PlayerInventory.GetAmountHeld(PlayerInventory.CurrencyID)
                : 0;
            bool canAfford = currency >= scenario.TreatmentCost;
            bool treatmentCan = SandboxManager.CanPerformAction()
                && !alreadyBuilt
                && roundUnlocked
                && canAfford
                && treatmentIdx >= 0;
            string treatmentSubtitle;
            if (alreadyBuilt)
            {
                treatmentSubtitle = "Treatment online";
            }
            else if (!roundUnlocked)
            {
                // Scenario stores 0-indexed round; show 1-indexed for the player.
                treatmentSubtitle = $"Cost: {scenario.TreatmentCost} (unlocks round {scenario.TreatmentMinRound + 1})";
            }
            else
            {
                treatmentSubtitle = $"Cost: {scenario.TreatmentCost}";
            }
            DrawAction(ref y, x, "Buy Water Treatment Facility (1 AP)", treatmentSubtitle, treatmentCan,
                () => DoBuyTreatment(mgr, treatmentIdx, scenario.TreatmentCost));
        }

        private void DrawAction(ref float y, float x, string title, string subtitle, bool enabled, System.Action onPressed)
        {
            bool prevEnabled = GUI.enabled;
            GUI.enabled = enabled;
            if (GUI.Button(new Rect(x, y, PanelWidth, ButtonHeight), title, _buttonStyle))
            {
                onPressed?.Invoke();
            }
            GUI.enabled = prevEnabled;
            y += ButtonHeight;
            DrawOutlinedLabel(new Rect(x, y, PanelWidth, StatusHeight), subtitle, _statusStyle, _statusOutlineStyle);
            y += StatusHeight + Padding;
        }

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true
            };
            // Bright white button text — the default skin's background is mid-grey, so a
            // pure-white label reads cleanly against it.
            _buttonStyle.normal.textColor = Color.white;
            _buttonStyle.hover.textColor = Color.white;
            _buttonStyle.active.textColor = Color.white;
            _buttonStyle.focused.textColor = Color.white;

            _statusStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleRight,
                fontStyle = FontStyle.Italic,
                wordWrap = true
            };
            _statusStyle.normal.textColor = Color.white;

            // Pair style used by DrawOutlinedLabel — same metrics as _statusStyle but black,
            // drawn 1px offset in four directions to outline the white fill on top.
            _statusOutlineStyle = new GUIStyle(_statusStyle);
            _statusOutlineStyle.normal.textColor = Color.black;

            _stylesReady = true;
        }

        // IMGUI doesn't ship a text-outline option; the 4-direction blit is the standard hack
        // — five label draws per call but cheap at this surface area, and produces a crisp
        // 1px black halo so labels stay readable over arbitrary scene colours.
        private static void DrawOutlinedLabel(Rect rect, string text, GUIStyle fill, GUIStyle outline)
        {
            GUI.Label(new Rect(rect.x - 1f, rect.y, rect.width, rect.height), text, outline);
            GUI.Label(new Rect(rect.x + 1f, rect.y, rect.width, rect.height), text, outline);
            GUI.Label(new Rect(rect.x, rect.y - 1f, rect.width, rect.height), text, outline);
            GUI.Label(new Rect(rect.x, rect.y + 1f, rect.width, rect.height), text, outline);
            GUI.Label(rect, text, fill);
        }

        // Same outline trick but tints the fill via GUI.contentColor. Outline stays black
        // because the outline style's textColor is black and we hold contentColor at white
        // during the four outline draws — the multiplicative tint only takes effect for the
        // final fill draw.
        private static void DrawColoredOutlinedLabel(Rect rect, string text, GUIStyle fill, GUIStyle outline, Color fillColor)
        {
            Color prev = GUI.contentColor;
            GUI.contentColor = Color.white;
            GUI.Label(new Rect(rect.x - 1f, rect.y, rect.width, rect.height), text, outline);
            GUI.Label(new Rect(rect.x + 1f, rect.y, rect.width, rect.height), text, outline);
            GUI.Label(new Rect(rect.x, rect.y - 1f, rect.width, rect.height), text, outline);
            GUI.Label(new Rect(rect.x, rect.y + 1f, rect.width, rect.height), text, outline);
            GUI.contentColor = fillColor;
            GUI.Label(rect, text, fill);
            GUI.contentColor = prev;
        }


        // ─── Action handlers ────────────────────────────────────────────────

        private static void DoFish(SandboxManager mgr, int oysterIdx, int eColiIdx)
        {
            if (oysterIdx < 0) return;
            if (!SandboxManager.CanPerformAction()) return;

            Entity oyster = mgr.EntityManager.GetEntityType(oysterIdx);
            int baseAmount = (oyster != null && oyster.HarvestLimit > 0) ? oyster.HarvestLimit : 1;

            // First seawater compute cell with oyster > 0. Cycling round-robin would feel
            // fairer but a deterministic-first pass keeps the placeholder predictable; live
            // population pulls down the same cell each turn until it depletes, then the next
            // region takes over naturally.
            RegionComputeManager regions = mgr.RegionComputeManager;
            if (regions == null)
            {
                Debug.LogWarning($"{LogChannel} Fish failed: scenario is not in region-compute mode.");
                return;
            }

            foreach (int regionId in regions.AllRegionIds)
            {
                if (regions.GetRegionType(regionId) != RegionType.Seawater) continue;
                if (regions.IsAliased(regionId)) continue;
                if (!regions.TryGetCenterCell(regionId, out var c)) continue;
                long pop = mgr.EntityManager.RawGetPopulation(c.col, c.row, oysterIdx);
                if (pop <= 0L) continue;

                // Per-cell sickness check — uses the eColi_sewage level in the SAME compute
                // cell we're about to fish (not a grid-wide aggregate) so the player can
                // localise their fishing to cleaner regions if some bays are dirtier than
                // others. Treatment investment lowers eColi over time, restoring safe fishing.
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

        private static void DoPickLitter(SandboxManager mgr, int litterIdx, int litterReductionPerRegion)
        {
            if (!SandboxManager.CanPerformAction()) return;
            if (mgr.HasRoundAction(SandboxManager.ActionFlagLitter)) return;

            RegionComputeManager regions = mgr.RegionComputeManager;
            if (regions == null)
            {
                Debug.LogWarning($"{LogChannel} Pick Litter failed: scenario is not in region-compute mode.");
                return;
            }

            // Trim litter from any shore/bay region's compute cell that still has any. No
            // water-quality side-effect on purpose: that lever lives with the Water Treatment
            // Facility. Picking is still worth the AP for the Litter+Fundraise combo bonus
            // and for the bottle-count beach visual.
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
                    }
                }
            }

            mgr.MarkRoundAction(SandboxManager.ActionFlagLitter);
            SandboxManager.SpendActionPoint();
            SandboxManager.OnActionCompleted();
        }

        private static void DoFundraise(SandboxManager mgr, int income)
        {
            if (!SandboxManager.CanPerformAction()) return;
            if (mgr.HasRoundAction(SandboxManager.ActionFlagFundraise)) return;
            if (mgr.PlayerInventory == null) return;

            mgr.PlayerInventory.AddItem(PlayerInventory.CurrencyID, income);
            mgr.MarkRoundAction(SandboxManager.ActionFlagFundraise);
            SandboxManager.SpendActionPoint();
            SandboxManager.OnActionCompleted();
        }

        private static void DoBuyTreatment(SandboxManager mgr, int treatmentIdx, int cost)
        {
            if (treatmentIdx < 0 || mgr.PlayerInventory == null) return;
            if (!SandboxManager.CanPerformAction()) return;
            if (mgr.PlayerInventory.GetAmountHeld(PlayerInventory.CurrencyID) < cost) return;
            if (mgr.EntityManager.GetTotalPopulationOfEntityType(treatmentIdx) > 0) return;

            RegionComputeManager regions = mgr.RegionComputeManager;
            if (regions == null)
            {
                Debug.LogWarning($"{LogChannel} Buy Treatment failed: scenario is not in region-compute mode.");
                return;
            }

            mgr.PlayerInventory.RemoveItem(PlayerInventory.CurrencyID, cost);

            // Seed the invisible facility in every water region's compute cell (seawater,
            // freshwater, overflow) so the matrix's treatment row drains pollutants in all
            // bodies of water — not just the bay. Estuary aliases are skipped because they
            // share a compute cell with their parent freshwater region. One unit per region;
            // the matrix term scales with population so larger water bodies could in
            // principle hold a bigger facility footprint, but for v1 the cost / decay
            // coefficients are tuned for unit population.
            int installed = 0;
            foreach (int regionId in regions.AllRegionIds)
            {
                RegionType rt = regions.GetRegionType(regionId);
                if (rt != RegionType.Seawater && rt != RegionType.Freshwater && rt != RegionType.Overflow) continue;
                if (regions.IsAliased(regionId)) continue;
                if (!regions.TryGetCenterCell(regionId, out var c)) continue;
                mgr.EntityManager.RawSetPopulation(c.col, c.row, treatmentIdx, 1L);
                installed++;
            }

            // Visually heal the sewage-overflow patches: drive the shader's overflow tint
            // strength to zero so the brown blend dissolves back into clean seawater blue.
            // SetOverflowTint already animates over 2 s via WaterTintController's lerp, so
            // the transition is smooth. We target the seawater default shallow colour as
            // the lerp's colour endpoint for symmetry — strength=0 makes the colour value
            // visually irrelevant once the lerp completes, but supplying a clean endpoint
            // avoids any odd mid-lerp tint.
            WaterTintController tint = mgr.WaterTintController;
            if (tint != null)
            {
                tint.SetOverflowTint(tint.SeawaterDefaultShallow, 0f);
            }

            Debug.Log($"{LogChannel} Water Treatment Facility installed in {installed} water region(s) for {cost} currency; overflow tint cleared.");

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

        // True if any non-aliased seawater compute cell holds more of the given entity than
        // `threshold`. Drives the panel's "Water: contaminated" indicator using the same
        // per-cell comparison DoFish uses to apply the sickness penalty, so the warning and
        // the actual penalty are guaranteed to be consistent.
        private static bool AnySeawaterRegionHasPopulationAbove(SandboxManager mgr, int entityIdx, long threshold)
        {
            if (entityIdx < 0) return false;
            RegionComputeManager regions = mgr.RegionComputeManager;
            if (regions == null) return false;
            foreach (int regionId in regions.AllRegionIds)
            {
                if (regions.GetRegionType(regionId) != RegionType.Seawater) continue;
                if (regions.IsAliased(regionId)) continue;
                if (!regions.TryGetCenterCell(regionId, out var c)) continue;
                if (mgr.EntityManager.RawGetPopulation(c.col, c.row, entityIdx) > threshold) return true;
            }
            return false;
        }

        // Catchment-wide water-pollution tier — delegates to the same PollutionTier helper
        // the EntityWidget + ObjectiveWidget call so the panel header, the aggregator widget
        // on the right, and the aggregate win condition can never disagree.
        private static string ClassifyWaterQuality(SandboxManager mgr)
        {
            return PollutionTier.ComputeAggregateTier(mgr.EntityManager);
        }

        private static bool AnyShoreRegionHasEntity(SandboxManager mgr, int entityIdx)
        {
            if (entityIdx < 0) return false;
            RegionComputeManager regions = mgr.RegionComputeManager;
            if (regions == null) return false;
            foreach (int regionId in regions.AllRegionIds)
            {
                RegionType t = regions.GetRegionType(regionId);
                bool applies = t == RegionType.Seawater || t == RegionType.Freshwater || t == RegionType.Beach;
                if (!applies) continue;
                if (regions.IsAliased(regionId)) continue;
                if (!regions.TryGetCenterCell(regionId, out var c)) continue;
                if (mgr.EntityManager.RawGetPopulation(c.col, c.row, entityIdx) > 0L) return true;
            }
            return false;
        }
    }
}
