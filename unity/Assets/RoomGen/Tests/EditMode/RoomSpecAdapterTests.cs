using NUnit.Framework;
using RoomGen.Adapter;
using RoomGen.Contracts;

namespace RoomGen.Tests
{
    /// <summary>
    /// G2 Part A — verifies RoomSpecAdapter maps Diego's canonical RoomSpec into the generator's
    /// internal RoomSpec, and the tracer-bullet: the adapted control/treatment pair differs ONLY
    /// in geometry.ceiling_height_m. (Furniture PLACEMENT is Part B / Fable, not asserted here.)
    /// The fixtures are the committed KA ceiling pair (spec/pairs/ceiling_height_study_01), written
    /// with single quotes and un-escaped at parse time for readability.
    /// </summary>
    public sealed class RoomSpecAdapterTests
    {
        static string Control => KaFixture("3.2", "control");
        static string Treatment => KaFixture("2.6", "treatment");

        /// <summary>Shared canonical-pair fixture (also used by SlotResolverTests).</summary>
        public static string KaFixture(string ceiling, string condition) =>
            Fixture.Replace('\'', '"').Replace("<<CEIL>>", ceiling).Replace("<<COND>>", condition);

        const string Fixture = @"{
            'spec_version':'1.0','room_type':'dining_room','seed':42,
            'shell':{'width_m':4.5,'length_m':6.0,'ceiling_height_m':<<CEIL>>,'contour':'angular'},
            'surfaces':{'wall':{'material':'plaster','tint_hex':'#ece7dc'},'floor':{'material':'wood'},'ceiling':{'material':'plaster','visible':true}},
            'openings':{'door':{'wall':'front','position':0.2,'width_m':1.0},
                        'windows':[{'wall':'left','position':0.3,'width_m':1.2},{'wall':'left','position':0.7,'width_m':1.2}]},
            'lighting':{'preset':'warm_evening','warmth':0.2,'intensity':0.9},
            'furniture':[{'catalog_id':'dining_table_6','placement':{'slot':'table_center'}},
                         {'catalog_id':'dining_chair','placement':{'slot':'chair_1'}},
                         {'catalog_id':'dining_chair','placement':{'slot':'chair_2'}},
                         {'catalog_id':'dining_chair','placement':{'slot':'chair_3'}},
                         {'catalog_id':'dining_chair','placement':{'slot':'chair_4'}},
                         {'catalog_id':'dining_chair','placement':{'slot':'chair_5'}},
                         {'catalog_id':'dining_chair','placement':{'slot':'chair_6'}},
                         {'catalog_id':'sideboard','placement':{'slot':'long_wall_back'}},
                         {'catalog_id':'pendant_light','placement':{'slot':'above_table'}}],
            'experiment':{'condition':'<<COND>>','manipulated_variables':['shell.ceiling_height_m'],'pair_id':'ceiling_height_study_01'},
            'provenance':{'generated_by':'human_wizard','execution_path':'live'}
        }";

        [Test]
        public void Maps_shell_to_geometry()
        {
            var g = RoomSpecAdapter.Adapt(Control).Spec.Geometry;
            Assert.AreEqual(4.5f, g.WidthM, 1e-4f);
            Assert.AreEqual(6.0f, g.LengthM, 1e-4f);
            Assert.AreEqual(3.2f, g.CeilingHeightM, 1e-4f);
            Assert.AreEqual(0f, g.CornerRadiusM, 1e-4f, "angular contour -> zero corner radius");
            Assert.AreEqual(0.15f, g.WallThicknessM, 1e-4f);
        }

        [Test]
        public void Maps_surfaces_to_builtin_materials()
        {
            var s = RoomSpecAdapter.Adapt(Control).Spec.Surfaces;
            Assert.AreEqual("builtin.oak", s.FloorMaterialId);            // wood
            Assert.AreEqual("builtin.warm-white", s.WallMaterialId);      // plaster
            Assert.AreEqual("builtin.ceiling-white", s.CeilingMaterialId); // plaster on ceiling
        }

        [Test]
        public void Maps_openings_with_position_to_centre_formula()
        {
            var openings = RoomSpecAdapter.Adapt(Control).Spec.Openings;
            Assert.AreEqual(3, openings.Count); // 1 door + 2 windows

            var door = openings.Find(o => o.Kind == "door");
            Assert.IsNotNull(door);
            Assert.AreEqual("front", door.Wall);
            Assert.AreEqual((0.2f - 0.5f) * 4.5f, door.CenterM, 1e-3f); // front wall len 4.5 -> -1.35
            Assert.AreEqual(0f, door.BottomM, 1e-3f);
            Assert.AreEqual(2.05f, door.TopM, 1e-3f);

            var windows = openings.FindAll(o => o.Kind == "window");
            Assert.AreEqual(2, windows.Count);
            Assert.AreEqual((0.3f - 0.5f) * 6.0f, windows[0].CenterM, 1e-3f); // left wall len 6.0 -> -1.2
            Assert.AreEqual((0.7f - 0.5f) * 6.0f, windows[1].CenterM, 1e-3f); // +1.2
            Assert.AreEqual(0.95f, windows[0].BottomM, 1e-3f); // default sill
            Assert.AreEqual(1.95f, windows[0].TopM, 1e-3f);    // default head
        }

