using System.IO;
using System.Linq;
using NUnit.Framework;
using RoomGen.Contracts;
using RoomGen.Export;
using RoomGen.Lighting;
using RoomGen.Metrics;
using RoomGen.Validation;
using UnityEngine;

namespace RoomGen.Tests
{
    public sealed class ContractsAndValidationTests
    {
        [Test]
        public void JsonRoundTripPreservesPair()
        {
            var pair = LoadExamplePair();
            var json = RoomJson.Serialize(pair);
            var restored = RoomJson.Deserialize<ConditionPairSpec>(json);
            Assert.That(RoomJson.Serialize(restored), Is.EqualTo(json));
        }

        [Test]
        public void BaselinePairDiffersOnlyByCeilingHeight()
        {
            var report = PairValidator.Validate(LoadExamplePair());
            Assert.That(report.Ok, Is.True, IssueSummary(report));
            Assert.That(report.ChangedFields,
                Is.EqualTo(new[] { "geometry.ceiling_height_m" }));
            Assert.That(report.CoupledMetrics.Any(metric => metric.Metric == "volume_m3"), Is.True);
        }

        [Test]
        public void CurvedWallPairDiffersOnlyByCornerRadius()
        {
            var asset = Resources.Load<TextAsset>("RoomGen/Examples/curved-wall-pair");
            Assert.That(asset, Is.Not.Null, "curved-wall-pair resource is missing.");
            var report = PairValidator.Validate(RoomJson.Deserialize<ConditionPairSpec>(asset.text));
            Assert.That(report.Ok, Is.True, IssueSummary(report));
            Assert.That(report.ChangedFields, Is.EqualTo(new[] { "geometry.corner_radius_m" }));
        }

        [Test]
        public void WarmthPairDiffersOnlyByColorTemperature()
        {
            var asset = Resources.Load<TextAsset>("RoomGen/Examples/warmth-pair");
            Assert.That(asset, Is.Not.Null, "warmth-pair resource is missing.");
            var report = PairValidator.Validate(RoomJson.Deserialize<ConditionPairSpec>(asset.text));
            Assert.That(report.Ok, Is.True, IssueSummary(report));
            Assert.That(report.ChangedFields, Is.EqualTo(new[] { "lighting.color_temperature_k" }));
        }

        [Test]
        public void ValidatorRejectsUndeclaredDifference()
        {
            var pair = LoadExamplePair();
            pair.Treatment.Surfaces.FloorMaterialId = "builtin.walnut";
            var report = PairValidator.Validate(pair);
            Assert.That(report.Ok, Is.False);
            Assert.That(report.Issues.Any(issue =>
                issue.Code == "UNDECLARED_DIFFERENCE" &&
                issue.Path == "surfaces.floor_material_id"), Is.True);
        }

        [Test]
        public void ValidatorRejectsOpeningOutsideWall()
        {
            var pair = LoadExamplePair();
            pair.Control.Openings[0].CenterM = 9f;
            var issues = RoomSpecValidator.Validate(pair.Control);
            Assert.That(issues.Any(issue => issue.Code == "OPENING_BOUNDS"), Is.True);
        }

        [Test]
        public void ValidatorRejectsFurnitureCollision()
        {
            var pair = LoadExamplePair();
            pair.Control.Furniture[1].PositionM = new Vec3(0f, 0f, 0f);
            var issues = RoomSpecValidator.Validate(pair.Control);
            Assert.That(issues.Any(issue => issue.Code == "FURNITURE_COLLISION"), Is.True);
        }

        [Test]
        public void ValidatorRejectsFurnitureBlockingDoor()
        {
            var pair = LoadExamplePair();
            pair.Control.Furniture[8].PositionM = new Vec3(1.75f, 0f, 2.75f);
            var issues = RoomSpecValidator.Validate(pair.Control);
            Assert.That(issues.Any(issue => issue.Code == "FURNITURE_OPENING_COLLISION"), Is.True);
        }

        [Test]
        public void CurvedMetricsUseRoundedRectangleFormula()
        {
            var room = LoadExamplePair().Control;
            room.Geometry.CornerRadiusM = 0.8f;
            var metrics = RoomMetrics.Calculate(room);
            var expected = room.Geometry.WidthM * room.Geometry.LengthM -
                           (4f - Mathf.PI) * 0.8f * 0.8f;
            Assert.That(metrics.FloorAreaM2, Is.EqualTo(expected).Within(0.0001f));
        }

        [Test]
        public void LuxCalibrationPassesAtTableAndEyeProbes()
        {
            var pair = LoadExamplePair();
            var control = LightingCalibrator.MatchTargetLux(pair.Control);
            var treatment = LightingCalibrator.MatchTargetLux(pair.Treatment);
            Assert.That(control.Ok, Is.True,
                $"Control maximum error was {control.MaximumErrorLux:0.0} lux.");
            Assert.That(treatment.Ok, Is.True,
                $"Treatment maximum error was {treatment.MaximumErrorLux:0.0} lux.");
        }

        [Test]
        public void ExportedPackageReloadsWithoutSpecChanges()
        {
            var root = Path.Combine(Path.GetTempPath(), "roomgen-tests", TestContext.CurrentContext.Test.ID);
            var result = RoomPackageExporter.Export(LoadExamplePair(), root);
            Assert.That(result.Ok, Is.True, result.Message);
            var folder = Path.Combine(root, "dining-ceiling-height-v1");
            var restored = RoomPackageExporter.LoadPackage(folder);
            Assert.That(RoomJson.Serialize(restored), Is.EqualTo(RoomJson.Serialize(LoadExamplePair())));
        }

        [Test]
        public void ScreenshotComparatorDetectsExposureOrMaterialShift()
        {
            var baseline = SolidTexture(new Color32(100, 100, 100, 255));
            var matched = SolidTexture(new Color32(101, 100, 99, 255));
            var shifted = SolidTexture(new Color32(170, 160, 150, 255));
            Assert.That(ScreenshotRegression.Matches(baseline, matched), Is.True);
            Assert.That(ScreenshotRegression.Matches(baseline, shifted), Is.False);
            Object.DestroyImmediate(baseline);
            Object.DestroyImmediate(matched);
            Object.DestroyImmediate(shifted);
        }

        static ConditionPairSpec LoadExamplePair()
        {
            var asset = Resources.Load<TextAsset>("RoomGen/Examples/ceiling-height-pair");
            Assert.That(asset, Is.Not.Null);
            return RoomJson.Deserialize<ConditionPairSpec>(asset.text);
        }

        static string IssueSummary(ValidationReport report) =>
            string.Join("\n", report.Issues.Select(issue =>
                $"{issue.Code} {issue.Path}: {issue.Message}"));

        static Texture2D SolidTexture(Color32 color)
        {
            var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            var pixels = Enumerable.Repeat(color, 64).ToArray();
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}
