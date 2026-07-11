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
                fixture.AddComponent<HDAdditionalLightData>();
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
        // Daylight sun strength as a multiple of the room's target lux — gentle, scales with mood.
        const float SunTargetLuxFactor = 2.5f;
        // Absolute emissive brightness (nits) of the pendant shade, independent of exposure.
        const float PendantEmissiveNits = 12f;

        static void AddSun(Transform root, RoomSpec spec, LightingResult result, int layer)
        {
            var sunObject = new GameObject("Sun");
            sunObject.layer = layer;
            sunObject.transform.SetParent(root, false);
            // Angled from high-left so a window/door admits a daylight patch and casts soft shadows.
            sunObject.transform.localRotation = Quaternion.Euler(52f, 35f, 0f);
            var sun = sunObject.AddComponent<Light>();
            var sunHd = sunObject.AddComponent<HDAdditionalLightData>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            // Drive the Physically Based Sky: this directional light becomes the atmosphere's sun,
            // rendering a real sun disk (~0.5° like the real sun) and scattering daylight through the
            // windows. Without interactsWithSky the PBR sky has no sun and openings read dead.
            sunHd.interactsWithSky = true;
            sunHd.angularDiameter = 0.5f;
            sun.useColorTemperature = true;
            // Cooler than the interior lamps: daylight reads distinct from the warm fixtures.
            // COUPLED-VARIABLE NOTE: derived from lighting.color_temperature_k, so a warmth
            // manipulation also shifts the sun's tint (same class of coupling as pendant-Y from
            // ceiling height — deterministic function of the declared variable, both conditions
            // use the same formula; document in analysis, do not "fix").
            sun.colorTemperature = Mathf.Lerp(spec.Lighting.ColorTemperatureK, 6500f, 0.5f);
            sun.cullingMask = 1 << layer;
            sun.lightUnit = LightUnit.Lux;
            // Scales with target lux so a dim preset gets a dim sky and a bright one a brighter sky,
            // without overpowering the calibrated interior.
            sun.intensity = spec.Lighting.TargetLux * SunTargetLuxFactor;
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
            lightObject.AddComponent<HDAdditionalLightData>();
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
