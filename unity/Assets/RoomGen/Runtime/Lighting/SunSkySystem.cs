using System.Collections.Generic;
using UnityEngine;

namespace RoomGen.Lighting
{
    /// <summary>
    /// Time-of-day daylight for the generated rooms (RENDERING_RESEARCH: interior realism depends
    /// on a believable exterior sun — angle, colour and strength must move together). This drives
    /// the "Sun" directional light that LightingSystem creates per room build:
    ///   - elevation/azimuth follow a simple solar arc (low east morning, high south midday,
    ///     low west evening),
    ///   - colour temperature runs warm near the horizon (~2600 K) to neutral daylight at the
    ///     peak (~5800 K), the strongest realism cue of the three,
    ///   - illuminance scales with sin(elevation), so a low sun lights the room gently and a
    ///     midday sun matches LightingSystem's calibrated baseline (target lux x its factor).
    ///
    /// OPT-IN by design: when no hour is applied (or after Reset) the sun keeps LightingSystem's
    /// static angle and calibrated intensity, so Paco's matched-luminance report stays valid.
    /// Purely visual, never written to the RoomSpec — both conditions of a pair receive the same
    /// hour, so it is a shared/nuisance parameter and can never create a pair difference.
    /// </summary>
    public static class SunSkySystem
    {
        public const float MinHour = 6f;    // dawn
        public const float MaxHour = 20f;   // dusk
        public const float PeakHour = 13f;  // solar noon (slightly after 12, like real local time)

        // LightingSystem.AddSun baseline: intensity = TargetLux * 2.5 at its fixed angle. The arc
        // reproduces that strength at the peak so midday matches the calibrated look.
        const float SunTargetLuxFactor = 2.5f;
        const float HorizonKelvin = 2600f;
        const float NoonKelvin = 5800f;
        const float MaxElevationDeg = 62f;  // midsummer temperate-latitude peak
        const float MinElevationDeg = 4f;   // just above horizon at the range ends

        /// <summary>
        /// Aim and tint every "Sun" light under <paramref name="root"/> for the given hour.
        /// <paramref name="targetLux"/> is the room's lighting target (spec.Lighting.TargetLux),
        /// used so intensity stays proportional to the calibrated baseline.
        /// </summary>
        public static void Apply(Transform root, float hour, float targetLux)
        {
            hour = Mathf.Clamp(hour, MinHour, MaxHour);

            // 0 at dawn/dusk ends, 1 at the peak — the sun's height along its arc.
            var t = 1f - Mathf.Abs(hour - PeakHour) / Mathf.Max(PeakHour - MinHour, MaxHour - PeakHour);
            t = Mathf.Clamp01(t);
            var elevation = Mathf.Lerp(MinElevationDeg, MaxElevationDeg, Mathf.Sin(t * Mathf.PI * 0.5f));

            // Azimuth sweeps ~east (morning) through south (midday) to ~west (evening).
            var azimuth = Mathf.Lerp(105f, -105f, Mathf.InverseLerp(MinHour, MaxHour, hour)) + 35f;

            // Warm at the horizon, neutral daylight at the top of the arc.
            var kelvin = Mathf.Lerp(HorizonKelvin, NoonKelvin, Mathf.Sin(t * Mathf.PI * 0.5f));

            // Peak matches LightingSystem's calibrated strength; low sun tapers toward a soft glow.
            var strength = Mathf.Max(0.06f, Mathf.Sin(elevation * Mathf.Deg2Rad));
            var lux = targetLux * SunTargetLuxFactor * strength;

            foreach (var sun in FindSuns(root))
            {
                sun.transform.localRotation = Quaternion.Euler(elevation, azimuth, 0f);
                sun.useColorTemperature = true;
                sun.colorTemperature = kelvin;
                sun.intensity = lux;
            }
        }

        /// <summary>
        /// Restore LightingSystem's static sun (its fixed angle, blend and calibrated intensity)
        /// so the default look — and the matched-luminance calibration — is exactly as built.
        /// Mirrors LightingSystem.AddSun; if that changes, keep these values in sync.
        /// </summary>
        public static void Reset(Transform root, float specColorTemperatureK, float targetLux)
        {
            foreach (var sun in FindSuns(root))
            {
                sun.transform.localRotation = Quaternion.Euler(52f, 35f, 0f);
                sun.useColorTemperature = true;
                sun.colorTemperature = Mathf.Lerp(specColorTemperatureK, 6500f, 0.5f);
                sun.intensity = targetLux * SunTargetLuxFactor;
            }
        }

        static List<Light> FindSuns(Transform root)
        {
            var suns = new List<Light>();
            if (root == null) return suns;
            foreach (var light in root.GetComponentsInChildren<Light>(true))
            {
                if (light.type == LightType.Directional && light.gameObject.name == "Sun")
                    suns.Add(light);
            }
            return suns;
        }
    }
}
