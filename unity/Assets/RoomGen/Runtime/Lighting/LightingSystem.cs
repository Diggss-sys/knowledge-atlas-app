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
                var lumens = spec.Lighting.BaseLuminousFluxLm * result.IntensityScale / positions.Length;
                light.intensity = LightUnitUtils.ConvertIntensity(
                    light, lumens, LightUnit.Lumen, LightUnit.Candela);
            }

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
