using System;
using UnityEngine;

namespace Glitchers.EcoKnow.Sandbox.Terrain
{
    /// <summary>
    /// Single source of truth for terrain-name → Material asset mapping. Drives
    /// material assignment on the visual <see cref="UnityEngine.Tilemaps.TilemapRenderer"/>s
    /// created by <see cref="DualGridTerrainRenderer"/>. Authored as an asset
    /// (<c>SO_TerrainShaderConfig.asset</c>) so changing a material does not require
    /// a code edit.
    ///
    /// For water terrains, <see cref="WaterTintController"/> creates per-instance
    /// clones of the asset materials and substitutes them at runtime so colour /
    /// uniform updates do not mutate the on-disk asset.
    /// </summary>
    [CreateAssetMenu(menuName = "EcoKnow/Terrain Shader Config", fileName = "SO_TerrainShaderConfig")]
    public class TerrainShaderConfig : ScriptableObject
    {
        [Serializable]
        public class TerrainMaterial
        {
            [Tooltip("Terrain name string (use TilesetConstants.* values: dirt, grass, sand, sea, freshwater).")]
            public string Terrain;
            public Material Material;
        }

        [SerializeField] private TerrainMaterial[] _materials;

        public Material GetMaterial(string terrain)
        {
            if (_materials == null || string.IsNullOrEmpty(terrain)) return null;
            for (int i = 0; i < _materials.Length; i++)
            {
                if (_materials[i].Terrain == terrain) return _materials[i].Material;
            }
            return null;
        }
    }
}
