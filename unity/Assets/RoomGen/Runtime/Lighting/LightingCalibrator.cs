using System;
using System.Linq;
using RoomGen.Contracts;
using UnityEngine;

namespace RoomGen.Lighting
{
    public static class LightingCalibrator
    {
        const float Utilization = 0.58f;
        const float UniformFraction = 0.82f;
        // Clear double glazing passes ~75% of visible light.
        const float GlassTransmittance = 0.75f;
        // Lumped geometry/absorption term: the share of transmitted daylight that actually reaches
        // the working plane. Together with the glazing:floor ratio this is the classic average
        // daylight factor — for the dining room (~3.6 m² glazing, 33 m² floor) it lands near 2.8%,
        // which is a normal side-lit room.
        const float DaylightUtilization = 0.35f;
        // Ceiling on how much of the fixtures' output daylight is allowed to replace. The daylight
        // estimate assumes that light actually REACHES the working plane, which needs full GI
        // transport; on the raster path (the study path — handoffs/RENDER_PATH_NOTE.md) that
        // transport is weak, so an unbounded harvest would systematically UNDER-light every
        // windowed room. Bounded, the worst case is a modest dim instead of a dark room, and a
        // heavily glazed room can never switch its fixtures off on the strength of an estimate.
        const float MaxDaylightHarvest = 0.35f;

        /// <summary>
        /// Exterior daylight illuminance the sun is driven at. One source of truth shared with
        /// LightingSystem.AddSun and SunSkySystem's time-of-day arc.
        /// </summary>
        public static float ExteriorDaylightLux(RoomSpec spec) =>
            spec.Lighting.TargetLux * LightingSystem.SunTargetLuxFactor;

        /// <summary>
        /// Average daylight illuminance arriving on the working plane through the windows
        /// (average-daylight-factor method): E_in = E_out x transmittance x utilization x
        /// (glazing area / floor area). A windowless room returns 0 and the fixtures carry
        /// everything, exactly as before.
        /// </summary>
        public static float DaylightLux(RoomSpec spec)
        {
            var floorArea = Mathf.Max(1f, spec.Geometry.WidthM * spec.Geometry.LengthM);
            var glazing = 0f;
            foreach (var opening in spec.Openings)
            {
                if (!string.Equals(opening.Kind, "window", StringComparison.OrdinalIgnoreCase)) continue;
                glazing += opening.WidthM * Mathf.Max(0f, opening.TopM - opening.BottomM);
            }
            if (glazing <= 0f) return 0f;
            return ExteriorDaylightLux(spec) * GlassTransmittance * DaylightUtilization
                   * (glazing / floorArea);
        }

        public static LightingResult MatchTargetLux(RoomSpec spec)
        {
            var probes = new[]
            {
                new ProbeDefinition("table-center", 0.76f, Vector2.zero),
                new ProbeDefinition("eye-center", 1.2f, Vector2.zero),
                new ProbeDefinition("table-left", 0.76f, new Vector2(-1.1f, 0f)),
                new ProbeDefinition("table-right", 0.76f, new Vector2(1.1f, 0f))
            };

            var hasPendant = spec.Furniture.Any(f => f.AssetId == "builtin.pendant-light");
            var unscaled = probes.Select(probe => EstimateLux(spec, probe, hasPendant)).ToArray();
            var mean = Mathf.Max(0.001f, unscaled.Average());
            // Daylight harvesting. The windows now deliver real daylight to the working plane, so the
            // fixtures fill ONLY the gap it leaves. Total illuminance still lands on TargetLux — which
            // both the matched-luminance guarantee and the FIXED exposure depend on — while the light
            // itself now arrives with direction through the glazing instead of flat from the ceiling.
            // Both conditions of a pair share the same glazing, so this cannot introduce a pair
            // difference on its own.
            var daylight = Mathf.Min(DaylightLux(spec),
                spec.Lighting.TargetLux * MaxDaylightHarvest);
            var scale = Mathf.Max(0f, spec.Lighting.TargetLux - daylight) / mean;
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
                    // Total = daylight through the glazing + the fixtures' scaled contribution.
                    Lux = daylight + unscaled[i] * scale
                });
            }

            result.MeanLux = result.Probes.Average(p => p.Lux);
            result.MaximumErrorLux = result.Probes.Max(p => Mathf.Abs(p.Lux - result.TargetLux));
            result.Ok = result.MaximumErrorLux <= result.ToleranceLux;
            return result;
        }

        static float EstimateLux(RoomSpec spec, ProbeDefinition probe, bool hasPendant)
        {
            var area = Mathf.Max(1f, spec.Geometry.WidthM * spec.Geometry.LengthM);
            var flux = Mathf.Max(1f, spec.Lighting.BaseLuminousFluxLm);
            var uniform = flux * Utilization / area; // bounce term uses the total (conserved) flux
            var fixtureY = spec.Geometry.CeilingHeightM - 0.08f;
            var fixturePositions = new[]
            {
                new Vector2(-spec.Geometry.WidthM * 0.23f, -spec.Geometry.LengthM * 0.23f),
                new Vector2( spec.Geometry.WidthM * 0.23f, -spec.Geometry.LengthM * 0.23f),
                new Vector2(-spec.Geometry.WidthM * 0.23f,  spec.Geometry.LengthM * 0.23f),
                new Vector2( spec.Geometry.WidthM * 0.23f,  spec.Geometry.LengthM * 0.23f)
            };

            // Flux is split between the recessed spots and (if present) the pendant, mirroring
            // LightingSystem exactly — otherwise the calibration target would silently drift when
            // the pendant carries part of the load.
            var pendantFraction = hasPendant ? LightingSystem.PendantFluxFraction : 0f;
            var spotFraction = 1f - pendantFraction;

            var direct = 0f;
            var spotCandela = flux * spotFraction / fixturePositions.Length / (4f * Mathf.PI);
            foreach (var fixture in fixturePositions)
            {
                var horizontal = Vector2.Distance(fixture, probe.Position);
                var vertical = Mathf.Max(0.2f, fixtureY - probe.Height);
                var distanceSquared = horizontal * horizontal + vertical * vertical;
                var cosine = vertical / Mathf.Sqrt(distanceSquared);
                direct += spotCandela * cosine / distanceSquared;
            }

            if (pendantFraction > 0f)
            {
                // Single downward point source hung over the table centre (0,0).
                var pendantY = spec.Geometry.CeilingHeightM - 0.6f;
                var horizontal = probe.Position.magnitude;
                var vertical = Mathf.Max(0.2f, pendantY - probe.Height);
                var distanceSquared = horizontal * horizontal + vertical * vertical;
                var cosine = vertical / Mathf.Sqrt(distanceSquared);
                var pendantCandela = flux * pendantFraction / (4f * Mathf.PI);
                direct += pendantCandela * cosine / distanceSquared;
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
