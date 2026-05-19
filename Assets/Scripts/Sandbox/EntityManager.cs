using System;
using System.Collections.Generic;
using System.Linq;
using Glitchers.EcoKnow.Sandbox.Grid;
using Glitchers.EcoKnow.Sandbox.Grid.Regions;
using UnityEngine;

namespace Glitchers.EcoKnow.Sandbox
{
    [System.Serializable]
    public record Entity
        (
        string ID,
        string Icon,
        string Colour,
        float GrowthRate,
        float MovementRate,

        float VulnerableThreshold,
        float AbundanceThreshold,

        bool AutoPlace,
        int StartPopulation,

        bool CanHarvest,
        bool CanIntroduce,

        int HarvestLimit,
        int IntroduceLimit,

        Quantity[] HarvestQuantities,
        Quantity[] IntroduceQuantities,

        EntityZoneInformation[] ZoneInformation,

        // Hide this entity's per-cell token while keeping it in the right-side EntityPanel.
        // Defaults to false to preserve existing scenarios; opt-in per-entity in the JSON.
        bool HiddenFromCellToken = false
        );


    [System.Serializable]
    public class EntityZoneInformation
    {
        [SerializeField] public int ZoneID;
        [SerializeField] public float GrowthRate;
        [SerializeField] public float MovementRate;
        [SerializeField] public List<int> Transitions;

        public EntityZoneInformation(int zoneID) { ZoneID = zoneID; Transitions = new List<int>(); }
    }

    public delegate void EntityEvent(int column, int row, int id);

    //This class stores and handles manipulation of the Entity data
    //Data can be requested or modified here
    public class EntityManager : MonoBehaviour
    {
        private IEntityCalculator _entityCalculator = new StandardCalculator(); //Swap this out for different mathematics
        public IEntityCalculator Calculator => _entityCalculator;

        private Matrix _entityMatrix;
        private Dictionary<int, float[,]> _zoneAlphaMatrices;

        public float[,] AlphaMatrix => _entityMatrix.entityMatrix;
        private Entity[] _entityTypeList;
        public int EntityTypeCount => _entityTypeList.Length;


        //Cell lookup table
        //X, Y, entityIndex
        //Stored as long so pollutant values (e.g. e_coli ~3e14) survive without overflow.
        //UI-facing getters clamp to int for display.
        private long[,,] _entityLookupTable;

        public EntityEvent onEntityHarvested;
        public EntityEvent onEntityIntroduced;

        // Region-wide compute mode (scenario-level toggle). When non-null and active,
        // only "compute" cells (one per region) hold real entity populations; "visual"
        // cells are zeroed at scenario load and skipped by the calculators.
        private RegionComputeManager _regionComputeManager;
        public RegionComputeManager RegionComputeManager => _regionComputeManager;
        public bool IsRegionMode => _regionComputeManager != null && _regionComputeManager.IsActive;

        private const string LogChannel = "[EntityManager]";

        #region Setup
        public void RegisterAlphaMatrix(Matrix matrix)
        {
            _entityMatrix = matrix;
        }

        public void RegisterEntities(List<Entity> entityRecords)
        {
            if ((entityRecords == null) || (entityRecords.Count <= 0) || _entityMatrix == null)
            {
                return;
            }

            //Make sure the order we register matches the order from the alpha matrix. This will be important for maths later
            _entityTypeList = entityRecords.OrderBy(x => Array.IndexOf(_entityMatrix.entityIDs, x.ID)).ToArray();
        }

        public void AddEntitiesToGrid(GridDef gridDef, GridManager gridManager)
        {
            if ((gridDef == null) || (gridManager == null))
            {
                return;
            }

            Vector2 gridSize = gridManager.GridSize;
            _entityLookupTable = new long[(int)gridSize.x, (int)gridSize.y, EntityTypeCount];

            //Add arbritrary amount of entities to each cell for now
            for (int row = 0; row < gridSize.y; row++)
            {
                for (int column = 0; column < gridSize.x; column++)
                {
                    for (int i = 0; i < EntityTypeCount; i++)
                    {
                        Entity entityType = GetEntityType(i);

                        int startPopulation = gridDef.HasPopulations() ? gridDef.GetPopulation(column, row, i) : entityType.AutoPlace == true ? entityType.StartPopulation : 0;
                        _entityLookupTable[column, row, i] = gridManager.FindCellAtPosition(column, row) != null ? startPopulation : -1L;
                    }
                }
            }

            StartCoroutine(gridManager.OnEntitiesAdded());
        }
        #endregion

