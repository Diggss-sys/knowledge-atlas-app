using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace RoomGen.Contracts
{
    [Serializable]
    public sealed class RoomSpec
    {
        public const string CurrentVersion = "1.0";

        [JsonProperty("spec_version")] public string SpecVersion = CurrentVersion;
        [JsonProperty("room_id")] public string RoomId = "dining-room";
        [JsonProperty("seed")] public int Seed = 160;
        [JsonProperty("geometry")] public GeometrySpec Geometry = new GeometrySpec();
        [JsonProperty("surfaces")] public SurfaceSpec Surfaces = new SurfaceSpec();
        [JsonProperty("openings")] public List<OpeningSpec> Openings = new List<OpeningSpec>();
        [JsonProperty("lighting")] public LightingSpec Lighting = new LightingSpec();
        [JsonProperty("furniture")] public List<FurniturePlacementSpec> Furniture = new List<FurniturePlacementSpec>();
        [JsonProperty("provenance")] public ProvenanceSpec Provenance = new ProvenanceSpec();
    }

    [Serializable]
    public sealed class GeometrySpec
    {
        [JsonProperty("width_m")] public float WidthM = 5.4f;
        [JsonProperty("length_m")] public float LengthM = 6.2f;
        [JsonProperty("ceiling_height_m")] public float CeilingHeightM = 2.4f;
        [JsonProperty("wall_thickness_m")] public float WallThicknessM = 0.15f;
        [JsonProperty("corner_radius_m")] public float CornerRadiusM;
    }

    [Serializable]
    public sealed class SurfaceSpec
    {
        [JsonProperty("floor_material_id")] public string FloorMaterialId = "builtin.oak";
        [JsonProperty("wall_material_id")] public string WallMaterialId = "builtin.warm-white";
        [JsonProperty("ceiling_material_id")] public string CeilingMaterialId = "builtin.ceiling-white";
        [JsonProperty("texture_scale_m")] public float TextureScaleM = 1f;
    }

    [Serializable]
    public sealed class OpeningSpec
    {
        [JsonProperty("opening_id")] public string OpeningId = "";
        [JsonProperty("kind")] public string Kind = "window";
        [JsonProperty("wall")] public string Wall = "left";
        [JsonProperty("center_m")] public float CenterM;
        [JsonProperty("width_m")] public float WidthM = 1.2f;
        [JsonProperty("bottom_m")] public float BottomM = 0.9f;
        [JsonProperty("top_m")] public float TopM = 2f;
    }

    [Serializable]
    public sealed class LightingSpec
    {
        [JsonProperty("preset_id")] public string PresetId = "builtin.recessed-neutral";
        [JsonProperty("target_lux")] public float TargetLux = 300f;
        [JsonProperty("tolerance_lux")] public float ToleranceLux = 35f;
        [JsonProperty("color_temperature_k")] public float ColorTemperatureK = 3500f;
        [JsonProperty("base_luminous_flux_lm")] public float BaseLuminousFluxLm = 3200f;
        [JsonProperty("fixed_exposure_ev100")] public float FixedExposureEv100 = 8f;
    }

    [Serializable]
    public sealed class FurniturePlacementSpec
    {
        [JsonProperty("asset_id")] public string AssetId = "";
        [JsonProperty("slot_id")] public string SlotId = "";
        [JsonProperty("position_m")] public Vec3 PositionM = new Vec3();
        [JsonProperty("rotation_y_deg")] public float RotationYDeg;
        [JsonProperty("footprint_m")] public Vec2 FootprintM = new Vec2(1f, 1f);
    }

    [Serializable]
    public sealed class ProvenanceSpec
    {
        [JsonProperty("preset_id")] public string PresetId = "dining-room-v1";
        [JsonProperty("generator_version")] public string GeneratorVersion = "1.0.0";
        [JsonProperty("source")] public string Source = "room-studio";
    }

    [Serializable]
    public sealed class Vec2
    {
        [JsonProperty("x")] public float X;
        [JsonProperty("y")] public float Y;

        public Vec2() { }
        public Vec2(float x, float y) { X = x; Y = y; }
    }

    [Serializable]
    public sealed class Vec3
    {
        [JsonProperty("x")] public float X;
        [JsonProperty("y")] public float Y;
        [JsonProperty("z")] public float Z;

        public Vec3() { }
        public Vec3(float x, float y, float z) { X = x; Y = y; Z = z; }
    }
}
