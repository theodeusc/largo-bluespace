using System;
using System.Linq;
using System.Collections.Generic;
using Glitchers.EcoKnow.Sandbox.Grid;
using UnityEngine;

namespace Glitchers.EcoKnow.Sandbox
{
    public class StandardWraparoundCalculator : IEntityCalculator
    {
        public string Name() => "Standard (Wraparound)";
        public string Version() => "1.0";

        public void CalculatePopulations(EntityManager entityManager, long[,,] entityLookupTable)
        {
            if (entityManager == null)
            {
                Debug.LogError($"[{Name()} Calculator] EntityManager is null! Aborting calculations...");
                return;
            }

            for (int column = 0; column < entityLookupTable.GetLongLength(0); column++)
            {
                for (int row = 0; row < entityLookupTable.GetLongLength(1); row++)
                {
                    CellEntity[] entityList = entityManager.GetEntitiesForCell(column, row);

                    if ((entityList == null) || (entityList.Length <= 0))
                    {
                        Debug.LogError($"[{Name()} Calculator] No entities found for Cell [{row} , {column}]. Aborting calculations...");
                        return;
                    }

                    if (entityList.Length != entityManager.EntityTypeCount)
                    {
                        Debug.LogError($"[{Name()} Calculator] Entity count [{entityList.Length}] for Cell [{row} , {column}] does not match the Simulation Entity count [{entityManager.EntityTypeCount}]! Aborting calculations...");
                        return;
                    }

                    //Check for empty/invalid cells
                    int nullCount = 0;
                    foreach (CellEntity entity in entityList)
                    {
                        if (entity.Population < 0)
                        {
                            nullCount += 1;
                        }
                    }

                    if (nullCount >= entityManager.EntityTypeCount)
                    {
                        //This cell is completely empty, don't bother calculating
                        //Debug.Log($"Row {row} / Column {column} is empty!");
                        continue;
                    }

                    int zone = entityManager.GetZoneType(column, row);
                    float[,] A = entityManager.GetAlphaMatrixForZone(zone);
                    if ((A.GetLongLength(0) != entityManager.EntityTypeCount) || (A.GetLongLength(1) != entityManager.EntityTypeCount))
                    {
                        Debug.LogError($"[{Name()} Calculator] Entity count [{entityList.Length}] does not match the entity count of the Alpha Matrix. Aborting calculations...");
                        return;
                    }

                    double[] r = entityManager.GetEntityTypeList().Select(x => (double)(x.ZoneInformation != null && x.ZoneInformation.FirstOrDefault(z => z.ZoneID == zone) != null ? x.ZoneInformation.FirstOrDefault(z => z.ZoneID == zone).GrowthRate : x.GrowthRate)).ToArray();
                    double[] N = new double[entityManager.EntityTypeCount];
                    for (int n = 0; n < N.Length; n++)
                    {
                        N[n] = (double)entityLookupTable[column, row, n];
                    }

                    //AN
                    double[] AN = new double[entityManager.EntityTypeCount];
                    for (int yy = 0; yy < A.GetLongLength(1); yy++)
                    {
                        double result = 0.0;
                        for (int xx = 0; xx < A.GetLongLength(0); xx++)
                        {
                            result += (double)A[xx, yy] * N[xx];
                            //Debug.Log(A[xx, yy]);
                        }

                        AN[yy] = result;
                    }

                    //N + N.(r + AN)
                    double[] NNrAN = new double[entityManager.EntityTypeCount];
                    for (int a = 0; a < N.Length; a++)
                    {
                        NNrAN[a] = N[a] + (N[a] * (r[a] + AN[a]));
                        //Debug.Log(NNrAN[a]);
                    }

                    //Update entity numbers
                    for (int b = 0; b < NNrAN.Length; b++)
                    {
                        double v = NNrAN[b];
                        if (double.IsNaN(v) || v <= 0.0) entityLookupTable[column, row, b] = 0L;
                        else if (v >= (double)long.MaxValue) entityLookupTable[column, row, b] = long.MaxValue;
                        else entityLookupTable[column, row, b] = (long)Math.Floor(v);
                    }
                }
            }
        }

