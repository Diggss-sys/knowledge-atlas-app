using System.Linq;
using RoomGen.Contracts;
using UnityEngine;

namespace RoomGen.Lighting
{
    public static class LightingCalibrator
    {
        const float Utilization = 0.58f;
        const float UniformFraction = 0.82f;

        public static LightingResult MatchTargetLux(RoomSpec spec)
        {
            var probes = new[]
            {
                new ProbeDefinition("table-center", 0.76f, Vector2.zero),
                new ProbeDefinition("eye-center", 1.2f, Vector2.zero),
                new ProbeDefinition("table-left", 0.76f, new Vector2(-1.1f, 0f)),
                new ProbeDefinition("table-right", 0.76f, new Vector2(1.1f, 0f))
            };

            var unscaled = probes.Select(probe => EstimateLux(spec, probe)).ToArray();
            var mean = Mathf.Max(0.001f, unscaled.Average());
            var scale = spec.Lighting.TargetLux / mean;
            var result = new LightingResult
            {
                TargetLux = spec.Lighting.TargetLux,
                ToleranceLux = spec.Lighting.ToleranceLux,
                IntensityScale = scale
            };

            for (var i = 0; i < probes.Length; i++)
            {
                result.Probes.Add(new ProbeReading
                {
                    ProbeId = probes[i].Id,
                    HeightM = probes[i].Height,
                    Lux = unscaled[i] * scale
                });
            }

            result.MeanLux = result.Probes.Average(p => p.Lux);
            result.MaximumErrorLux = result.Probes.Max(p => Mathf.Abs(p.Lux - result.TargetLux));
            result.Ok = result.MaximumErrorLux <= result.ToleranceLux;
            return result;
        }

        static float EstimateLux(RoomSpec spec, ProbeDefinition probe)
        {
            var area = Mathf.Max(1f, spec.Geometry.WidthM * spec.Geometry.LengthM);
            var flux = Mathf.Max(1f, spec.Lighting.BaseLuminousFluxLm);
            var uniform = flux * Utilization / area;
            var fixtureY = spec.Geometry.CeilingHeightM - 0.08f;
            var fixturePositions = new[]
            {
                new Vector2(-spec.Geometry.WidthM * 0.23f, -spec.Geometry.LengthM * 0.23f),
                new Vector2( spec.Geometry.WidthM * 0.23f, -spec.Geometry.LengthM * 0.23f),
                new Vector2(-spec.Geometry.WidthM * 0.23f,  spec.Geometry.LengthM * 0.23f),
                new Vector2( spec.Geometry.WidthM * 0.23f,  spec.Geometry.LengthM * 0.23f)
            };

            var direct = 0f;
            foreach (var fixture in fixturePositions)
            {
                var horizontal = Vector2.Distance(fixture, probe.Position);
                var vertical = Mathf.Max(0.2f, fixtureY - probe.Height);
                var distanceSquared = horizontal * horizontal + vertical * vertical;
                var cosine = vertical / Mathf.Sqrt(distanceSquared);
                var candela = flux / fixturePositions.Length / (4f * Mathf.PI);
                direct += candela * cosine / distanceSquared;
            }

            return uniform * UniformFraction + direct * (1f - UniformFraction);
        }

        readonly struct ProbeDefinition
        {
            public readonly string Id;
            public readonly float Height;
            public readonly Vector2 Position;

            public ProbeDefinition(string id, float height, Vector2 position)
            {
                Id = id;
                Height = height;
                Position = position;
            }
        }
    }
}