        #region Zones
        public void RegisterZoneAlphaMatrices(Matrix[] matrices)
        {
            if (matrices == null || _entityTypeList == null)
            {
                return;
            }

            _zoneAlphaMatrices = new Dictionary<int, float[,]>();

            // Also set _entityMatrix to the first matrix for legacy compat
            if (matrices.Length > 0)
            {
                _entityMatrix = matrices[0];
            }

            int entityCount = _entityTypeList.Length;
            string[] masterIDs = _entityTypeList.Select(x => x.ID).ToArray();

            foreach (Matrix m in matrices)
            {
                float[,] zoneMatrix = new float[entityCount, entityCount];

                if (m.entityIDs != null && m.entityMatrix != null)
                {
                    // Map each matrix entity ID to master index
                    int[] indexMap = new int[m.entityIDs.Length];
                    for (int i = 0; i < m.entityIDs.Length; i++)
                    {
                        indexMap[i] = Array.IndexOf(masterIDs, m.entityIDs[i]);
                    }

                    for (int row = 0; row < m.entityIDs.Length; row++)
                    {
                        for (int col = 0; col < m.entityIDs.Length; col++)
                        {
                            int masterRow = indexMap[row];
                            int masterCol = indexMap[col];
                            if (masterRow >= 0 && masterCol >= 0)
                            {
                                zoneMatrix[masterCol, masterRow] = m.entityMatrix[col, row];
                            }
                        }
                    }
                }

                _zoneAlphaMatrices[m.zoneIndex] = zoneMatrix;
            }
        }

        public float[,] GetAlphaMatrixForZone(int zoneID)
        {
            if (_zoneAlphaMatrices != null && _zoneAlphaMatrices.TryGetValue(zoneID, out float[,] matrix))
            {
                return matrix;
            }

            return _entityMatrix?.entityMatrix;
        }

        public int GetZoneType(int column, int row)
        {
            if (SandboxManager.Instance.GridManager == null)
            {
                return 0;
            }

            return SandboxManager.Instance.GridManager.GetZoneType(column, row);
        }
        #endregion

        #region Region Mode
        public void SetRegionMode(RegionComputeManager manager)
        {
            _regionComputeManager = manager;
        }

        public CellType GetCellType(int column, int row)
        {
            if (_regionComputeManager == null) return CellType.Compute;
            return _regionComputeManager.GetCellType(column, row);
        }

        // Calculator gate: cells return false here are skipped by the per-cell maths.
        // Returns true when region mode is off (every cell computes) or when the cell
        // is the region's compute cell.
        public bool ShouldComputeCell(int column, int row)
        {
            if (_regionComputeManager == null || !_regionComputeManager.IsActive) return true;
            return _regionComputeManager.GetCellType(column, row) == CellType.Compute;
        }

        // Direct lookup-table access used by the aggregation/seed steps in RegionComputeManager
        // and RegionMovement. Bypasses every higher-level helper so the manager can rewrite
        // populations without recursion. Operates on the underlying long storage so pollutant
        // values (~1e14) are preserved without saturation.
        public long RawGetPopulation(int column, int row, int index)
        {
            if (_entityLookupTable == null) return -1L;
            if (column < 0 || column >= _entityLookupTable.GetLongLength(0)) return -1L;
            if (row < 0 || row >= _entityLookupTable.GetLongLength(1)) return -1L;
            if (index < 0 || index >= _entityLookupTable.GetLongLength(2)) return -1L;
            return _entityLookupTable[column, row, index];
        }

        public void RawSetPopulation(int column, int row, int index, long value)
        {
            if (_entityLookupTable == null) return;
            if (column < 0 || column >= _entityLookupTable.GetLongLength(0)) return;
            if (row < 0 || row >= _entityLookupTable.GetLongLength(1)) return;
            if (index < 0 || index >= _entityLookupTable.GetLongLength(2)) return;
            _entityLookupTable[column, row, index] = value;
        }