        public void CalculateMovement(EntityManager entityManager, long[,,] entityLookupTable)
        {
            if (entityManager == null)
            {
                Debug.LogError($"[{Name()} Calculator] EntityManager is null! Aborting calculations...");
                return;
            }

            //We want to calculate the entity count change in each cell, THEN apply that difference to the entire grid
            //Perform this per-entity, following the maths provided
            for (int i = 0; i < entityManager.EntityTypeCount; i++)
            {
                Entity entity = entityManager.GetEntityTypeList()[i];

                if (entity.MovementRate > 0f || (entity.ZoneInformation != null && entity.ZoneInformation.Any(x => x.MovementRate > 0f)))
                {
                    long[,,] movementTable = new long[entityLookupTable.GetLongLength(0), entityLookupTable.GetLongLength(1), entityLookupTable.GetLongLength(2)];

                    //Move our requested entity in each cell
                    for (int column = 0; column < entityLookupTable.GetLongLength(0); column++)
                    {
                        for (int row = 0; row < entityLookupTable.GetLongLength(1); row++)
                        {
                            long currentPopulation = entityLookupTable[column, row, i];
                            if (currentPopulation < 0L)
                            {
                                //This cell/entity is empty, do not perform movement calculations
                                continue;
                            }

                            int currentZone = entityManager.GetZoneType(column, row);
                            float movementRate = entity.ZoneInformation != null && entity.ZoneInformation.Any(x => x.ZoneID == currentZone) ? entity.ZoneInformation.FirstOrDefault(x => x.ZoneID == currentZone).MovementRate : entity.MovementRate;

                            if (movementRate <= 0f)
                            {
                                continue;
                            }


                            // Get valid neighbors for this cell, filtering by zone transitions
                            List<Vector2Int> validNeighbors = GetValidNeighbors(entityManager, entityLookupTable, column, row, i, entity, currentZone);
                            int neighbouringCellCount = validNeighbors.Count;

                            Vector2[] edgeCells = entityManager.FindOppositeEdges(column, row, i, true);

                            // Filter edges by zone transitions
                            if (entity.ZoneInformation != null)
                            {
                                // Filter edge cells
                                List<Vector2> filteredEdges = new List<Vector2>();
                                foreach (Vector2 edge in edgeCells)
                                {
                                    int edgeZone = entityManager.GetZoneType((int)edge.x, (int)edge.y);

                                    bool canTransition = entity.ZoneInformation.Any(x => x.ZoneID == currentZone && x.Transitions.Contains(edgeZone));
                                    if (canTransition)
                                    {
                                        filteredEdges.Add(edge);
                                    }
                                }

                                edgeCells = filteredEdges.ToArray();
                            }

                            if (neighbouringCellCount == 0 && edgeCells.Count() == 0)
                            {
                                continue;
                            }

                            //get number of entities to move. Avoid an O(N) per-individual loop for
                            //huge populations (pollutants ~1e14) — switch to a binomial-style
                            //normal approximation above a safety threshold.
                            long entitiesToMove;
                            if (currentPopulation < 1000L)
                            {
                                entitiesToMove = 0L;
                                int small = (int)currentPopulation;
                                for (int j = 0; j < small; j++)
                                {
                                    entitiesToMove += UnityEngine.Random.Range(0f, 1f) <= movementRate ? 1 : 0;
                                }
                            }
                            else
                            {
                                double mean = (double)currentPopulation * movementRate;
                                double stdDev = Math.Sqrt((double)currentPopulation * movementRate * (1.0 - movementRate));
                                double u1 = 1.0 - UnityEngine.Random.Range(0f, 1f);
                                double u2 = 1.0 - UnityEngine.Random.Range(0f, 1f);
                                double standardNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
                                double sample = mean + stdDev * standardNormal;
                                if (sample <= 0.0) entitiesToMove = 0L;
                                else if (sample >= (double)currentPopulation) entitiesToMove = currentPopulation;
                                else entitiesToMove = (long)Math.Round(sample);
                            }

                            //divide moving entities by the number of valid neighbours
                            int targetCellCount = neighbouringCellCount + edgeCells.Count();
                            long entitiesMovingPerCell = targetCellCount > 0 ? entitiesToMove / targetCellCount : 0L;

                            //Add to neighbouring cells and remove from current cell respectively
                            for (int x = -1; x < 2; x++)
                            {
                                for (int y = -1; y < 2; y++)
                                {
                                    int xPos = column + x;
                                    int yPos = row + y;

                                    if (x == 0 && y == 0)
                                    {
                                        movementTable[xPos, yPos, i] -= (entitiesMovingPerCell * targetCellCount);
                                    }
                                    else if (xPos >= 0 &&
                                            xPos < entityLookupTable.GetLongLength(0) &&
                                            yPos >= 0 &&
                                            yPos < entityLookupTable.GetLongLength(1))
                                    {
                                        //Check for valid cell and zone transition
                                        if (entityLookupTable[xPos, yPos, i] >= 0L)
                                        {
                                            if (entity.ZoneInformation != null)
                                            {
                                                int neighbourZone = entityManager.GetZoneType(xPos, yPos);
                                                if (entity.ZoneInformation.Any(x => x.ZoneID == currentZone && x.Transitions.Contains(neighbourZone)))
                                                {
                                                    movementTable[xPos, yPos, i] += entitiesMovingPerCell;
                                                }
                                            }
                                            else
                                            {
                                                movementTable[xPos, yPos, i] += entitiesMovingPerCell;
                                            }
                                        }
                                    }
                                }
                            }

                            //Wrap around edges
                            foreach(Vector2 cell in edgeCells)
                            {
                                movementTable[(int)cell.x, (int)cell.y, i] += entitiesMovingPerCell;
                            }
                        }
                    }

                    //Apply the movementTable numbers to our actual cells
                    for (int column = 0; column < entityLookupTable.GetLongLength(0); column++)
                    {
                        for (int row = 0; row < entityLookupTable.GetLongLength(1); row++)
                        {
                            //Check for valid cell
                            if (entityLookupTable[column, row, i] >= 0L)
                            {
                                entityLookupTable[column, row, i] = Math.Max(0L, entityLookupTable[column, row, i] + movementTable[column, row, i]);
                            }
                        }
                    }
                }
            }
        }

