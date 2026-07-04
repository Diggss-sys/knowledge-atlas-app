using NUnit.Framework;
using RoomGen.Adapter;
using RoomGen.Contracts;

namespace RoomGen.Tests
{
    /// <summary>
    /// G2 Part B — verifies SlotResolver's placement math (spec/PRESETS.md) through the adapter,
    /// using the canonical KA dining fixture. Expected values are hand-computed from the contract:
    ///   table 2.0×1.0 at origin; chairs 0.5×0.5, CLEARANCE 0.05
    ///   front/back chair z = ±(0.5+0.05+0.25) = ±0.8; two per side at x = ((k+1)/3 − 0.5)·2.0 = ∓0.3333
    ///   left/right chair x = ±(1.0+0.05+0.25) = ±1.3, z = 0 (single slot per side)
    ///   sideboard (1.6×0.5) on the back wall of a 6 m room, offset 0.3: z = −3+0.3+0.25 = −2.45
    ///   pendant: ceiling-mounted at the ceiling plane.
    /// Rotations face the table: front→180, back→0, left→90, right→−90 (0 = facing +z).
    /// </summary>
    public sealed class SlotResolverTests
    {
        static string Control =>
            RoomSpecAdapterTests.KaFixture("3.2", "control");
        static string ControlWithoutChair4 =>
            Control.Replace("{\"catalog_id\":\"dining_chair\",\"placement\":{\"slot\":\"chair_4\"}},", "");

        static FurniturePlacementSpec Item(RoomSpec spec, string slotId) =>
            spec.Furniture.Find(f => f.SlotId == slotId);

        const float Eps = 1e-3f;

        [Test]
        public void Table_sits_at_the_absolute_slot()
        {
            var spec = RoomSpecAdapter.Adapt(Control).Spec;
            var table = Item(spec, "table_center");
            Assert.AreEqual(0f, table.PositionM.X, Eps);
            Assert.AreEqual(0f, table.PositionM.Z, Eps);
            Assert.AreEqual(0f, table.RotationYDeg, Eps);
        }

        [Test]
        public void Front_and_back_chairs_flank_the_table_and_face_it()
        {
            var spec = RoomSpecAdapter.Adapt(Control).Spec;

            var c3 = Item(spec, "chair_3"); // front, index 0
            Assert.AreEqual(-2f / 6f, c3.PositionM.X, Eps);  // (1/3 − 1/2)·2.0
            Assert.AreEqual(0.8f, c3.PositionM.Z, Eps);
            Assert.AreEqual(180f, c3.RotationYDeg, Eps);

            var c4 = Item(spec, "chair_4"); // front, index 1
            Assert.AreEqual(+2f / 6f, c4.PositionM.X, Eps);
            Assert.AreEqual(0.8f, c4.PositionM.Z, Eps);

            var c1 = Item(spec, "chair_1"); // back, index 0
            Assert.AreEqual(-2f / 6f, c1.PositionM.X, Eps);
            Assert.AreEqual(-0.8f, c1.PositionM.Z, Eps);
            Assert.AreEqual(0f, c1.RotationYDeg, Eps);
        }

        [Test]
        public void End_chairs_sit_at_the_table_ends_and_face_it()
        {
            var spec = RoomSpecAdapter.Adapt(Control).Spec;

            var c5 = Item(spec, "chair_5"); // left end
            Assert.AreEqual(-1.3f, c5.PositionM.X, Eps);
            Assert.AreEqual(0f, c5.PositionM.Z, Eps);
            Assert.AreEqual(90f, c5.RotationYDeg, Eps);

            var c6 = Item(spec, "chair_6"); // right end
            Assert.AreEqual(+1.3f, c6.PositionM.X, Eps);
            Assert.AreEqual(-90f, c6.RotationYDeg, Eps);
        }

        [Test]
        public void Sideboard_sits_against_the_back_wall_facing_in()
        {
            var spec = RoomSpecAdapter.Adapt(Control).Spec;
            var sb = Item(spec, "long_wall_back");
            Assert.AreEqual(0f, sb.PositionM.X, Eps);
            Assert.AreEqual(-2.45f, sb.PositionM.Z, Eps); // −L/2 + 0.3 + depth/2
            Assert.AreEqual(0f, sb.RotationYDeg, Eps);    // faces +z, into the room
        }

        [Test]
        public void Pendant_mounts_at_the_ceiling_over_the_table()
        {
            var spec = RoomSpecAdapter.Adapt(Control).Spec;
            var pendant = Item(spec, "above_table");
            Assert.AreEqual(0f, pendant.PositionM.X, Eps);
            Assert.AreEqual(0f, pendant.PositionM.Z, Eps);
            Assert.AreEqual(3.2f, pendant.PositionM.Y, Eps); // ceiling plane of the control room
        }

        [Test]
        public void Omitting_one_chair_does_not_move_the_others()
        {
            // Single-variable discipline: group size counts the SLOTS DEFINED IN THE LAYOUT, not the
            // items placed — a presence manipulation on chair_4 must not shift chair_3.
            var full = RoomSpecAdapter.Adapt(Control).Spec;
            var reduced = RoomSpecAdapter.Adapt(ControlWithoutChair4).Spec;

            Assert.IsNull(Item(reduced, "chair_4"), "fixture edit failed — chair_4 should be absent");
            var before = Item(full, "chair_3");
            var after = Item(reduced, "chair_3");
            Assert.AreEqual(before.PositionM.X, after.PositionM.X, Eps);
            Assert.AreEqual(before.PositionM.Z, after.PositionM.Z, Eps);
        }
    }
}
