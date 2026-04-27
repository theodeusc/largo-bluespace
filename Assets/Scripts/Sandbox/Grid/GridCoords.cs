using UnityEngine;

namespace Glitchers.EcoKnow.Sandbox.Grid
{
    public readonly struct GridCoords
    {
        public float CellStep { get; }

        public static readonly Vector3 DualGridOffset = new Vector3(-0.5f, -0.5f, 0f);

        public GridCoords(float cellScale, float cellGap)
        {
            CellStep = cellScale + cellGap;
        }

        public Vector3Int CellToData(int col, int row)
        {
            return new Vector3Int(col, -row, 0);
        }

        public Vector3 CellToWorld(int col, int row)
        {
            return new Vector3(col * CellStep, -row * CellStep, 0f);
        }

        public (int col, int row) WorldToCell(Vector3 localPos)
        {
            int col = Mathf.RoundToInt(localPos.x / CellStep);
            int row = Mathf.RoundToInt(-localPos.y / CellStep);
            return (col, row);
        }

        public static Vector3 VisualTilemapOffset(float z)
        {
            return new Vector3(-0.5f, -0.5f, z);
        }
    }
}