        // Saturating cast for UI / consumer paths that still operate in int. Values above
        // int.MaxValue (e.g. pollutant counts at full pollution) clamp to int.MaxValue;
        // negative sentinels (invalid cells) survive intact.
        private static int ClampToInt(long value)
        {
            if (value > int.MaxValue) return int.MaxValue;
            if (value < int.MinValue) return int.MinValue;
            return (int)value;
        }
        #endregion

        #region Entity Data
        public Entity GetEntityType(int index)
        {
            if (index >= _entityTypeList.Count() || index < 0)
            {
                Debug.LogError($"{LogChannel} Failed to find Entity Type with index [{index}], index is invalid!");
                return null;
            }

            return _entityTypeList[index];
        }

        public Entity[] GetEntityTypeList()
        {
            if ((_entityTypeList == null) || (_entityTypeList.Length <= 0))
            {
                Debug.LogError($"{LogChannel} Failed to return Entity Type List, list is null or empty!");
                return null;
            }

            return _entityTypeList;
        }

        public int GetEntityIndex(Entity type)
        {
            return Array.IndexOf(_entityTypeList, type);
        }

        public int GetEntityIndex(string id)
        {
            return Array.IndexOf(_entityTypeList, _entityTypeList.FirstOrDefault(x => x.ID.Equals(id, StringComparison.OrdinalIgnoreCase)));
        }

        public CellEntity[] GetEntitiesForCell(int column, int row)
        {
            CellEntity[] entityCounts = new CellEntity[EntityTypeCount];

            for (int i = 0; i < EntityTypeCount; i++)
            {
                Entity type = _entityTypeList[i];

                string id = type.ID;
                long populationLong = _entityLookupTable[column, row, i];
                int population = ClampToInt(populationLong);

                CellEntity.State state = CellEntity.State.STABLE;
                if (populationLong == 0)
                {
                    state = CellEntity.State.EXTINCT;
                }
                else if (populationLong <= type.VulnerableThreshold)
                {
                    state = CellEntity.State.VULNERABLE;
                }
                else if (populationLong >= type.AbundanceThreshold)
                {
                    state = CellEntity.State.ABUNDANT;
                }

                entityCounts[i] = new CellEntity(i, id, population, state);
            }

            return entityCounts;
        }

        public int GetPopulationInCell(int column, int row, int index)
        {
            if (index >= _entityLookupTable.GetLongLength(2) || index < 0)
            {
                Debug.LogError($"{LogChannel} Failed to find population of Entity with index [{index}] in Cell [{column}, {row}], index is invalid!");
                return 0;
            }

            return ClampToInt(_entityLookupTable[column, row, index]);
        }

        public Dictionary<string, int> GetPopulationsInCell(int column, int row)
        {
            Dictionary<string, int> populations = new Dictionary<string, int>();

            for (int i = 0; i < EntityTypeCount; i++)
            {
                Entity type = _entityTypeList[i];
                int population = ClampToInt(_entityLookupTable[column, row, i]);
                populations.Add(type.ID, population);
            }

            return populations;
        }

        public Dictionary<string, int>[,] GetPopulationsByCell()
        {
            Dictionary<string, int>[,] populations = new Dictionary<string, int>[_entityLookupTable.GetLongLength(0), _entityLookupTable.GetLongLength(1)];

            for (int column = 0; column < _entityLookupTable.GetLongLength(0); column++)
            {
                for (int row = 0; row < _entityLookupTable.GetLongLength(1); row++)
                {
                    populations[column, row] = GetPopulationsInCell(column, row);
                }
            }

            return populations;
        }

        public int GetTotalPopulationOfEntityType(int index)
        {
            if (index >= _entityLookupTable.GetLongLength(2) || index < 0)
            {
                Debug.LogError($"{LogChannel} Failed to find total population of Entity with index [{index}], index is invalid!");
                return 0;
            }

            //Find total. Accumulate in long to avoid mid-sum overflow on pollutants;
            //consumers receive a saturating int (large pollutant totals display as int.MaxValue).
            long totalPopulation = 0L;
            for (int column = 0; column < _entityLookupTable.GetLongLength(0); column++)
            {
                for (int row = 0; row < _entityLookupTable.GetLongLength(1); row++)
                {
                    long population = _entityLookupTable[column, row, index];
                    if (population > 0L)
                    {
                        totalPopulation += population; //-1 population means the entity/cell is not valid, so don't add it
                    }
                }
            }

            return ClampToInt(totalPopulation);
        }

