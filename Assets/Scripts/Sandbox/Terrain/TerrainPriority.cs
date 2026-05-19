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

        // Sorting/Z helpers for renderables that need to slot above a named terrain
        // but below whatever terrain sits in the next layer. Mirror the constants used
        // by DualGridTerrainRenderer so the two stay aligned to one source of truth.
        public const int TerrainSortingOrderBase = 10;
        public const float LayerZStep = 0.01f;
        public const float WithinLayerZStep = 0.005f;

        // SortingOrder for sprites that should sit above every terrain layer.
        // Derived from Layers.Length so it stays correct if the layer scheme grows.
        public static readonly int AboveAllTerrainSortingOrder = TerrainSortingOrderBase + Layers.Length;

        // Returns the sortingOrder used by the visual tilemap of the given terrain.
        // Pixel-art entities that should render between two terrain layers use the
        // sortingOrder of the LOWER layer and rely on z within that order to draw above
        // the terrain's own tilemap; the next layer's higher sortingOrder still wins.
        public static int SortingOrderOf(string terrain)
        {
            int layer = LayerOf(terrain);
            return layer < 0 ? TerrainSortingOrderBase : TerrainSortingOrderBase + layer;
        }

        // Returns a z value placing a renderable IMMEDIATELY above the given terrain's
        // visual tilemap within the same sortingOrder. More-negative z draws later in
        // Unity's transparency sort, so we subtract half a within-layer step from the
        // terrain's own z to land between it and the next-higher within-layer entry.
        public static float ZAbove(string terrain)
        {
            int layer = LayerOf(terrain);
            int withinRank = WithinLayerRank(terrain);
            if (layer < 0 || withinRank < 0) return 0f;
            int layerLength = Layers[layer].Length;
            float terrainZ = -layer * LayerZStep - (layerLength - 1 - withinRank) * WithinLayerZStep;
            return terrainZ - WithinLayerZStep * 0.5f;
        }
    }
}
