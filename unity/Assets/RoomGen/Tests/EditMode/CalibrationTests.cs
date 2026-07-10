using NUnit.Framework;
using RoomGen.Contracts;
using RoomGen.Lighting;

namespace RoomGen.Tests
{
    public class CalibrationTests
    {
        static RoomSpec BaseSpec()
        {
            var s = new RoomSpec();
            s.Geometry.WidthM = 4.2f;
            s.Geometry.LengthM = 5.0f;
            s.Geometry.CeilingHeightM = 2.6f;
            s.Lighting.TargetLux = 300f;
            s.Lighting.ToleranceLux = 60f;
            s.Lighting.BaseLuminousFluxLm = 3200f;
            return s;
        }

        [Test]
        public void Calibration_scales_to_target_within_tolerance()
        {
            var r = LightingCalibrator.MatchTargetLux(BaseSpec());
            Assert.That(r.MeanLux, Is.EqualTo(300f).Within(0.5f), "Mean lux should be scaled to target.");
            Assert.IsTrue(r.Ok, "Probe spread should be within tolerance for a standard room.");
        }

        [Test]
        public void Taller_ceiling_needs_more_intensity_for_the_same_target()
        {
            var control = BaseSpec();
            var treatment = BaseSpec();
            treatment.Geometry.CeilingHeightM = 3.2f;

            var rc = LightingCalibrator.MatchTargetLux(control);
            var rt = LightingCalibrator.MatchTargetLux(treatment);

            // Matched luminance: both conditions are driven to the same target mean lux...
            Assert.That(rc.MeanLux, Is.EqualTo(rt.MeanLux).Within(0.5f));
            // ...but the taller room is physically dimmer, so it needs a higher intensity scale.
            Assert.That(rt.IntensityScale, Is.GreaterThan(rc.IntensityScale));
        }

        [Test]
        public void Pendant_is_accounted_for_not_ignored()
        {
            var noPendant = BaseSpec();
            var withPendant = BaseSpec();
            withPendant.Furniture.Add(new FurniturePlacementSpec
            {
                AssetId = "builtin.pendant-light",
                SlotId = "pendant-center",
                PositionM = new Vec3 { X = 0f, Y = 0f, Z = 0f },
                CeilingMounted = true
            });

            var rNo = LightingCalibrator.MatchTargetLux(noPendant);
            var rYes = LightingCalibrator.MatchTargetLux(withPendant);

            // Moving 40% of the flux onto a source directly over the probes must change the required
            // scale — proof the calibrator models the pendant instead of silently drifting off target.
            Assert.AreNotEqual(rNo.IntensityScale, rYes.IntensityScale);
            // And it still lands on target.
            Assert.That(rYes.MeanLux, Is.EqualTo(300f).Within(0.5f));
        }
    }
}
