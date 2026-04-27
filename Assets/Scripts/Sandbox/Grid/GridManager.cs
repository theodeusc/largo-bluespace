using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Glitchers.EcoKnow.Sandbox.Terrain;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Glitchers.EcoKnow.Sandbox.Grid
{
    public delegate void CellEvent(Cell inCell);

    [System.Serializable]
    public class GridDef
    {
        public int rows;
        public int columns;
        public int[,] tileIDs;
        public int[,][] tilePopulations;
        public ZoneDef[] zoneDefs; // null = legacy single-zone map
        public string bgColour; // null = no background color specified

        public bool HasZones()
        {
            return zoneDefs != null && zoneDefs.Count() > 0;
        }

        public int GetZoneType(int column, int row)
        {
            if (tileIDs != null
                && column >= 0 && column < tileIDs.GetLength(0)
                && row >= 0 && row < tileIDs.GetLength(1))
            {
                int id = tileIDs[column, row];
                return id >= 0 ? id : 0;
            }
            return 0;
        }

        public int GetZoneCount()
        {
            return zoneDefs != null ? zoneDefs.Length : 1;
        }

        public bool HasPopulations()
        {
            return tilePopulations != null;
        }

        public int GetValidEntityCount()
        {
            if (HasPopulations())
            {
                foreach(int[] list in tilePopulations)
                {
                    if (list != null)
                    {
                        return list.Length;
                    }
                }
            }

            return -1;
        }

        public int GetPopulation(int column, int row, int entityIndex)
        {
            int population = 0;
            if (tilePopulations != null)
            {
                if ((column < tilePopulations.GetLongLength(0)) && (row < tilePopulations.GetLongLength(1)))
                {
                    if ((tilePopulations[column, row] != null) && (entityIndex < tilePopulations[column, row].Length))
                    {
                        population = tilePopulations[column, row][entityIndex];
                    }
                }
            }

            return population;
        }
    }


    public class GridManager : MonoBehaviour
    {
        [Header("Camera")]
        [SerializeField] protected GridCamera gridCamera;

        [Header("Background")]
        [SerializeField] private Image _background;
        [SerializeField] private Color _defaultBackgroundColour;

        [Header("Grid")]
        [SerializeField] protected int rows;
        [SerializeField] protected int columns;
        private int[,] _tileZones;
        private bool[,] _voidMask;

        [Header("Cells")]
        [SerializeField] protected Transform cellContainer;
        [SerializeField] protected float cellWidth;
        [SerializeField] protected float cellHeight;
        [SerializeField] protected float cellGap;

        [Header("Prefabs")]
        [SerializeField] protected GameObject cellPrefab;

        [Header("Terrain")]
        [SerializeField] protected DualGridTerrainRenderer _terrainRenderer;

        //Events
        public CellEvent OnCellClicked;

        private Cell[,] cellList;

        public Vector2 CellScale => new Vector2(cellWidth, cellHeight);
        public Vector2 CellSize => GetActualCellSize();
        public float CellGap => cellGap;
        public int TotalCells => GetComponentsInChildren<Cell>().Length;
        public Vector2 GridSize => new Vector2(columns, rows);
        private Camera Camera => gridCamera == null ? Camera.main : gridCamera.Camera;

        private const string LogChannel = "[GridManager]";


        #region Static Helper Functions
        public static GridDef LoadGridDef(TextAsset mapCSV)
        {
            GridDef def = new GridDef();

            string rawCSV = mapCSV.text;
            rawCSV = rawCSV.Trim(' ', '\n', '\r');

            // Separate header lines (#-prefixed) from data rows
            string[] allLines = rawCSV.Replace("\r", string.Empty).Split('\n');
            List<string> headerLines = new List<string>();
            List<string> dataLines = new List<string>();
            foreach (string line in allLines)
            {
                if (line.TrimStart().StartsWith("#"))
                    headerLines.Add(line.TrimStart());
                else
                    dataLines.Add(line);
            }

            // Parse headers
            foreach (string header in headerLines)
            {
                if (header.StartsWith("#bg:"))
                {
                    def.bgColour = header.Substring("#bg:".Length).Trim();
                }
                else if (header.StartsWith("#zones:"))
                {
                    string zonesStr = header.Substring("#zones:".Length);
                    string[] zoneParts = zonesStr.Split(',');
                    List<ZoneDef> zones = new List<ZoneDef>();
                    foreach (string part in zoneParts)
                    {
                        // Format: "0=Land:#2ed669"
                        string[] idAndRest = part.Split('=');
                        if (idAndRest.Length >= 2 && int.TryParse(idAndRest[0], out int zoneId))
                        {
                            string[] nameAndColour = idAndRest[1].Split(':');
                            string zoneName = nameAndColour[0];
                            string zoneColour = nameAndColour.Length > 1 ? nameAndColour[1] : "";
                            zones.Add(new ZoneDef(zoneId, zoneName, zoneColour));
                        }
                    }
                    if (zones.Count > 0)
                    {
                        def.zoneDefs = zones.ToArray();
                    }
                }
            }

            // Rebuild CSV data from data lines only
            string dataCSV = string.Join("\n", dataLines);

            var regex = @",(?![^[]*\])"; //Look ahead, ignore commas within [] parentheses
            string[] IDs = Regex.Split(dataCSV.Replace("\n", ","), regex);

            def.rows = dataLines.Count;
            def.columns = IDs.Length / def.rows;

            def.tileIDs = new int[def.columns, def.rows];

            bool hasPopulations = dataCSV.Contains('[');
            def.tilePopulations = hasPopulations ? new int[def.columns, def.rows][] : null;


            int tileIndex = 0;
            for (int y = 0; y < def.rows; y++)
            {
                for (int x = 0; x < def.columns; x++)
                {
                    string[] tileDef = IDs[tileIndex].Replace("]", string.Empty).Split('[');

                    int tileID = -1;
                    int.TryParse(tileDef[0], out tileID);

                    if (def.tilePopulations != null)
                    {
                        int[] populations = tileDef.Length > 1 ? Array.ConvertAll(tileDef[1].Split(','), int.Parse) : null;
                        def.tilePopulations[x, y] = populations;
                    }

                    def.tileIDs[x, y] = tileID;
                    tileIndex++;
                }
            }

            string zoneInfo = def.zoneDefs != null ? $" / Zones: {def.zoneDefs.Length}" : "";
            Debug.Log($"Map: {mapCSV.name} / Rows: {def.rows} Columns: {def.columns}{zoneInfo}");

            return def;
        }
        #endregion

        public void Awake()
        {
            DisableGrid();
            if (Camera != null)
            {
                Camera.enabled = true;
            }
        }

        public void Init()
        {
            if (SandboxManager.Instance.EntityManager != null)
            {
                SandboxManager.Instance.EntityManager.onEntityHarvested += OnEntityUpdated;
                SandboxManager.Instance.EntityManager.onEntityIntroduced += OnEntityUpdated;
            }
        }

        public void Cleanup()
        {
            if (SandboxManager.Instance.EntityManager != null)
            {
                SandboxManager.Instance.EntityManager.onEntityHarvested -= OnEntityUpdated;
                SandboxManager.Instance.EntityManager.onEntityIntroduced -= OnEntityUpdated;
            }
        }

        public void SetupGrid(GridDef gridDef)
        {
            if (cellPrefab == null)
            {
                return;
            }

            rows = gridDef.rows;
            columns = gridDef.columns;

            RegisterTileZones(gridDef.tileIDs);

            SetBackgroundColour(gridDef.bgColour);

            // Build zone color lookup from zoneDefs
            Dictionary<int, Color> zoneColors = null;
            if (gridDef.zoneDefs != null)
            {
                zoneColors = new Dictionary<int, Color>();
                foreach (ZoneDef zone in gridDef.zoneDefs)
                {
                    if (ColorUtility.TryParseHtmlString(zone.Colour, out Color color))
                    {
                        zoneColors[zone.ID] = color;
                    }
                }
            }

            //Clear old cells. Skip non-Cell children (e.g. TerrainRoot) so the terrain renderer
            //isn't destroyed alongside cells when the scenario reloads.
            foreach (Transform child in cellContainer)
            {
                if (child.GetComponent<Cell>() != null)
                {
                    Destroy(child.gameObject);
                }
            }

            cellList = new Cell[columns, rows];

            bool hideZoneColor = _terrainRenderer != null;

            //Generate new ones
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    int tileID = gridDef.tileIDs[x, y];
                    if (tileID == -1)
                    {
                        continue;
                    }

                    GameObject cell = Instantiate(cellPrefab, cellContainer, false);
                    cell.transform.localScale = new Vector3(cellWidth, cellHeight, 1f);
                    cell.transform.localPosition = new Vector2(x * (cellWidth + cellGap), y * -(cellHeight + cellGap));

                    Color? zoneColor = null;
                    if (zoneColors != null && zoneColors.TryGetValue(tileID, out Color c))
                    {
                        zoneColor = c;
                    }

                    cell.GetComponent<Cell>()?.Init(x, y, tileID, zoneColor, hideZoneColor);
                    cellList[x, y] = cell.GetComponent<Cell>();
                }
            }

            if (_terrainRenderer != null)
            {
                _terrainRenderer.Build(columns, rows, cellWidth, cellGap, GetZoneType, IsVoid);
            }

            gridCamera.Init(this);
        }

        public void EnableGrid()
        {
            if (cellContainer != null)
            {
                cellContainer.gameObject.SetActive(true);
            }
            if (gridCamera != null)
            {
                gridCamera.gameObject.SetActive(true);
            }
        }

        public void DisableGrid()
        {
            if (cellContainer != null)
            {
                cellContainer.gameObject.SetActive(false);
            }
            if (gridCamera != null)
            {
                gridCamera.gameObject.SetActive(false);
            }
        }

        public void HandleInput()
        {
            gridCamera?.UpdateInput();

            if (Input.GetButtonDown("Fire1"))
            {
                // Don't process cell clicks if camera is being dragged
                if (gridCamera != null && gridCamera.IsDragging())
                {
                    return;
                }
                
                Cell cell = CastToCell();
                if (cell != null)
                {
                    cell.OnClicked();
                    OnCellClicked?.Invoke(cell);
                }
            }
        }

        private Cell CastToCell()
        {
            //Do we have touch input?
            if ((Input.touchCount > 0) && (Input.GetTouch(0).phase == TouchPhase.Began))
            {
                if (EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId))
                {
                    return null;
                }
            }
            //No touch input, check default pointer
            else
            {
                if (EventSystem.current.IsPointerOverGameObject())
                {
                    return null;
                }
            }


            RaycastHit2D hit;
            float distance = 100f;

            Ray ray = Camera.ScreenPointToRay(Input.mousePosition);

            hit = Physics2D.Raycast(ray.origin, ray.direction, distance);

            if (hit.transform != null)
            {
                //Debug.DrawLine(ray.origin, hit.point, Color.red, 2f);
                //Debug.Log(hit.transform.gameObject.name);

                Cell cell = hit.transform.gameObject.GetComponent<Cell>();
                return cell;
            }

            return null;
        }

        public Cell FindCellAtPosition(int column, int row)
        {
            if ((cellList != null) && (cellList.Length > 0))
            {
                //Search for our cell
                if ((row >= 0)
                    && (column >= 0)
                    && (column < cellList.GetLength(0))
                    && (row < cellList.GetLength(1)))
                {
                    return cellList[column, row];
                }
            }

            return null;
        }

        public Vector2 GetActualCellSize()
        {
            Vector2 size = Vector2.zero;

            if (cellPrefab != null)
            {
                SpriteRenderer spriteRenderer = cellPrefab.GetComponentInChildren<SpriteRenderer>();
                if (spriteRenderer != null)
                {
                    size = spriteRenderer.sprite.bounds.size;
                }
            }

            return size;
        }


        #region Camera
        public void CenterCamera()
        {
            gridCamera?.CenterCamera();
        }

        public void FocusCell(Cell inCell)
        {
            if (gridCamera != null)
            {
                gridCamera.FocusCamera(inCell.Column, inCell.Row);
            }
        }
        #endregion


        #region Background
        private void SetBackgroundColour(string hexColour)
        {
            if (_background == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(hexColour))
            {
                _background.color = _defaultBackgroundColour;
            }

            if (ColorUtility.TryParseHtmlString(hexColour, out Color bgColor))
            {
                _background.color = bgColor;
            }
        }
        #endregion

        #region Entities
        public IEnumerator OnEntitiesAdded()
        {
            foreach(Cell cell in cellList)
            {
                if (cell != null)
                {
                    cell.ClearEntityTokens();
                }
            }

            yield return new WaitForEndOfFrame();

            foreach(Cell cell in cellList)
            {
                if (cell != null)
                {
                    cell.SetupEntityTokens();
                }
            }

            yield return new WaitForEndOfFrame();

            UpdateAllCells();
        }

        public void OnEntityUpdated(int column, int row, int id)
        {
            UpdateAllCells(); //We need to update all cells so that the proportional percentage reflects Harvest/Introduce changes
        }

        public void UpdateAllCells()
        {
            foreach(Cell cell in cellList)
            {
                if (cell != null)
                {
                    cell.UpdateEntityCount();
                }
            }
        }
        #endregion

        #region Zones
        public void RegisterTileZones(int[,] tileIDs)
        {
            if (tileIDs == null)
            {
                return;
            }

            int cols = tileIDs.GetLength(0);
            int rows = tileIDs.GetLength(1);
            _tileZones = new int[cols, rows];
            _voidMask = new bool[cols, rows];
            for (int x = 0; x < cols; x++)
            {
                for (int y = 0; y < rows; y++)
                {
                    int id = tileIDs[x, y];
                    _tileZones[x, y] = id >= 0 ? id : 0;
                    _voidMask[x, y] = id < 0;
                }
            }
        }

        public int GetZoneType(int column, int row)
        {
            if (_tileZones != null
                && column >= 0 && column < _tileZones.GetLength(0)
                && row >= 0 && row < _tileZones.GetLength(1))
            {
                return _tileZones[column, row];
            }
            return 0;
        }

        public bool IsVoid(int column, int row)
        {
            if (_voidMask == null) return true;
            if (column < 0 || column >= _voidMask.GetLength(0)) return true;
            if (row < 0 || row >= _voidMask.GetLength(1)) return true;
            return _voidMask[column, row];
        }
        #endregion
    }
}
