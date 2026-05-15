using System.Collections.Generic;

namespace Glitchers.EcoKnow.Sandbox.Grid.Regions
{
    // Decides whether a given entity is allowed to move between two region types.
    // Empty by default: nothing moves. Callers (e.g. scenario setup, future entity definitions)
    // populate the table by calling Allow / AllowBidirectional during scenario load.
    public static class RegionMovementPolicy
    {
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
