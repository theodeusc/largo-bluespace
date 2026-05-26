using System.Collections.Generic;
using System.Linq;
using Glitchers.EcoKnow.Sandbox.Grid.Regions;
using UnityEngine;
using UnityEngine.UI.Extensions;

namespace Glitchers.EcoKnow.Sandbox.Grid
{
    public class CellEntity
    {
        public enum State { EXTINCT, VULNERABLE, STABLE, ABUNDANT};
        private State _currentState;
        public State CurrentState => _currentState;

        private int _index;
        public int Index => _index;

        private string _id;
        public string ID => _id;

        private int _population;
        public int Population => _population;

        public CellEntity(int index, string id, int population, State state)
        {
            _index = index;
            _id = id;
            _population = population;
            _currentState = state;
        }
    };

    public class Cell : MonoBehaviour
    {
        [SerializeField] protected GameObject highlightObject;
        [SerializeField] protected GameObject selectedObject;
        [SerializeField] protected SpriteRenderer zoneColorRenderer;

        [Header("Tokens")]
        [SerializeField] private Cell_Token _cellTokenPrefab;
        [SerializeField] private FlowLayoutGroup _cellTokenContainer;
        [SerializeField] private float spacingCompact;
        [SerializeField] private float spacingWide;
        // Master toggle for the in-world token canvas. Off by design — population counts and
        // state-border "warnings" rendered per cell were noisy and made the map hard to read.
        // The underlying entity/population data is unaffected; only the world-space display
        // is suppressed. Flip to true on the prefab if a future scenario wants the numbers
        // back without redoing the wiring.
        [SerializeField] private bool _showCellTokens = false;

        private int _row;
        private int _column;
        private bool _mouseOver;

        private const string LogChannel = "[Cell]";

        public int Row => _row;
        public int Column => _column;
        public Vector2 GridPosition => new Vector2(Column, Row);

        public GridManager ParentGrid => GetComponentInParent<GridManager>();

        public void Init(int column, int row, int tileID, Color? zoneColor = null, bool hideZoneColor = false)
        {
            _row = row;
            _column = column;

            this.gameObject.name = $"Cell {_column}_{_row}";

            if (zoneColorRenderer != null)
            {
                if (hideZoneColor)
                {
                    zoneColorRenderer.enabled = false;
                }
                else if (zoneColor.HasValue)
                {
                    zoneColorRenderer.color = zoneColor.Value;
                }
            }

            // When the per-cell token display is disabled, deactivate the entire token
            // container so the world-space canvas (numbers, icons, state-border warnings)
            // never renders. Token-creation and per-frame update paths are also gated so
            // we don't waste work building hidden hierarchies.
            if (!_showCellTokens && _cellTokenContainer != null)
            {
                _cellTokenContainer.gameObject.SetActive(false);
            }

            ShowHighlight(false);
        }

        #region Cells
        public Cell GetNeighbouringCell(int x, int y)
        {
            //Get cell x and y away
            GridManager gridManager = GetComponentInParent<GridManager>();
            if (gridManager != null)
            {
                return gridManager.FindCellAtPosition(_column + x, _row + y);
            }

            return null;
        }

        public List<Cell> GetAllNeighbouringCells()
        {
            List<Cell> neighbours = new List<Cell>();
            GridManager gridManager = GetComponentInParent<GridManager>();
            {
                if (gridManager != null)
                {
                    for (int x = -1; x < 2; x++)
                    {
                        for (int y = -1; y < 2; y++)
                        {
                            Cell cell = gridManager.FindCellAtPosition(_column + x, _row + y);
                            if (cell != null)
                            {
                                neighbours.Add(cell);
                            }
                        }
                    }
                }
            }

            return neighbours;
        }
        #endregion

        #region Highlight and Selection
        public void OnClicked()
        {

        }

        public void ShowHighlight(bool visible)
        {
            highlightObject?.SetActive(visible);
        }

        public void ShowSelected(bool visible)
        {
            selectedObject?.SetActive(visible);
        }
        #endregion



        #region MouseEvents
        void OnMouseEnter()
        {
            _mouseOver = true;
            ShowHighlight(true);
        }

        void OnMouseExit()
        {
            _mouseOver = false;
            ShowHighlight(false);
        }

        private void OnMouseDown()
        {
        }

        private void OnMouseUp()
        {
        }
        #endregion

        #region Entities
        public CellEntity[] GetCellEntities()
        {
            if (SandboxManager.Instance.EntityManager != null)
            {
                return SandboxManager.Instance?.EntityManager?.GetEntitiesForCell(Column, Row);
            }

            return null;
        }

        public void UpdateEntityCount()
        {
            // Per-cell tokens globally disabled — skip the per-frame refresh entirely.
            if (!_showCellTokens) return;

            // Region-wide compute: visual cells render no tokens. Only the region's compute cell shows entities.
            if (IsVisualInRegionMode()) return;

            CellEntity[] entities = GetCellEntities();
            UpdateTokens(FilterHiddenEntities(entities));
        }

        // Strips entries flagged Entity.HiddenFromCellToken so the count downstream matches
        // the number of tokens that were actually spawned by SetupEntityTokens. Without this
        // filter, UpdateTokens' childCount-vs-length safety check aborts the per-frame refresh.
        private CellEntity[] FilterHiddenEntities(CellEntity[] entities)
        {
            if (entities == null) return entities;
            EntityManager em = SandboxManager.Instance != null ? SandboxManager.Instance.EntityManager : null;
            if (em == null) return entities;

            List<CellEntity> visible = new List<CellEntity>(entities.Length);
            for (int i = 0; i < entities.Length; i++)
            {
                Entity type = em.GetEntityType(entities[i].Index);
                if (type != null && type.HiddenFromCellToken) continue;
                visible.Add(entities[i]);
            }
            return visible.ToArray();
        }

