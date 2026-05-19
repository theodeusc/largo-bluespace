using System.Collections.Generic;
using UnityEngine;

namespace Glitchers.EcoKnow.Sandbox.Grid.Regions
{
    // Decides whether a given entity is allowed to move between two region types.
    // Empty by default: nothing moves. Callers (e.g. scenario setup, future entity definitions)
    // populate the table by calling Allow / AllowBidirectional during scenario load.
    public static class RegionMovementPolicy
    {
        // Entity IDs the scenario uses for water-borne pollutants. These match the IDs
        // emitted in Largo.json. If a scenario uses different IDs, register movement
        // manually; this helper is opt-in (called explicitly by SandboxManager).
        private static readonly string[] WaterPollutantIds = new[]
        {
            "eColi_sewage",
            "eColi_agri",
            "phosphate",
            "sediment",
            "litter",
        };

        // One-way pollutant dispersion: freshwater → seawater and overflow → seawater.
        // Estuary is invisible to movement because its cells alias to a freshwater compute
        // cell (see RegionComputeManager.BuildEstuaryAliases) — no separate Estuary entry needed.
        // Called from SandboxManager.SetupAndRunScenario after RegionComputeManager.Initialize.
        // Safe to call when the scenario lacks one of these IDs: missing entities are skipped.
        public static void RegisterWaterPollutants(EntityManager entityManager)
        {
            if (entityManager == null) return;
            for (int i = 0; i < WaterPollutantIds.Length; i++)
            {
                int entityId = entityManager.GetEntityIndex(WaterPollutantIds[i]);
                if (entityId < 0) continue;
                Allow(entityId, RegionType.Freshwater, RegionType.Seawater);
                Allow(entityId, RegionType.Overflow, RegionType.Seawater);
            }
        }

        private struct Key
        {
            public int EntityId;
            public RegionType From;
            public RegionType To;
        }

        private static readonly HashSet<Key> _allowed = new HashSet<Key>();

        public static bool CanMove(int entityId, RegionType from, RegionType to)
        {
            return _allowed.Contains(new Key { EntityId = entityId, From = from, To = to });
        }

        public static void Allow(int entityId, RegionType from, RegionType to)
        {
            _allowed.Add(new Key { EntityId = entityId, From = from, To = to });
        }

        public static void AllowBidirectional(int entityId, params RegionType[] types)
        {
            if (types == null) return;
            for (int i = 0; i < types.Length; i++)
            {
                for (int j = 0; j < types.Length; j++)
                {
                    if (i == j) continue;
                    Allow(entityId, types[i], types[j]);
                }
            }
        }

        public static void Clear()
        {
            _allowed.Clear();
        }
    }
}
