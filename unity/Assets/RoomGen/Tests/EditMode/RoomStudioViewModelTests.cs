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
        public void DragUpdatesLiveButRateLimited()
        {
            var vm = new RoomStudioViewModel(NewPair());
            var rebuilds = 0;
            vm.RebuildRequested += () => rebuilds++;

            // A real drag: the room must FOLLOW the slider, not jump to the value once you let go.
            // 20 frames at 60 fps ≈ 0.33 s, which spans several throttle windows.
            for (var i = 0; i < 20; i++)
            {
                vm.SetConditionValue(false, 2.4f + i * 0.01f);
                vm.Tick(1f / 60f);
            }
            Assert.That(rebuilds, Is.GreaterThan(1),
                "the room must update DURING the drag, not only when the slider is released");

            // ...but not once per frame — that is the legacy panel's bug (a full mesh regeneration
            // every frame). 0.33 s at a 0.05 s floor can produce at most ~7.
            Assert.That(rebuilds, Is.LessThanOrEqualTo(8),
                "rebuilds must stay rate-limited, not fire every frame");

            // Releasing the slider flushes the FINAL value: the last edit of a drag usually lands
            // inside a throttle window, so one more rebuild is owed. Without it the room would keep
            // the second-to-last value and quietly disagree with the slider.
            vm.Tick(1f);
            var settled = rebuilds;

            // After that flush, idling must be silent — no rebuild loop burning frames.
            vm.Tick(1f);
            vm.Tick(1f);
            Assert.That(rebuilds, Is.EqualTo(settled), "no rebuilds while idle");
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
