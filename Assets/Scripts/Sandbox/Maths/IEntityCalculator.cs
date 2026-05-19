using UnityEngine;

namespace Glitchers.EcoKnow.Sandbox
{
    public interface IEntityCalculator
    {
        public string Name();
        public string Version();

        public void CalculatePopulations(EntityManager entityManager, long[,,] entityLookupTable);
        public void CalculateMovement(EntityManager entityManager, long[,,] entityLookupTable);
    }
}
