using System.Collections.Generic;

namespace Glitchers.EcoKnow.Sandbox.Terrain
{
    /// <summary>
    /// Single source of truth for tileset names, tileset→archetype mapping, and resource paths.
    /// All tileset-related constants should reference this class instead of duplicating strings.
    /// </summary>
    public static class TilesetConstants
    {
        public const string Dirt = "dirt";
        public const string Grass = "grass";
        public const string Sand = "sand";
        public const string Sea = "sea";
        public const string SeaVoid = "sea_void";

        public static readonly string[] All = { Dirt, Grass, Sand, Sea };

        public static readonly Dictionary<string, Archetype> ToArchetype = new Dictionary<string, Archetype>
        {
            { Dirt, Archetype.Soil },
            { Grass, Archetype.Vegetation },
            { Sand, Archetype.Soil },
            { Sea, Archetype.Liquid }
        };

        public static readonly Dictionary<string, string> TilesetPaths = new Dictionary<string, string>
        {
            { Dirt, "Tiles/Dirt/dirt_tileset" },
            { Grass, "Tiles/Grass/grass_tileset" },
            { Sand, "Tiles/Sand/sand_tileset" },
            { Sea, "Tiles/Sea/sea_tileset" },
            { SeaVoid, "Tiles/SeaVoid/sea_void" }
        };

        // Boundary variants are sprite-only — NOT terrains. Do not add to TerrainPriority.Layers.
        // A visual tile of the keyed terrain swaps to the variant's sprite when any of its 4 sampled
        // data corners is "void" (out-of-bounds OR tileID == -1 in the level JSON).
        public static readonly Dictionary<string, string> VoidVariants = new Dictionary<string, string>
        {
            { Sea, SeaVoid }
        };

        // Underlying terrains stamped on water cells (e.g. sand) spill into the boundary halo and
        // their soft edges leak past the seam of the SeaVoid sprites. To clip them per-pixel, the
        // black pixels of the keyed variant tileset (e.g. sea_void.png with a black mask outside the
        // sea content) are baked at load time into an alpha-0 mask applied to the target terrain's
        // sprites. The masked sprites are then used in place of the regular ones at boundary halos.
        public static readonly Dictionary<string, string> VoidMasks = new Dictionary<string, string>
        {
            { Sand, SeaVoid }
        };

        public static readonly Dictionary<string, string> TexturePaths = new Dictionary<string, string>
        {
            { Dirt, "Tiles/Dirt/dirt_texture" },
            { Grass, "Tiles/Grass/grass_texture" },
            { Sand, "Tiles/Sand/sand_texture" },
            { Sea, null }
        };

        public static bool IsKnownTileset(string name) =>
            name != null && ToArchetype.ContainsKey(name.ToLowerInvariant());
    }
}