        public int GetHarvestLimits(int index)
        {
            Entity type = GetEntityType(index);
            if (type != null)
            {
                return type.HarvestLimit;
            }

            return 0;
        }

        public int GetIntroduceLimits(int index)
        {
            Entity type = GetEntityType(index);
            if (type != null)
            {
                return type.IntroduceLimit;
            }

            return 0;
        }

        public Quantity[] GetHarvestRewards(int index)
        {
            Entity type = GetEntityType(index);
            if (type != null)
            {
                return type.HarvestQuantities;
            }

            return null;
        }

        public Quantity[] GetIntroduceCosts(int index)
        {
            Entity type = GetEntityType(index);
            if (type != null)
            {
                return type.IntroduceQuantities;
            }

            return null;
        }
        #endregion

        #region Data Manipulation
        public bool TryHarvestEntityFromCell(int column, int row, int index, int amount)
        {
            if (column < 0 || row < 0 || column >= _entityLookupTable.GetLongLength(0) || row >= _entityLookupTable.GetLongLength(1))
            {
                Debug.LogError($"{LogChannel} Failed to harvest entity from Cell [{column}, {row}], location out of bounds!");
                return false;
            }

            if (index >= 0 && index < _entityLookupTable.GetLongLength(2))
            {
                //Check our type first
                Entity type = _entityTypeList[index];
                if (type == null || !type.CanHarvest)
                {
                    Debug.LogError($"{LogChannel} Failed to harvest entity from Cell [{column}, {row}]. Entity index {index} cannot be harvested!");
                    return false;
                }

                //Check population
                long currentPopulation = _entityLookupTable[column, row, index];
                if (currentPopulation == 0L) //Fail interaction if we have nothing to harvest
                {
                    return false;
                }

                //TODO: We cannot harvest more than we have in the cell, so what sort of user feedback should we get if we try to harvest too much?

                long newPopulation = Math.Max(currentPopulation - amount, 0L);
                long difference = newPopulation - currentPopulation;
                _entityLookupTable[column, row, index] = newPopulation;

                SandboxManager.Instance.PlayerInventory.AddQuantities(type.HarvestQuantities, (int)Math.Abs(difference));
                Debug.Log($"{LogChannel} [HARVEST Entity {index}] Current: {currentPopulation} / New: {newPopulation} / Difference: {difference}");

                onEntityHarvested?.Invoke(column, row, index);

                return true;
            }
            else
            {
                Debug.LogError($"{LogChannel} Failed to harvest entity from Cell [{column}, {row}]. Entity index {index} is invalid!");
            }

            return false;
        }

        public bool TryIntroduceEntityToCell(int column, int row, int index, int amount)
        {
            if (column < 0 || row < 0 || column >= _entityLookupTable.GetLongLength(0) || row >= _entityLookupTable.GetLongLength(1))
            {
                Debug.LogError($"{LogChannel} Failed to introduce entity to Cell [{column}, {row}], location out of bounds!");
                return false;
            }

            if (index >= 0 && index < _entityLookupTable.GetLongLength(2))
            {
                //Check our type first
                Entity type = _entityTypeList[index];
                if (type == null || !type.CanIntroduce)
                {
                    Debug.LogError($"{LogChannel} Failed to introduce entity to Cell [{column}, {row}]. Entity index {index} cannot be introduced!");
                    return false;
                }

                bool hasRequiredQuantities = SandboxManager.Instance.PlayerInventory.HasQuantities(type.IntroduceQuantities, amount);
                if (!hasRequiredQuantities)
                {
                    Debug.LogError($"{LogChannel} Failed to introduce entity to Cell [{column}, {row}]. Player does not have the required resources to introduce Entity of type {index}");
                    return false;
                }

                long currentPopulation = _entityLookupTable[column, row, index];
                long newPopulation = currentPopulation + amount;
                long difference = newPopulation - currentPopulation;
                _entityLookupTable[column, row, index] = newPopulation;

                SandboxManager.Instance.PlayerInventory.RemoveQuantities(type.IntroduceQuantities, amount);
                Debug.Log($"{LogChannel} [INTRODUCE Entity {index}] Current: {currentPopulation} / New: {newPopulation} / Difference: {difference}");

                onEntityIntroduced?.Invoke(column, row, index);

                return true;
            }
            else
            {
                Debug.LogError($"{LogChannel} Failed to introduce entity to Cell [{column}, {row}]. Entity index {index} is invalid!");
            }

            return false;
        }
        #endregion