        private bool IsVisualInRegionMode()
        {
            EntityManager em = SandboxManager.Instance != null ? SandboxManager.Instance.EntityManager : null;
            if (em == null || !em.IsRegionMode) return false;
            return em.GetCellType(_column, _row) == CellType.Visual;
        }

        public void ClearEntityTokens()
        {
            if ((_cellTokenPrefab == null) || (_cellTokenContainer == null))
            {
                Debug.LogError($"{LogChannel} Failed to cleanup tokens at Cell Row {_row} / Column {_column}, token prefab or token container is null!");
                return;
            }

            foreach (Cell_Token child in _cellTokenContainer.GetComponentsInChildren<Cell_Token>(true))
            {
                Destroy(child.gameObject);
            }
        }

        public void SetupEntityTokens()
        {
            //Populate with a token for each valid type, regardless of whether they are spawned in the cell yet

            // Per-cell tokens globally disabled — skip instantiation entirely. Saves the
            // per-cell GameObject creation cost on scenario load and guarantees no token
            // visuals reach the world-space canvas regardless of region-compute mode.
            if (!_showCellTokens) return;

            if ((_cellTokenPrefab == null) || (_cellTokenContainer == null))
            {
                Debug.LogError($"{LogChannel} Failed to spawn tokens at Cell Row {_row} / Column {_column}, token prefab or token container is null!");
                return;
            }

            // Region-wide compute: visual cells don't render entities. Skip token setup so they
            // stay clean — UpdateEntityCount is also gated, so the empty container never tries
            // to update non-existent tokens.
            if (IsVisualInRegionMode()) return;

            EntityManager entityManager = SandboxManager.Instance.EntityManager;
            if (entityManager != null)
            {
                CellEntity[] cellEntities = entityManager.GetEntitiesForCell(_column, _row);
                for (int i = 0; i < cellEntities.Length; i++)
                {
                    Entity type = entityManager.GetEntityType(i);
                    // Per-entity opt-out via Scenario JSON: hidden entities still show in the
                    // right-side EntityPanel but skip the per-cell token (saves screen space
                    // for entities the player doesn't need numeric readouts on, e.g. sealife).
                    if (type != null && type.HiddenFromCellToken) continue;

                    int totalPopulation = entityManager.GetTotalPopulationOfEntityType(i);

                    Cell_Token token = Instantiate(_cellTokenPrefab, _cellTokenContainer.transform);
                    token.Init(i, type);
                    token.UpdatePopulation(cellEntities[i].Population, totalPopulation, cellEntities[i].CurrentState);
                    token.SetVisible(cellEntities[i].Population > 0);
                }

                UpdateTokenSpacing();
            }
        }

        public void UpdateTokens(CellEntity[] entityList)
        {
            if (_cellTokenContainer == null)
            {
                return;
            }

            if (_cellTokenContainer.transform.childCount != entityList.Length)
            {
                Debug.LogError($"{LogChannel} Failed to update tokens at Cell Row {_row} / Column {_column}, mismatch between token count and entity list length!");
                return;
            }

            EntityManager entityManager = SandboxManager.Instance.EntityManager;


            if ((entityList != null) && (entityManager != null))
            {
                Cell_Token[] tokens = _cellTokenContainer.GetComponentsInChildren<Cell_Token>(true);
                CellEntity[] orderedEntities = entityList.OrderByDescending(x => x.Population).ThenByDescending(x => x.CurrentState).ToArray();

                if (tokens.Length > 0)
                {
                    for (int i = 0; i < orderedEntities.Length; i++)
                    {
                        int totalPopulation = entityManager.GetTotalPopulationOfEntityType(orderedEntities[i].Index);
                        Cell_Token token = tokens.FirstOrDefault(x => x.Index == orderedEntities[i].Index); // Make sure we match before adjusting any numbers
                        if (token != null)
                        {
                            token.UpdatePopulation(orderedEntities[i].Population, totalPopulation, orderedEntities[i].CurrentState);
                            token.transform.SetSiblingIndex(i);

                            //We only want to set visible once population has increased
                            //We never set invisible at zero population, only extinct
                            if (orderedEntities[i].Population > 0)
                            {
                                token.SetVisible(true);
                            }
                        }
                    }
                }

                UpdateTokenSpacing();
            }
        }

        private void UpdateTokenSpacing()
        {
            if (_cellTokenContainer != null)
            {
                int tokenCount = _cellTokenContainer.GetComponentsInChildren<Cell_Token>(false).Count();

                if (tokenCount <= 4)
                {
                    _cellTokenContainer.SpacingX = spacingWide;
                    _cellTokenContainer.SpacingY = spacingWide;
                }
                else if (tokenCount <= 6)
                {
                    _cellTokenContainer.SpacingX = spacingWide;
                    _cellTokenContainer.SpacingY = spacingCompact;
                }
                else
                {
                    _cellTokenContainer.SpacingX = spacingCompact;
                    _cellTokenContainer.SpacingY = spacingCompact;
                }
            }
        }
        #endregion
    }
}
