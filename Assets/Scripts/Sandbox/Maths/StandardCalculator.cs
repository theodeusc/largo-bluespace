using System;
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

                    double[] r = entityManager.GetEntityTypeList().Select(x => (double)(x.ZoneInformation != null && x.ZoneInformation.FirstOrDefault(z => z.ZoneID == zone) != null ? x.ZoneInformation.FirstOrDefault(z => z.ZoneID == zone).GrowthRate : x.GrowthRate)).ToArray();
                    // Read N directly from the long lookup table (not CellEntity.Population)
                    // so values above int.MaxValue (pollutants) survive into the math.
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
                        if (double.IsNaN(v) || v <= 0.0)
                        {
                            entityLookupTable[column, row, b] = 0L;
                        }
                        else if (v >= (double)long.MaxValue)
                        {
                            entityLookupTable[column, row, b] = long.MaxValue;
                        }
                        else
                        {
                            entityLookupTable[column, row, b] = (long)Math.Floor(v);
                        }
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

                            if (neighbouringCellCount == 0)
                            {
                                continue;
                            }

                            // 1. Sample number of movers from Binomial distribution
                            long entitiesToMove = SampleBinomial(currentPopulation, movementRate);

                            if (entitiesToMove > 0L)
                            {
                                // 2. Distribute movers among neighbors using equal probabilities
                                long[] moversPerNeighbor = SampleMultinomial(entitiesToMove, neighbouringCellCount);

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

        // Binomial sampling with n=1000 threshold compromise.
        // Operates in double for the normal-approximation branch so populations up to ~1e15
        // (pollutant counts) sample without catastrophic float-precision loss.
        private long SampleBinomial(long n, float p)
        {
            if (n <= 0L || p <= 0f) return 0L;
            if (p >= 1f) return n;

            // Use normal approximation for large populations (n >= 1000)
            if (n >= 1000L)
            {
                double mean = (double)n * p;
                double stdDev = Math.Sqrt((double)n * p * (1.0 - p));
                double sample = SampleNormal(mean, stdDev);
                if (sample <= 0.0) return 0L;
                if (sample >= (double)n) return n;
                return (long)Math.Round(sample);
            }
            // Use exact binomial sampling for small populations (n < 1000)
            else
            {
                int successes = 0;
                int small = (int)n;
                for (int i = 0; i < small; i++)
                {
                    if (UnityEngine.Random.Range(0f, 1f) <= p)
                    {
                        successes++;
                    }
                }
                return successes;
            }
        }

        // Normal distribution sampling using Box-Muller transform
        private double SampleNormal(double mean, double stdDev)
        {
            double u1 = 1.0 - UnityEngine.Random.Range(0f, 1f);
            double u2 = 1.0 - UnityEngine.Random.Range(0f, 1f);
            double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
            return mean + stdDev * randStdNormal;
        }

        // Multinomial sampling for distributing movers among neighbors.
        // For huge totals (e.g. pollutants ~1e14) the per-mover loop would freeze the game,
        // so we switch to a deterministic proportional split above a safety threshold.
        private const long PerMoverLoopThreshold = 1_000_000L;
        private long[] SampleMultinomial(long totalMovers, int numberOfCategories)
        {
            long[] results = new long[numberOfCategories];

            if (numberOfCategories == 1)
            {
                results[0] = totalMovers;
                return results;
            }

            if (totalMovers > PerMoverLoopThreshold)
            {
                long perCat = totalMovers / numberOfCategories;
                long remainder = totalMovers - (perCat * numberOfCategories);
                for (int i = 0; i < numberOfCategories; i++) results[i] = perCat;
                results[0] += remainder;
                return results;
            }

            // Distribute movers one by one to random neighbors
            int loopCount = (int)totalMovers;
            for (int i = 0; i < loopCount; i++)
            {
                int chosenNeighbor = UnityEngine.Random.Range(0, numberOfCategories);
                results[chosenNeighbor]++;
            }

            return results;
        }
    }
}