        // Helper method to get valid neighbor coordinates, with zone transition filtering
        private List<Vector2Int> GetValidNeighbors(EntityManager entityManager, long[,,] entityLookupTable, int column, int row, int entityIndex, Entity entity = null, int currentZone = 0)
        {
            List<Vector2Int> validNeighbors = new List<Vector2Int>();

            for (int x = -1; x < 2; x++)
            {
                for (int y = -1; y < 2; y++)
                {
                    int xPos = column + x;
                    int yPos = row + y;

                    // Skip the center cell and check boundaries
                    if ((x == 0 && y == 0) ||
                        xPos < 0 || xPos >= entityLookupTable.GetLongLength(0) ||
                        yPos < 0 || yPos >= entityLookupTable.GetLongLength(1))
                    {
                        continue;
                    }

                    // Check if the target cell is valid for this entity
                    if (entityLookupTable[xPos, yPos, entityIndex] >= 0L)
                    {
                        // Check zone transition rules
                        if (entity != null && entity.ZoneInformation != null)
                        {
                            int neighbourZone = entityManager.GetZoneType(xPos, yPos);

                            bool canTransition = entity.ZoneInformation.Any(x => x.ZoneID == currentZone && x.Transitions.Contains(neighbourZone));
                            if (!canTransition)
                            {
                                continue;
                            }
                        }

                        validNeighbors.Add(new Vector2Int(xPos, yPos));
                    }
                }
            }

            return validNeighbors;
        }
    }
}