        [Test]
        public void Maps_lighting_preset_intensity_and_warmth_law()
        {
            var l = RoomSpecAdapter.Adapt(Control).Spec.Lighting;
            Assert.AreEqual(200f * 0.9f, l.TargetLux, 1e-2f);          // warm_evening 200 lx * intensity 0.9
            Assert.AreEqual(6500f - 3800f * 0.2f, l.ColorTemperatureK, 1e-2f); // documented warmth law -> 5740 K
        }

        [Test]
        public void Maps_furniture_items_by_catalog()
        {
            var furniture = RoomSpecAdapter.Adapt(Control).Spec.Furniture;
            Assert.AreEqual(9, furniture.Count);
            Assert.AreEqual("builtin.dining-table", furniture[0].AssetId);
            Assert.AreEqual("table_center", furniture[0].SlotId);
            Assert.AreEqual(6, furniture.FindAll(f => f.AssetId == "builtin.dining-chair").Count);
            Assert.IsNotNull(furniture.Find(f => f.AssetId == "builtin.sideboard"));
        }

        [Test]
        public void Adapting_identical_stimuli_with_different_condition_labels_is_identical()
        {
            // Condition/provenance labels are experiment metadata, not stimulus: two specs that
            // differ only in those labels must adapt to byte-identical internal specs.
            var a = RoomSpecAdapter.Adapt(KaFixture("3.2", "control")).Spec;
            var b = RoomSpecAdapter.Adapt(KaFixture("3.2", "treatment")).Spec;
            Assert.AreEqual(RoomJson.Serialize(a), RoomJson.Serialize(b));
        }

        [Test]
        public void Pair_differs_only_in_ceiling_height()
        {
            var control = RoomSpecAdapter.Adapt(Control).Spec;
            var treatment = RoomSpecAdapter.Adapt(Treatment).Spec;

            Assert.AreNotEqual(control.Geometry.CeilingHeightM, treatment.Geometry.CeilingHeightM,
                "sanity: the two conditions must differ in ceiling height");

            // Ceiling mounting is a flag (the builder derives the height), so even the pendant's
            // spec entry is identical across the pair: equalising the ONE declared variable must
            // make the two adapted specs byte-identical.
            treatment.Geometry.CeilingHeightM = control.Geometry.CeilingHeightM;
            Assert.AreEqual(RoomJson.Serialize(control), RoomJson.Serialize(treatment),
                "after equalising ceiling height the adapted control and treatment must be identical");
        }

        [Test]
        public void Adapted_pair_passes_the_internal_validators()
        {
            // Regression for the black-screen bug: RoomGenerator refuses to BUILD a spec with
            // validation errors, and PairValidator gates the studio's export. The adapted canonical
            // pair must clear both — with the real DiningRoomPreset loaded, exactly as at runtime.
            var preset = RoomJson.Deserialize<DiningRoomPreset>(
                UnityEngine.Resources.Load<UnityEngine.TextAsset>("RoomGen/DiningRoomPreset").text);

            var pair = RoomSpecAdapter.AdaptPair(Control, Treatment);
            foreach (var (spec, label) in new[] { (pair.Control, "control"), (pair.Treatment, "treatment") })
            {
                var issues = RoomGen.Validation.RoomSpecValidator.Validate(spec, preset);
                Assert.IsFalse(issues.Exists(i => i.Severity == "error"),
                    label + " has validation errors: " + string.Join("; ",
                        issues.ConvertAll(i => i.Code + " " + i.Path + " " + i.Message)));
            }

            var report = RoomGen.Validation.PairValidator.Validate(pair);
            Assert.IsTrue(report.Ok, "pair check failed: " + string.Join("; ",
                report.Issues.ConvertAll(i => i.Code + " " + i.Path + " " + i.Message))
                + " | changed: " + string.Join(", ", report.ChangedFields));
        }

        [Test]
        public void AdaptPair_translates_declared_variable_to_generator_path()
        {
            var pair = RoomSpecAdapter.AdaptPair(Control, Treatment);
            Assert.AreEqual("ceiling_height_study_01", pair.PairId);
            Assert.AreEqual("geometry.ceiling_height_m", pair.ManipulatedVariable);
            Assert.AreEqual(3.2f, pair.Control.Geometry.CeilingHeightM, 1e-4f);
            Assert.AreEqual(2.6f, pair.Treatment.Geometry.CeilingHeightM, 1e-4f);
        }
    }
}
