using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RoomGen.Contracts;
using RoomGen.Metrics;
using UnityEngine;

namespace RoomGen.Validation
{
    public static class PairValidator
    {
        public static ValidationReport Validate(ConditionPairSpec pair)
        {
            var report = new ValidationReport();
            if (pair == null)
            {
                report.Issues.Add(new ValidationIssue("error", "PAIR_NULL", "", "Condition pair is missing."));
                return report;
            }
            if (pair.Control == null || pair.Treatment == null)
            {
                report.Issues.Add(new ValidationIssue("error", "PAIR_CONDITION", "",
                    "Both control and treatment room specifications are required."));
                return report;
            }

            var preset = LoadPreset();
            report.Issues.AddRange(RoomSpecValidator.Validate(pair.Control, preset)
                .Select(issue => Prefix(issue, "control.")));
            report.Issues.AddRange(RoomSpecValidator.Validate(pair.Treatment, preset)
                .Select(issue => Prefix(issue, "treatment.")));

            report.ChangedFields = FindDifferences(pair.Control, pair.Treatment);
            if (string.IsNullOrWhiteSpace(pair.ManipulatedVariable))
            {
                report.Issues.Add(new ValidationIssue("error", "MANIPULATION_MISSING",
                    "manipulated_variable", "A manipulated variable must be declared."));
            }
            else
            {
                foreach (var path in report.ChangedFields.Where(path =>
                             !Covers(pair.ManipulatedVariable, path)))
                    report.Issues.Add(new ValidationIssue("error", "UNDECLARED_DIFFERENCE", path,
                        $"Only '{pair.ManipulatedVariable}' may differ between conditions."));

                if (!report.ChangedFields.Any(path => Covers(pair.ManipulatedVariable, path)))
                    report.Issues.Add(new ValidationIssue("error", "MANIPULATION_UNCHANGED",
                        pair.ManipulatedVariable, "The declared manipulation does not differ."));
            }

            report.ControlMetrics = RoomMetrics.Calculate(pair.Control);
            report.TreatmentMetrics = RoomMetrics.Calculate(pair.Treatment);
            AddCoupledMetrics(report);
            report.Ok = report.Issues.All(issue => issue.Severity != "error");
            return report;
        }

        public static List<string> FindDifferences(RoomSpec control, RoomSpec treatment)
        {
            var left = JObject.FromObject(control);
            var right = JObject.FromObject(treatment);
            var paths = new List<string>();
            Compare(left, right, "", paths);
            return paths.Distinct().OrderBy(path => path, StringComparer.Ordinal).ToList();
        }

        static void Compare(JToken left, JToken right, string path, List<string> paths)
        {
            if (left == null || right == null)
            {
                if (!JToken.DeepEquals(left, right)) paths.Add(path);
                return;
            }

            if (left.Type != right.Type)
            {
                paths.Add(path);
                return;
            }

            if (left is JObject leftObject && right is JObject rightObject)
            {
                var names = leftObject.Properties().Select(p => p.Name)
                    .Union(rightObject.Properties().Select(p => p.Name))
                    .OrderBy(name => name, StringComparer.Ordinal);
                foreach (var name in names)
                {
                    var childPath = string.IsNullOrEmpty(path) ? name : path + "." + name;
                    Compare(leftObject[name], rightObject[name], childPath, paths);
                }
                return;
            }

            if (left is JArray leftArray && right is JArray rightArray)
            {
                if (leftArray.Count != rightArray.Count)
                {
                    paths.Add(path);
                    return;
                }
                for (var i = 0; i < leftArray.Count; i++)
                    Compare(leftArray[i], rightArray[i], $"{path}[{i}]", paths);
                return;
            }

            if (!JToken.DeepEquals(left, right))
                paths.Add(path);
        }

        // Declarations that are a single conceptual variable spanning several leaf fields which
        // MUST move together. Only these may cover a whole sub-object; wall_bow is one variable
        // applied to all four walls. Everything else must name an exact leaf.
        static readonly string[] GroupVariables = { "geometry.wall_bow" };

        // A declared manipulation covers an exact field, OR — only for a whitelisted group variable
        // — the whole sub-object beneath it. Without the whitelist a bare prefix like "geometry" or
        // "lighting" would absorb every field under it (width + ceiling + radius + bow at once),
        // silently voiding the single-variable guarantee the gate exists to enforce.
        static bool Covers(string declared, string path) =>
            string.Equals(path, declared, StringComparison.Ordinal) ||
            (Array.IndexOf(GroupVariables, declared) >= 0 &&
             path.StartsWith(declared + ".", StringComparison.Ordinal));

        static ValidationIssue Prefix(ValidationIssue issue, string prefix) =>
            new ValidationIssue(issue.Severity, issue.Code,
                string.IsNullOrEmpty(issue.Path) ? prefix.TrimEnd('.') : prefix + issue.Path,
                issue.Message);

        static void AddCoupledMetrics(ValidationReport report)
        {
            AddIfChanged(report, "floor_area_m2", report.ControlMetrics.FloorAreaM2, report.TreatmentMetrics.FloorAreaM2);
            AddIfChanged(report, "volume_m3", report.ControlMetrics.VolumeM3, report.TreatmentMetrics.VolumeM3);
            AddIfChanged(report, "perimeter_m", report.ControlMetrics.PerimeterM, report.TreatmentMetrics.PerimeterM);
            AddIfChanged(report, "gross_wall_area_m2", report.ControlMetrics.GrossWallAreaM2, report.TreatmentMetrics.GrossWallAreaM2);
            AddIfChanged(report, "window_to_wall_ratio", report.ControlMetrics.WindowToWallRatio, report.TreatmentMetrics.WindowToWallRatio);
        }

        static void AddIfChanged(ValidationReport report, string name, float control, float treatment)
        {
            if (Math.Abs(control - treatment) > 0.0001f)
                report.CoupledMetrics.Add(new MetricDelta(name, control, treatment));
        }

        static DiningRoomPreset LoadPreset()
        {
            var asset = Resources.Load<TextAsset>("RoomGen/DiningRoomPreset");
            return asset == null ? null : RoomJson.Deserialize<DiningRoomPreset>(asset.text);
        }
    }
}
