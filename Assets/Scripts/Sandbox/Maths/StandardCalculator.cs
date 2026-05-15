using System.Linq;
using System.Collections.Generic;
using Glitchers.EcoKnow.Sandbox.Grid;
using Glitchers.EcoKnow.Sandbox.Grid.Regions;
using UnityEngine;

namespace Glitchers.EcoKnow.Sandbox
{
    public class StandardCalculator : IEntityCalculator
    {
        public string Name() => "Standard";
        public string Version() => "1.0";

        public void CalculatePopulations(EntityManager entityManager, int[,,] entityLookupTable)
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
                    // Region-wide compute: skip visual cells (only the region's compute cell ticks).
                    if (!entityManager.ShouldComputeCell(column, row))
                    {
                        continue;
                    }

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

                    float[] r = entityManager.GetEntityTypeList().Select(x => x.ZoneInformation != null && x.ZoneInformation.FirstOrDefault(z => z.ZoneID == zone) != null ? x.ZoneInformation.FirstOrDefault(z => z.ZoneID == zone).GrowthRate : x.GrowthRate).ToArray();
                    float[] N = entityList.Select(x => (float)x.Population).ToArray();

                    //AN
                    float[] AN = new float[entityManager.EntityTypeCount];
                    for (int yy = 0; yy < A.GetLongLength(1); yy++)
                    {
                        float result = 0f;
                        for (int xx = 0; xx < A.GetLongLength(0); xx++)
                        {
                            result += A[xx, yy] * N[xx];
                            //Debug.Log(A[xx, yy]);
                        }

                        AN[yy] = result;
                    }

                    //N + N.(r + AN)
                    float[] NNrAN = new float[entityManager.EntityTypeCount];
                    for (int a = 0; a < N.Length; a++)
                    {
                        NNrAN[a] = N[a] + (N[a] * (r[a] + AN[a]));
                        //Debug.Log(NNrAN[a]);
                    }

                    //Update entity numbers
                    for (int b = 0; b < NNrAN.Length; b++)
                    {
                        entityLookupTable[column, row, b] = Mathf.Max(0, Mathf.FloorToInt(NNrAN[b]));
                    }
                }
            }
        }

        public void CalculateMovement(EntityManager entityManager, int[,,] entityLookupTable)
        {
            if (entityManager == null)
            {
                Debug.LogError($"[{Name()} Calculator] EntityManager is null! Aborting calculations...");
                return;
            }

            // Region-wide compute: replace per-cell 8-neighbour movement with region-to-region hops.
            // Empty RegionMovementPolicy table = no movement (default). Specific flows (e.g. water
            // pollution across Freshwater/Estuary/Seawater/Overflow) are enabled by populating the
            // policy table during scenario setup.
            if (entityManager.IsRegionMode)
            {
                RegionMovement.Run(entityManager, entityManager.RegionComputeManager);
                return;
            }

            //We want to calculate the entity count change in each cell, THEN apply that difference to the entire grid
            //Perform this per-entity, following the maths provided
            for (int i = 0; i < entityManager.EntityTypeCount; i++)
            {
                Entity entity = entityManager.GetEntityTypeList()[i];

                if (entity.MovementRate > 0f || (entity.ZoneInformation != null && entity.ZoneInformation.Any(x => x.MovementRate > 0f)))
                {
                    int[,,] movementTable = new int[entityLookupTable.GetLongLength(0), entityLookupTable.GetLongLength(1), entityLookupTable.GetLongLength(2)];

                    //Move our requested entity in each cell
                    for (int column = 0; column < entityLookupTable.GetLongLength(0); column++)
                    {
                        for (int row = 0; row < entityLookupTable.GetLongLength(1); row++)
                        {
                            int currentPopulation = entityLookupTable[column, row, i];
                            if (currentPopulation < 0)
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

                            if (neighbouringCellCount == 0)
                            {
                                continue;
                            }

                            // 1. Sample number of movers from Binomial distribution
                            int entitiesToMove = SampleBinomial(currentPopulation, movementRate);

                            if (entitiesToMove > 0)
                            {
                                // 2. Distribute movers among neighbors using equal probabilities
                                int[] moversPerNeighbor = SampleMultinomial(entitiesToMove, neighbouringCellCount);

                                // 3. Apply movement to movementTable
                                movementTable[column, row, i] -= entitiesToMove;

                                for (int neighborIndex = 0; neighborIndex < neighbouringCellCount; neighborIndex++)
                                {
                                    Vector2Int neighbor = validNeighbors[neighborIndex];
                                    movementTable[neighbor.x, neighbor.y, i] += moversPerNeighbor[neighborIndex];
                                }
                            }
                        }
                    }

                    //Apply the movementTable numbers to our actual cells
                    for (int column = 0; column < entityLookupTable.GetLongLength(0); column++)
                    {
                        for (int row = 0; row < entityLookupTable.GetLongLength(1); row++)
                        {
                            //Check for valid cell
                            if (entityLookupTable[column, row, i] >= 0)
                            {
                                entityLookupTable[column, row, i] = Mathf.Max(0, entityLookupTable[column, row, i] + movementTable[column, row, i]);
                            }
                        }
                    }
                }
            }
        }

        // Helper method to get valid neighbor coordinates, with zone transition filtering
        private List<Vector2Int> GetValidNeighbors(EntityManager entityManager, int[,,] entityLookupTable, int column, int row, int entityIndex, Entity entity = null, int currentZone = 0)
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
                    if (entityLookupTable[xPos, yPos, entityIndex] >= 0)
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

        // Binomial sampling with n=1000 threshold compromise
        private int SampleBinomial(int n, float p)
        {
            if (n <= 0 || p <= 0) return 0;
            if (p >= 1) return n;
            
            // Use normal approximation for large populations (n >= 1000)
            if (n >= 1000)
            {
                float mean = n * p;
                float stdDev = Mathf.Sqrt(n * p * (1 - p));
                float sample = SampleNormal(mean, stdDev);
                return Mathf.Clamp(Mathf.RoundToInt(sample), 0, n);
            }
            // Use exact binomial sampling for small populations (n < 1000)
            else
            {
                int successes = 0;
                for (int i = 0; i < n; i++)
                {
                    if (Random.Range(0f, 1f) <= p)
                    {
                        successes++;
                    }
                }
                return successes;
            }
        }

        // Normal distribution sampling using Box-Muller transform
        private float SampleNormal(float mean, float stdDev)
        {
            float u1 = 1.0f - Random.Range(0f, 1f);
            float u2 = 1.0f - Random.Range(0f, 1f);
            float randStdNormal = Mathf.Sqrt(-2.0f * Mathf.Log(u1)) * Mathf.Sin(2.0f * Mathf.PI * u2);
            return mean + stdDev * randStdNormal;
        }

        // Multinomial sampling for distributing movers among neighbors
        private int[] SampleMultinomial(int totalMovers, int numberOfCategories)
        {
            int[] results = new int[numberOfCategories];
            
            if (numberOfCategories == 1)
            {
                results[0] = totalMovers;
                return results;
            }

            // Distribute movers one by one to random neighbors
            for (int i = 0; i < totalMovers; i++)
            {
                int chosenNeighbor = Random.Range(0, numberOfCategories);
                results[chosenNeighbor]++;
            }

            return results;
        }
    }
}
