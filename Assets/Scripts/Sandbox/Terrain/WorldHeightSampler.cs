using UnityEngine;

namespace Glitchers.EcoKnow.Sandbox.Terrain
{
    /// <summary>
    /// Single Source of Truth for all seabed elevation parameters.
    /// Stateless w.r.t. world bounds — operates on continuous world coordinates
    /// so it's chunk-friendly for future infinite world support.
    /// </summary>
    public class WorldHeightSampler
    {
        public struct NoiseOctave
        {
            public float Frequency;
            public float Amplitude;
            public float OffsetX;
            public float OffsetY;

            public NoiseOctave(float frequency, float amplitude, float offsetX, float offsetY)
            {
                Frequency = frequency;
                Amplitude = amplitude;
                OffsetX = offsetX;
                OffsetY = offsetY;
            }
        }

        public const int OctaveCount = 3;

        // Default octaves: shelves, ridges, sediment
        private NoiseOctave[] _octaves;
        public NoiseOctave[] Octaves => _octaves;

        // Depth envelope: output = DepthBase + combined_noise * DepthAmplitude
        public float DepthBase { get; private set; }
        public float DepthAmplitude { get; private set; }

        // Smootherstep modulation center — distance norm below this is shallow
        public float DepthCenter { get; private set; }

        public WorldHeightSampler(int seed)
        {
            float seedOffsetX = (seed % 1000) * 0.7f;
            float seedOffsetY = (seed % 1000) * 1.3f;

            _octaves = new NoiseOctave[OctaveCount]
            {
                new NoiseOctave(0.25f, 0.55f, seedOffsetX,         seedOffsetY),           // Continental shelves
                new NoiseOctave(0.6f,  0.30f, seedOffsetX + 100f,  seedOffsetY + 100f),    // Mid-scale ridges
                new NoiseOctave(1.5f,  0.15f, seedOffsetX + 200f,  seedOffsetY + 200f),    // Fine sediment
            };

            DepthBase = 0.625f;
            DepthAmplitude = 0.375f;
            DepthCenter = 0.35f;
        }

        /// <summary>
        /// CPU-side depth query at continuous grid coordinates.
        /// Returns depth in [0, 1] range (0 = sea level, 1 = deepest).
        /// </summary>
        public float SampleDepth(float gridX, float gridY)
        {
            float combined = 0f;
            for (int i = 0; i < OctaveCount; i++)
            {
                ref NoiseOctave oct = ref _octaves[i];
                float n = Mathf.PerlinNoise(
                    gridX * oct.Frequency + oct.OffsetX,
                    gridY * oct.Frequency + oct.OffsetY
                );
                combined += n * oct.Amplitude;
            }

            // Map combined noise [0, 1] into depth envelope
            return Mathf.Clamp01(DepthBase + (combined - 0.5f) * DepthAmplitude * 2f);
        }

        /// <summary>
        /// Depth with shore-distance modulation. distNorm is [0,1] where
        /// 0 = at shore, 1 = maximum sea distance.
        /// </summary>
        public float SampleModulatedDepth(float gridX, float gridY, float distNorm)
        {
            float rawDepth = SampleDepth(gridX, gridY);

            // Smootherstep modulation — shallow near shore, full depth far out
            float t = Mathf.Clamp01(distNorm / Mathf.Max(DepthCenter * 2f, 0.01f));
            float modulation = t * t * t * (t * (t * 6f - 15f) + 10f);

            return rawDepth * modulation;
        }
    }
}
