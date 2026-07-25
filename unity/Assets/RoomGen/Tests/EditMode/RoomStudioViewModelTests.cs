using NUnit.Framework;
using RoomGen.Contracts;
using RoomGen.UI.Studio;
using RoomGen.Validation;

namespace RoomGen.Tests
{
    /// <summary>
    /// Drives the Room Studio view-model with no scene at all — the point of keeping it pure C#.
    /// These cover the three behaviours the IMGUI panel got wrong: per-frame rebuilds, slider ranges
    /// that disagreed with the gate, and silently-dead controls.
    /// </summary>
    public sealed class RoomStudioViewModelTests
    {
        static ConditionPairSpec NewPair()
        {
            var pair = new ConditionPairSpec
            {
                ManipulatedVariable = "geometry.ceiling_height_m",
                Control = new RoomSpec(),
                Treatment = new RoomSpec()
            };
            foreach (var spec in new[] { pair.Control, pair.Treatment })
            {
                spec.Geometry.WidthM = 5f;
                spec.Geometry.LengthM = 6f;
                spec.Geometry.CeilingHeightM = 2.4f;
            }
            return pair;
        }

        [Test]
        public void DraggingCoalescesIntoASingleRebuild()
        {
            var vm = new RoomStudioViewModel(NewPair());
            var rebuilds = 0;
            vm.RebuildRequested += () => rebuilds++;

            // Simulate a drag: many edits inside one debounce window.
            for (var i = 0; i < 20; i++)
            {
                vm.SetConditionValue(false, 2.4f + i * 0.01f);
                vm.Tick(0.001f);
            }
            Assert.That(rebuilds, Is.EqualTo(0), "must not rebuild mid-drag");

            vm.Tick(RoomStudioViewModel.DebounceSeconds);
            Assert.That(rebuilds, Is.EqualTo(1), "one rebuild after the drag settles");

            // Idle ticks must not keep firing.
            vm.Tick(1f);
            Assert.That(rebuilds, Is.EqualTo(1));
        }

        [Test]
        public void SliderBoundsComeFromTheValidator()
        {
            var vm = new RoomStudioViewModel(NewPair());
            var slider = vm.ConditionSlider(treatment: false);
            Assert.That(slider.Min, Is.EqualTo(RoomSpecValidator.MinCeilingHeightM));
            Assert.That(slider.Max, Is.EqualTo(RoomSpecValidator.MaxCeilingHeightM));
        }

        [Test]
        public void ValuesClampInsideTheLegalEnvelope()
        {
            var pair = NewPair();
            var vm = new RoomStudioViewModel(pair);

            vm.SetConditionValue(true, 999f);
            Assert.That(pair.Treatment.Geometry.CeilingHeightM,
                Is.EqualTo(RoomSpecValidator.MaxCeilingHeightM),
                "a slider must never produce a spec the gate would refuse");

            vm.SetSharedValue("shared.width_m", -5f);
            Assert.That(pair.Control.Geometry.WidthM, Is.EqualTo(RoomSpecValidator.MinWidthM));
            Assert.That(pair.Treatment.Geometry.WidthM, Is.EqualTo(RoomSpecValidator.MinWidthM),
                "shared values apply to BOTH conditions or they become a pair difference");
        }

        [Test]
        public void WallBowAppliesToAllFourWalls()
        {
            var pair = NewPair();
            pair.ManipulatedVariable = "geometry.wall_bow";
            var vm = new RoomStudioViewModel(pair);

            vm.SetConditionValue(true, -0.5f);
            var bow = pair.Treatment.Geometry.WallBow;
            Assert.That(bow.Front, Is.EqualTo(-0.5f));
            Assert.That(bow.Back, Is.EqualTo(-0.5f));
            Assert.That(bow.Left, Is.EqualTo(-0.5f));
            Assert.That(bow.Right, Is.EqualTo(-0.5f));
        }

        [Test]
        public void UnavailableActionsAlwaysCarryAReason()
        {
            var vm = new RoomStudioViewModel(NewPair());

            var vr = vm.VrAction();
            Assert.That(vr.Enabled, Is.False);
            Assert.That(vr.Reason, Is.Not.Empty, "a dark button must say why");

            var seam = vm.SeamWalkAction(walkRoomReady: false);
            Assert.That(seam.Enabled, Is.False);
            Assert.That(seam.Reason, Is.Not.Empty);

            vm.SetVrAvailable(true);
            Assert.That(vm.VrAction().Enabled, Is.True);
            Assert.That(vm.SeamWalkAction(walkRoomReady: true).Enabled, Is.True);
        }

        [Test]
        public void GlazedRoomIsTheFirstChoice()
        {
            // Daylight through windows is what the realism pass is judged on, so the studio should
            // open on the glazed room rather than a view of a blank wall.
            Assert.That(RoomStudioViewModel.Rooms[0].Id, Is.EqualTo("realism-test-pair"));
        }
    }
}
