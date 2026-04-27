using System;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Glitchers.EcoKnow.Sandbox.Terrain
{
    [CreateAssetMenu(menuName = "EcoKnow/Terrain Tile Config")]
    public class TerrainTileConfig : ScriptableObject
    {
        [Serializable]
        public class ArchetypeTileset
        {
            public Archetype Archetype;
            public TileBase[] Tiles = new TileBase[16];
        }

        [SerializeField] private ArchetypeTileset[] _tilesets;

        public TileBase[] GetTileset(Archetype archetype)
        {
            if (_tilesets == null) return null;

            for (int i = 0; i < _tilesets.Length; i++)
            {
                if (_tilesets[i].Archetype == archetype)
                {
                    return _tilesets[i].Tiles;
                }
            }

            return null;
        }
    }
}
