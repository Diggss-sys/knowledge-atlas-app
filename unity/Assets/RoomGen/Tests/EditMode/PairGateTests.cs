using System.Linq;
using NUnit.Framework;
using RoomGen.Gate;
using UnityEngine;

namespace RoomGen.Tests
{
    /// <summary>
    /// G3 — proves the C# PairGate reproduces Diego's reference validator (tools/validate_pair.py).
    /// The definition of done is the golden-vector test: every case in the committed
    /// spec/fixtures/diff_vectors.json must match its expected block exactly. The two parity tests
    /// cover the one path the 8 vectors don't (indexed furniture coverage) and the provenance rule.
    /// Tests read only strings + plain structs, so this assembly needs no Newtonsoft reference.
    /// </summary>
    public sealed class PairGateTests
    {
        static string Schema => Resources.Load<TextAsset>("RoomGen/room_spec.schema").text;
        static string DiffVectors => Resources.Load<TextAsset>("RoomGen/Fixtures/diff_vectors").text;

        [Test]
        public void Reproduces_every_golden_vector()
        {
            var outcomes = PairGateFixtureRunner.RunGoldenVectors(DiffVectors, Schema);

            Assert.AreEqual(8, outcomes.Count, "expected 8 golden vectors");
            var failures = outcomes.Where(o => !o.Match).Select(o => $"  {o.Name}: {o.Detail}").ToList();
            Assert.IsEmpty(failures,
                "PairGate diverged from the reference validator on:\n" + string.Join("\n", failures));
        }

        // --- parity coverage the 8 dotted-path vectors don't reach ---

        [Test]
        public void Declared_parent_path_covers_nested_indexed_changes()
        {
            // manipulated_variables:["furniture"] must cover a change at furniture[8].* — the indexed
            // path convention + prefix coverage that the dotted-only golden vectors never exercise.
            var control = Pair("control", furnitureCount: 9, ceiling: "3.0", manipulated: "\"furniture\"");
            var treatment = Pair("treatment", furnitureCount: 8, ceiling: "3.0", manipulated: "\"furniture\"");

            var r = PairGate.ValidateJson(control, treatment, Schema);
            Assert.IsTrue(r.Ok, "furniture[] change is covered by the declared 'furniture' parent path: "
                + string.Join(", ", r.Violations.Select(v => v.Code + " " + v.Path)));
            Assert.IsTrue(r.DiffPaths.Any(p => p.StartsWith("furniture[")),
                "the diff should be an indexed furniture path, got: " + string.Join(", ", r.DiffPaths));
        }

        [Test]
        public void Provenance_only_differences_are_not_stimulus_changes()
        {
            // The pair legitimately differs in the declared ceiling height; a provenance edit on top
            // of that must NOT register as a second (undeclared) change.
            var control = Pair("control", furnitureCount: 9, ceiling: "3.0",
                manipulated: "\"shell.ceiling_height_m\"");
            var treatment = Pair("treatment", furnitureCount: 9, ceiling: "2.6",
                manipulated: "\"shell.ceiling_height_m\"")
                .Replace("\"generated_by\":\"human_wizard\"", "\"generated_by\":\"ai_agent\"");

            var r = PairGate.ValidateJson(control, treatment, Schema);
            Assert.IsTrue(r.Ok, "provenance is non-stimulus; the pair should still pass: "
                + string.Join(", ", r.Violations.Select(v => v.Code + " " + v.Path)));
            Assert.AreEqual(new[] { "shell.ceiling_height_m" }, r.DiffPaths.ToArray());
        }

        // A schema-valid dining pair. Caller varies condition / furniture count / ceiling / declared
        // list to construct each scenario.
        static string Pair(string condition, int furnitureCount, string ceiling, string manipulated)
        {
            var chairs = string.Join(",",
                Enumerable.Range(1, furnitureCount).Select(i =>
                    "{\"catalog_id\":\"dining_chair\",\"placement\":{\"slot\":\"chair_" + i + "\"}}"));
            return @"{
                ""spec_version"":""1.0"",""room_type"":""dining_room"",""seed"":42,
                ""shell"":{""width_m"":4.5,""length_m"":6.0,""ceiling_height_m"":" + ceiling + @",""contour"":""angular""},
                ""surfaces"":{""wall"":{""material"":""plaster""},""floor"":{""material"":""wood""}},
                ""lighting"":{""preset"":""warm_evening""},
                ""furniture"":[" + chairs + @"],
                ""experiment"":{""condition"":""" + condition + @""",""manipulated_variables"":[" + manipulated + @"],""pair_id"":""p1""},
                ""provenance"":{""generated_by"":""human_wizard"",""execution_path"":""live""}
            }";
        }
    }
}
