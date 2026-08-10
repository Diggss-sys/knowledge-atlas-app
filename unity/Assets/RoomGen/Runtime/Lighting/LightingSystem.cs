using System.Linq;
using RoomGen.Contracts;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace RoomGen.Lighting
{
    public static class LightingSystem
    {
        public static LightingResult Build(RoomSpec spec, Transform parent, int layer)
        {
            var result = LightingCalibrator.MatchTargetLux(spec);
            var root = new GameObject("04 Lighting");
            root.layer = layer;
            root.transform.SetParent(parent, false);

            var positions = new[]
            {
                new Vector3(-spec.Geometry.WidthM * 0.23f, spec.Geometry.CeilingHeightM - 0.08f, -spec.Geometry.LengthM * 0.23f),
                new Vector3( spec.Geometry.WidthM * 0.23f, spec.Geometry.CeilingHeightM - 0.08f, -spec.Geometry.LengthM * 0.23f),
                new Vector3(-spec.Geometry.WidthM * 0.23f, spec.Geometry.CeilingHeightM - 0.08f,  spec.Geometry.LengthM * 0.23f),
                new Vector3( spec.Geometry.WidthM * 0.23f, spec.Geometry.CeilingHeightM - 0.08f,  spec.Geometry.LengthM * 0.23f)
            };

            // If the room declares a pendant luminaire it carries part of the flux; the recessed
            // spots give up that share so total emitted flux stays BaseLuminousFluxLm (the
            // matched-luminance mechanism). PendantFraction is 0 when there is no pendant.
            var hasPendant = spec.Furniture.Any(f => f.AssetId == "builtin.pendant-light");
            var pendantFraction = hasPendant ? PendantFluxFraction : 0f;
            var spotFraction = 1f - pendantFraction;

            foreach (var position in positions)
            {
                var fixture = new GameObject("Recessed Fixture");
                fixture.layer = layer;
                fixture.transform.SetParent(root.transform, false);
                fixture.transform.localPosition = position;
                fixture.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                var light = fixture.AddComponent<Light>();
                var lightHd = fixture.AddComponent<HDAdditionalLightData>();
                // Contact shadows (per-light opt-in for the QualityRig volume): grounds furniture
                // exactly where legs meet the floor — shadowmaps blur out at that scale.
                lightHd.useContactShadow.useOverride = true;
                lightHd.useContactShadow.@override = true;
                light.type = LightType.Spot;
                light.range = Mathf.Max(spec.Geometry.WidthM, spec.Geometry.LengthM) * 1.25f;
                light.spotAngle = 80f;
                light.innerSpotAngle = 62f;
                light.shadows = LightShadows.Soft;
                light.useColorTemperature = true;
                light.colorTemperature = spec.Lighting.ColorTemperatureK;
                light.cullingMask = 1 << layer;
                light.lightUnit = LightUnit.Lumen;
                var lumens = spec.Lighting.BaseLuminousFluxLm * spotFraction * result.IntensityScale / positions.Length;
                light.intensity = LightUnitUtils.ConvertIntensity(
                    light, lumens, LightUnit.Lumen, LightUnit.Candela);
            }

            AddSun(root.transform, spec, result, layer);
            if (hasPendant)
                AddPendantLuminaire(parent, spec, result, layer);

            var volumeObject = new GameObject("Fixed Exposure");
            volumeObject.layer = layer;
            volumeObject.transform.SetParent(root.transform, false);
            var volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 50f;
            volume.profile = ScriptableObject.CreateInstance<VolumeProfile>();
            volume.profile.hideFlags = HideFlags.DontSave;
            volumeObject.AddComponent<RuntimeAssetOwner>().Asset = volume.profile;
            var exposure = volume.profile.Add<Exposure>(true);
            exposure.mode.Override(ExposureMode.Fixed);
            exposure.fixedExposure.Override(spec.Lighting.FixedExposureEv100);

            foreach (var probe in result.Probes)
            {
                var marker = new GameObject("Probe " + probe.ProbeId);
                marker.layer = layer;
                marker.transform.SetParent(root.transform, false);
                marker.transform.localPosition = new Vector3(0f, probe.HeightM, 0f);
            }

            return result;
        }

        // Fraction of the room's luminous flux carried by the pendant (the rest goes to the
        // recessed spots); total emitted flux is unchanged so the calibration target still holds.
        // Public so LightingCalibrator models the same split (keeps prediction honest).
        public const float PendantFluxFraction = 0.4f;
        // EXTERIOR daylight illuminance, as a multiple of the room's INTERIOR target lux. These are
        // two different worlds: a room lit to 300 lux sits under a several-thousand-lux sky. The old
        // 2.5x put the sun at 750 lux — dead twilight — so windows read as flat pale panels, threw
        // no floor patch, and the walls only ever got GI crumbs (the "Roblox" tell). 20x overshot the
        // other way: HDRP's PhysicallyBasedSky derives its visible RADIANCE from this same light, so
        // the sky dome (seen directly through the glazing, and doubled by SSR.reflectSky bouncing it
        // off every surface) blew out and read as a saturated "heaven" glow at the fixed EV100 —
        // exposure is locked (CLAUDE.md: fix physics, don't mask with exposure), so this constant is
        // the one legal knob. 12x sits lower in the real daylight range (heavy-overcast to bright-sun
        // spans roughly 2,000-25,000 lux) while staying honest daylight, not the old 750-lux dusk: a
        // 70-lux dim preset gets a ~840 lux sky, a 300-lux room a ~3,600 lux day, a 500-lux bright one
        // ~6,000. Tune here first if the look needs pushing either direction.
        // The room does NOT get brighter overall — LightingCalibrator subtracts the daylight that
        // reaches the working plane and the fixtures fill only the remainder, so total illuminance
        // still lands on target and the FIXED exposure stays correct. What changes is where the
        // light COMES FROM (windows, with direction) instead of how much there is.
        // Public: SunSkySystem's time-of-day arc and the calibrator both read this one constant.
        public const float SunTargetLuxFactor = 12f;
        // The calibrated static sun: high-left angle for a daylight patch + soft shadows, and the
        // daylight-blend that tints it cooler than the interior lamps. SunSkySystem.Reset restores
        // exactly these, so they live here (change once, both agree) rather than being hand-copied.
        public static readonly Vector3 CalibratedSunEuler = new Vector3(52f, 35f, 0f);
        public const float SunDaylightKelvin = 6500f;
        public const float SunDaylightBlend = 0.5f;
        // Absolute emissive brightness (nits) of the pendant shade, independent of exposure.
        const float PendantEmissiveNits = 12f;

        static void AddSun(Transform root, RoomSpec spec, LightingResult result, int layer)
        {
            var sunObject = new GameObject("Sun");
            sunObject.layer = layer;
            sunObject.transform.SetParent(root, false);
            // Angled from high-left so a window/door admits a daylight patch and casts soft shadows.
            sunObject.transform.localRotation = Quaternion.Euler(CalibratedSunEuler);
            var sun = sunObject.AddComponent<Light>();
            var sunHd = sunObject.AddComponent<HDAdditionalLightData>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            // Drive the Physically Based Sky: this directional light becomes the atmosphere's sun,
            // rendering a real sun disk (~0.5° like the real sun) and scattering daylight through the
            // windows. Without interactsWithSky the PBR sky has no sun and openings read dead.
            sunHd.interactsWithSky = true;
            sunHd.angularDiameter = 0.5f;
            // Ray-traced sun shadows (cameras with the RayTracing frame setting): the 0.5°
            // angular diameter becomes a real penumbra — crisp at contact, softer with distance,
            // the single strongest "this light is real" cue on the window patches.
            sunHd.useRayTracedShadows = true;
            // Sun contact shadows: crisp grounding at the base of furniture inside the window
            // light patches, where the eye checks realism hardest.
            sunHd.useContactShadow.useOverride = true;
            sunHd.useContactShadow.@override = true;
            sun.useColorTemperature = true;
            // Cooler than the interior lamps: daylight reads distinct from the warm fixtures.
            // COUPLED-VARIABLE NOTE: derived from lighting.color_temperature_k, so a warmth
            // manipulation also shifts the sun's tint (same class of coupling as pendant-Y from
            // ceiling height — deterministic function of the declared variable, both conditions
            // use the same formula; document in analysis, do not "fix").
            sun.colorTemperature = Mathf.Lerp(spec.Lighting.ColorTemperatureK, SunDaylightKelvin, SunDaylightBlend);
            sun.cullingMask = 1 << layer;
            sun.lightUnit = LightUnit.Lux;
            // Scales with target lux so a dim preset gets a dim sky and a bright one a brighter sky,
            // without overpowering the calibrated interior.
            // EXTERIOR daylight, not the interior target — see SunTargetLuxFactor. The calibrator
            // owns this number so the sun, the time-of-day arc, and the daylight the fixtures
            // harvest against can never disagree.
            sun.intensity = LightingCalibrator.ExteriorDaylightLux(spec);
        }

        static void AddPendantLuminaire(Transform parent, RoomSpec spec, LightingResult result, int layer)
        {
            Transform pendant = null;
            foreach (var t in parent.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.Contains("builtin.pendant-light")) { pendant = t; break; }
            }
            if (pendant == null) return;

            var warm = Mathf.CorrelatedColorTemperatureToRGB(spec.Lighting.ColorTemperatureK);

            // Emissive shade so the fixture itself visibly glows (and feeds bloom).
            var shade = pendant.Find("Shade");
            if (shade != null && shade.TryGetComponent<MeshRenderer>(out var renderer))
            {
                var emissive = new Material(Shader.Find("HDRP/Lit")) { name = "Pendant Emissive" };
                emissive.SetColor("_BaseColor", warm);
                emissive.SetColor("_EmissiveColor", warm * PendantEmissiveNits);
                emissive.SetFloat("_EmissiveExposureWeight", 0f);
                renderer.sharedMaterial = emissive;
                pendant.gameObject.AddComponent<RuntimeAssetOwner>().Asset = emissive;
            }

            // Point light just below the shade carrying the pendant's flux share.
            var lightObject = new GameObject("Pendant Light");
            lightObject.layer = layer;
            lightObject.transform.SetParent(pendant, false);
            lightObject.transform.localPosition = new Vector3(0f, -0.7f, 0f);
            var light = lightObject.AddComponent<Light>();
            var pendantHd = lightObject.AddComponent<HDAdditionalLightData>();
            // Pendant hangs directly over the table: contact shadows ground the tabletop clutter
            // and the table's own base under its strongest light.
            pendantHd.useContactShadow.useOverride = true;
            pendantHd.useContactShadow.@override = true;
            light.type = LightType.Point;
            light.range = Mathf.Max(spec.Geometry.WidthM, spec.Geometry.LengthM) * 1.5f;
            light.shadows = LightShadows.Soft;
            light.useColorTemperature = true;
            light.colorTemperature = spec.Lighting.ColorTemperatureK;
            light.cullingMask = 1 << layer;
            light.lightUnit = LightUnit.Lumen;
            var lumens = spec.Lighting.BaseLuminousFluxLm * PendantFluxFraction * result.IntensityScale;
            light.intensity = LightUnitUtils.ConvertIntensity(light, lumens, LightUnit.Lumen, LightUnit.Candela);
        }
    }

    sealed class RuntimeAssetOwner : MonoBehaviour
    {
        public Object Asset;

        void OnDestroy()
        {
            if (Asset == null) return;
            if (Application.isPlaying) Destroy(Asset);
            else DestroyImmediate(Asset);
        }
    }
}
