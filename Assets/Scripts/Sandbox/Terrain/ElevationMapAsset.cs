using UnityEngine;

namespace Glitchers.EcoKnow.Sandbox.Terrain
{
    /// <summary>
    /// Pre-baked ElevationMap data for a (mapName, seed) pair. Stored as a project
    /// asset so terrain generation runs exactly once per scenario across the team
    /// rather than every cold launch. Lives under
    /// Assets/_EcoKnow/Resources/ElevationMaps/{mapName}_{seed}.asset so it can be
    /// loaded in built games via Resources.Load.
    ///
    /// Generated on first scenario run in-editor (see <see cref="ElevationMap.CreateForScenario"/>);
    /// commit the resulting .asset alongside the scenario JSON. The three Texture2Ds
    /// are stored as sub-assets of the .asset file.
    /// </summary>
    public class ElevationMapAsset : ScriptableObject
    {
        [SerializeField] private int _columns;
        [SerializeField] private int _rows;
        [SerializeField] private int _seed;

        // Classification grids flattened to row-major byte arrays (0/1). Cheap to
        // serialize and trivially convertible back to bool[,].
        [SerializeField] private byte[] _isSeaFlat;
        [SerializeField] private byte[] _isFreshwaterFlat;
        [SerializeField] private byte[] _isOverflowFlat;
        [SerializeField] private byte[] _isFreshOnSandFlat;

        // Per-cell altitude, flattened row-major. Read by gameplay via GetAltitude.
        [SerializeField] private float[] _altitudesFlat;

        // Sub-assets stored inside this .asset file. Set by editor-time generate path.
        [SerializeField] private Texture2D _altitudeTexture;
        [SerializeField] private Texture2D _overflowTexture;
        [SerializeField] private Texture2D _freshSandTexture;

        public int Columns => _columns;
        public int Rows => _rows;
        public int Seed => _seed;
        public byte[] IsSeaFlat => _isSeaFlat;
        public byte[] IsFreshwaterFlat => _isFreshwaterFlat;
        public byte[] IsOverflowFlat => _isOverflowFlat;
        public byte[] IsFreshOnSandFlat => _isFreshOnSandFlat;
        public float[] AltitudesFlat => _altitudesFlat;
        public Texture2D AltitudeTexture => _altitudeTexture;
        public Texture2D OverflowTexture => _overflowTexture;
        public Texture2D FreshSandTexture => _freshSandTexture;

        internal void Populate(
            int columns, int rows, int seed,
            byte[] isSea, byte[] isFreshwater, byte[] isOverflow, byte[] isFreshOnSand,
            float[] altitudes,
            Texture2D altitudeTexture, Texture2D overflowTexture, Texture2D freshSandTexture)
        {
            _columns = columns;
            _rows = rows;
            _seed = seed;
            _isSeaFlat = isSea;
            _isFreshwaterFlat = isFreshwater;
            _isOverflowFlat = isOverflow;
            _isFreshOnSandFlat = isFreshOnSand;
            _altitudesFlat = altitudes;
            _altitudeTexture = altitudeTexture;
            _overflowTexture = overflowTexture;
            _freshSandTexture = freshSandTexture;
        }
    }
}