        #region Population and Movement Maths
        public void PerformCalculations()
        {
            _entityCalculator?.CalculatePopulations(this, _entityLookupTable);
            _entityCalculator?.CalculateMovement(this, _entityLookupTable);
        }
 
        public int GetValidNeighbourCount(int column, int row, int entity)
        {
            int neighbours = 0;
            for (int x = -1; x < 2; x++)
            {
                for (int y = -1; y < 2; y++)
                {
                    if (x != 0 || y != 0)
                    {
                        int xPos = column + x;
                        int yPos = row + y;

                        if (xPos >= 0 &&
                            xPos < _entityLookupTable.GetLongLength(0) &&
                            yPos >= 0 &&
                            yPos < _entityLookupTable.GetLongLength(1))
                        {
                            //Populations less than 0 are invalid cells
                            long population = _entityLookupTable[xPos, yPos, entity];
                            if (population >= 0L)
                            {
                                neighbours += 1;
                            }
                        }
                    }
                }
            }

            return neighbours;
        }

        public Vector2[] FindOppositeEdges(int column, int row, int entity, bool ignoreNeighbours = false)
        {
            List<Vector2> oppositeEdges = new List<Vector2>();

            //Test Left
            bool edgeLeft = true;
            bool edgeRight = true;

            for (int i = 0; i < _entityLookupTable.GetLongLength(0); i++)
            {
                if (_entityLookupTable[i, row, entity] >= 0)
                {
                    if (i < column)
                    {
                        edgeLeft = false;
                    }
                    else if (i > column)
                    {
                        edgeRight = false;
                    }
                }
            }

            bool edgeTop = true;
            bool edgeBottom = true;
            for (int j = 0; j < _entityLookupTable.GetLongLength(1); j++)
            {
                if (_entityLookupTable[column, j, entity] >= 0)
                {
                    if (j < row)
                    {
                        edgeTop = false;
                    }
                    else if (j > row)
                    {
                        edgeBottom = false;
                    }
                }
            }


            //Now gather our opposite Cells if relevant
            if (edgeLeft)
            {
                int limit = ignoreNeighbours ? column + 1 : column;
                for (int i = (int)_entityLookupTable.GetLongLength(0) - 1; i > limit; i--)
                {
                    if (_entityLookupTable[i, row, entity] >= 0)
                    { 
                        oppositeEdges.Add(new Vector2(i, row));
                        break;
                    }
                }
            }

            if (edgeRight)
            {
                int limit = ignoreNeighbours ? column - 1 : column;
                for (int j = 0; j < limit; j++)
                {
                    if (_entityLookupTable[j, row, entity] >= 0)
                    {
                        oppositeEdges.Add(new Vector2(j, row));
                        break;
                    }
                }
            }

            if (edgeTop)
            {
                int limit = ignoreNeighbours ? row + 1 : row;
                for (int k = (int)_entityLookupTable.GetLongLength(1) - 1; k > limit; k--)
                {
                    if (_entityLookupTable[column, k, entity] >= 0)
                    {
                        oppositeEdges.Add(new Vector2(column, k));
                        break;
                    }
                }
            }

            if (edgeBottom)
            {
                int limit = ignoreNeighbours ? row - 1 : row;
                for (int l = 0; l < limit; l++)
                {
                    if (_entityLookupTable[column, l, entity] >= 0)
                    {
                        oppositeEdges.Add(new Vector2(column, l));
                        break;
                    }
                }
            }

            return oppositeEdges.ToArray();
        }
        #endregion
    }
}
