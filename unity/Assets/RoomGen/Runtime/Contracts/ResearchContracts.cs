using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace RoomGen.Contracts
{
    [Serializable]
    public sealed class DiningRoomPreset
    {
        [JsonProperty("preset_version")] public string PresetVersion = "1.0";
        [JsonProperty("preset_id")] public string PresetId = "dining-room-v1";
        [JsonProperty("display_name")] public string DisplayName = "Dining Room";
        [JsonProperty("ceiling_height_range_m")] public RangeSpec CeilingHeightRangeM = new RangeSpec(2.2f, 3.8f);
        [JsonProperty("width_range_m")] public RangeSpec WidthRangeM = new RangeSpec(4.5f, 7f);
        [JsonProperty("length_range_m")] public RangeSpec LengthRangeM = new RangeSpec(5f, 8f);
        [JsonProperty("corner_radius_range_m")] public RangeSpec CornerRadiusRangeM = new RangeSpec(0f, 1.2f);
        [JsonProperty("material_ids")] public List<string> MaterialIds = new List<string>();
        [JsonProperty("lighting_preset_ids")] public List<string> LightingPresetIds = new List<string>();
        [JsonProperty("required_slots")] public List<string> RequiredSlots = new List<string>();
    }

    [Serializable]
    public sealed class RangeSpec
    {
        [JsonProperty("min")] public float Min;
        [JsonProperty("max")] public float Max;

        public RangeSpec() { }
        public RangeSpec(float min, float max) { Min = min; Max = max; }
        public bool Contains(float value) => value >= Min && value <= Max;
    }

    [Serializable]
    public sealed class ConditionPairSpec
    {
        [JsonProperty("pair_version")] public string PairVersion = "1.0";
        [JsonProperty("pair_id")] public string PairId = "ceiling-height-pair";
        [JsonProperty("manipulated_variable")] public string ManipulatedVariable = "geometry.ceiling_height_m";
        [JsonProperty("control")] public RoomSpec Control = new RoomSpec();
        [JsonProperty("treatment")] public RoomSpec Treatment = new RoomSpec();
    }

    [Serializable]
    public sealed class ValidationIssue
    {
        [JsonProperty("severity")] public string Severity;
        [JsonProperty("code")] public string Code;
        [JsonProperty("path")] public string Path;
        [JsonProperty("message")] public string Message;

        public ValidationIssue() { }
        public ValidationIssue(string severity, string code, string path, string message)
        {
            Severity = severity;
            Code = code;
            Path = path;
            Message = message;
        }
    }

    [Serializable]
    public sealed class ValidationReport
    {
        [JsonProperty("ok")] public bool Ok;
        [JsonProperty("issues")] public List<ValidationIssue> Issues = new List<ValidationIssue>();
        [JsonProperty("changed_fields")] public List<string> ChangedFields = new List<string>();
        [JsonProperty("control_metrics")] public RoomMetricsRecord ControlMetrics;
        [JsonProperty("treatment_metrics")] public RoomMetricsRecord TreatmentMetrics;
        [JsonProperty("coupled_metrics")] public List<MetricDelta> CoupledMetrics = new List<MetricDelta>();
    }

    [Serializable]
    public sealed class RoomMetricsRecord
    {
        [JsonProperty("floor_area_m2")] public float FloorAreaM2;
        [JsonProperty("volume_m3")] public float VolumeM3;
        [JsonProperty("perimeter_m")] public float PerimeterM;
        [JsonProperty("gross_wall_area_m2")] public float GrossWallAreaM2;
        [JsonProperty("net_wall_area_m2")] public float NetWallAreaM2;
        [JsonProperty("window_area_m2")] public float WindowAreaM2;
        [JsonProperty("window_to_wall_ratio")] public float WindowToWallRatio;
        [JsonProperty("minimum_furniture_clearance_m")] public float MinimumFurnitureClearanceM;
    }

    [Serializable]
    public sealed class MetricDelta
    {
        [JsonProperty("metric")] public string Metric;
        [JsonProperty("control")] public float Control;
        [JsonProperty("treatment")] public float Treatment;
        [JsonProperty("delta")] public float Delta;

        public MetricDelta() { }
        public MetricDelta(string metric, float control, float treatment)
        {
            Metric = metric;
            Control = control;
            Treatment = treatment;
            Delta = treatment - control;
        }
    }

    [Serializable]
    public sealed class ProbeReading
    {
        [JsonProperty("probe_id")] public string ProbeId;
        [JsonProperty("height_m")] public float HeightM;
        [JsonProperty("lux")] public float Lux;
    }

    [Serializable]
    public sealed class LightingResult
    {
        [JsonProperty("ok")] public bool Ok;
        [JsonProperty("target_lux")] public float TargetLux;
        [JsonProperty("tolerance_lux")] public float ToleranceLux;
        [JsonProperty("intensity_scale")] public float IntensityScale;
        [JsonProperty("mean_lux")] public float MeanLux;
        [JsonProperty("maximum_error_lux")] public float MaximumErrorLux;
        [JsonProperty("probes")] public List<ProbeReading> Probes = new List<ProbeReading>();
    }

    [Serializable]
    public sealed class BuildManifest
    {
        [JsonProperty("manifest_version")] public string ManifestVersion = "1.0";
        [JsonProperty("unity_version")] public string UnityVersion;
        [JsonProperty("generator_version")] public string GeneratorVersion = "1.0.0";
        [JsonProperty("pair_id")] public string PairId;
        [JsonProperty("created_utc")] public string CreatedUtc;
        [JsonProperty("asset_versions")] public Dictionary<string, string> AssetVersions = new Dictionary<string, string>();
        [JsonProperty("calibration")] public Dictionary<string, LightingResult> Calibration = new Dictionary<string, LightingResult>();
        [JsonProperty("sha256")] public Dictionary<string, string> Sha256 = new Dictionary<string, string>();
    }

    [Serializable]
    public sealed class RoomBuildResult
    {
        [JsonIgnore] public UnityEngine.GameObject Root;
        [JsonProperty("ok")] public bool Ok;
        [JsonProperty("metrics")] public RoomMetricsRecord Metrics;
        [JsonProperty("lighting")] public LightingResult Lighting;
        [JsonProperty("warnings")] public List<string> Warnings = new List<string>();
    }

    [Serializable]
    public sealed class ExportResult
    {
        [JsonProperty("ok")] public bool Ok;
        [JsonProperty("package_path")] public string PackagePath;
        [JsonProperty("message")] public string Message;
        [JsonProperty("validation")] public ValidationReport Validation;
    }
}
