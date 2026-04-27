using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Glitchers.EcoKnow.Sandbox.Terrain
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum Archetype
    {
        Void,
        Soil,
        Vegetation,
        Rock,
        Cliff,
        Liquid,
        Fauna
    }

    public static class ArchetypeExtensions
    {
        private static readonly Archetype[] TerrainArchetypesByPriority = { Archetype.Soil, Archetype.Vegetation, Archetype.Rock, Archetype.Cliff, Archetype.Liquid };

        public static bool IsTerrainArchetype(this Archetype archetype)
        {
            return archetype != Archetype.Void && archetype != Archetype.Fauna;
        }

        public static bool IsMobileArchetype(this Archetype archetype)
        {
            return archetype == Archetype.Fauna;
        }

        public static int GetTerrainPriority(this Archetype archetype)
        {
            switch (archetype)
            {
                case Archetype.Soil: return 0;
                case Archetype.Vegetation: return 1;
                case Archetype.Rock: return 2;
                case Archetype.Cliff: return 3;
                case Archetype.Liquid: return 4;
                default: return -1;
            }
        }

        public static Archetype[] GetTerrainArchetypesByPriority()
        {
            return TerrainArchetypesByPriority;
        }

        public static string GetTilesetName(this Archetype archetype)
        {
            switch (archetype)
            {
                case Archetype.Soil: return "dirt";
                case Archetype.Vegetation: return "grass";
                case Archetype.Rock: return "stone";
                case Archetype.Cliff: return "cliff";
                case Archetype.Liquid: return "sea";
                default: return null;
            }
        }
    }
}
