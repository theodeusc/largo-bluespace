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
            { Sea, "Tiles/Sea/sea_tileset" }
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
