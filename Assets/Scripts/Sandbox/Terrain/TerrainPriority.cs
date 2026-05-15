using System.Collections.Generic;

namespace Glitchers.EcoKnow.Sandbox.Terrain
{
    /// <summary>
    /// Single source of truth for terrain layer ordering and per-zone terrain composition.
    /// Encodes the spec: <c>grass = sea &gt; sand &gt;= dirt</c>
    ///   • <see cref="Layers"/>[0] is the bottom layer; higher indices render on top.
    ///   • Within a layer, entry [0] renders on top of entry [1] (and so on).
    ///   • <see cref="ZoneTerrainStacks"/> maps each scenario zone ID to the terrain names present on cells of that zone.
    /// </summary>
    public static class TerrainPriority
    {
        public static readonly string[][] Layers =
        {
            new[] { TilesetConstants.Sand, TilesetConstants.Dirt },
            new[] { TilesetConstants.Crops, TilesetConstants.Grass, TilesetConstants.Freshwater, TilesetConstants.Sea }
        };

        public static readonly Dictionary<int, string[]> ZoneTerrainStacks = new Dictionary<int, string[]>
        {
            { 0, new[] { TilesetConstants.Sea, TilesetConstants.Sand } },
            { 1, new[] { TilesetConstants.Sand, TilesetConstants.Dirt } },
            { 2, new[] { TilesetConstants.Grass, TilesetConstants.Dirt } },
            { 3, new[] { TilesetConstants.Dirt } },
            { 4, new[] { TilesetConstants.Freshwater, TilesetConstants.Dirt } },
            { 5, new[] { TilesetConstants.Freshwater, TilesetConstants.Sand } },
            { 6, new[] { TilesetConstants.Freshwater, TilesetConstants.Sea, TilesetConstants.Sand } },
            { 7, new[] { TilesetConstants.Crops, TilesetConstants.Dirt } },
            // Overflow zones are seawater cells with a brown shader tint. Overflow
            // is intentionally absent from Layers — it is a marker for the
            // ElevationMap distance field, not a renderable layer. Sand is kept so
            // the bottom layer matches surrounding zone-0 cells.
            { 8, new[] { TilesetConstants.Sea, TilesetConstants.Overflow, TilesetConstants.Sand } }
        };

        public static int LayerOf(string terrain)
        {
            for (int layer = 0; layer < Layers.Length; layer++)
            {
                string[] entries = Layers[layer];
                for (int i = 0; i < entries.Length; i++)
                {
                    if (entries[i] == terrain)
                    {
                        return layer;
                    }
                }
            }
            return -1;
        }

        public static int WithinLayerRank(string terrain)
        {
            for (int layer = 0; layer < Layers.Length; layer++)
            {
                string[] entries = Layers[layer];
                for (int i = 0; i < entries.Length; i++)
                {
                    if (entries[i] == terrain)
                    {
                        return i;
                    }
                }
            }
            return -1;
        }

        public static IEnumerable<string> AllTerrainNames()
        {
            for (int layer = 0; layer < Layers.Length; layer++)
            {
                string[] entries = Layers[layer];
                for (int i = 0; i < entries.Length; i++)
                {
                    yield return entries[i];
                }
            }
        }
    }
}
