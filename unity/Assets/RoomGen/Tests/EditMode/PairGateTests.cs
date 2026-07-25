using System.Linq;
using NUnit.Framework;
using RoomGen.Gate;
using UnityEngine;

namespace RoomGen.Tests
{
    /// <summary>
    /// G3 — proves the C# PairGate reproduces Diego's reference validator (tools/validate_pair.py).
    /// The definition of done is the golden-vector test: every case in the committed
    /// spec/fixtures/diff_vectors.json must match its expected block exactly. The remaining tests
    /// cover what the 8 dotted-path vectors don't reach: the strict coverage contract (a bare parent
    /// declaration is refused, an exact indexed leaf declaration is accepted), the exact-leaf
    /// flagship case, and the provenance rule.
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
        public void Broad_parent_declaration_does_not_cover_nested_indexed_changes()
        {
            // A bare parent like "furniture" must NOT absorb everything beneath it: that would let a
            // pair differ in any number of nested fields while declaring a single variable, voiding
            // the single-variable guarantee. The reference validator (tools/validate_pair.py) and the
            // editor-side PairValidator both refuse this, so the runtime gate must too.
            // (This test previously asserted the opposite — the runtime gate was left permissive when
            // the reference + editor validator were tightened; see the strictness note on IsCovered.)
            var control = Pair("control", furnitureCount: 9, ceiling: "3.0", manipulated: "\"furniture\"");
            var treatment = Pair("treatment", furnitureCount: 8, ceiling: "3.0", manipulated: "\"furniture\"");

            var r = PairGate.ValidateJson(control, treatment, Schema);

            Assert.IsFalse(r.Ok, "a bare 'furniture' parent must not cover furniture[8].* changes");
            CollectionAssert.Contains(r.ViolationCodes, "undeclared_change",
                "the nested change is undeclared: " + string.Join(", ", r.ViolationCodes));
            Assert.IsTrue(r.DiffPaths.Any(p => p.StartsWith("furniture[")),
                "the diff should still report the indexed furniture path, got: " + string.Join(", ", r.DiffPaths));
        }

        [Test]
        public void Exact_indexed_leaf_declarations_cover_indexed_changes()
        {
            // The strict-contract way to declare an indexed change: name the exact leaf paths.
            // Dropping the 9th chair removes both of its leaves, so both are declared — and each
            // declared variable really does differ, so there is no declared_unchanged either.
            const string declared = "\"furniture[8].catalog_id\",\"furniture[8].placement.slot\"";
            var control = Pair("control", furnitureCount: 9, ceiling: "3.0", manipulated: declared);
            var treatment = Pair("treatment", furnitureCount: 8, ceiling: "3.0", manipulated: declared);

            var r = PairGate.ValidateJson(control, treatment, Schema);

            Assert.IsTrue(r.Ok, "exact indexed leaf declarations should pass: "
                + string.Join(", ", r.Violations.Select(v => v.Code + " " + v.Path)));
            Assert.IsTrue(r.DiffPaths.All(p => p.StartsWith("furniture[")),
                "only the indexed furniture leaves should differ, got: " + string.Join(", ", r.DiffPaths));
        }

        [Test]
        public void Exact_leaf_declaration_still_passes_the_flagship_pair()
        {
            // Guards the common case the whole demo rests on: one exact leaf declared, one leaf
            // differs, pair accepted. (The golden vectors cover this too; kept explicit so a
            // strictness regression names itself here.)
            var control = Pair("control", furnitureCount: 9, ceiling: "3.0",
                manipulated: "\"shell.ceiling_height_m\"");
            var treatment = Pair("treatment", furnitureCount: 9, ceiling: "2.6",
                manipulated: "\"shell.ceiling_height_m\"");

            var r = PairGate.ValidateJson(control, treatment, Schema);

            Assert.IsTrue(r.Ok, "the single-variable ceiling pair must pass: "
                + string.Join(", ", r.Violations.Select(v => v.Code + " " + v.Path)));
            Assert.AreEqual(new[] { "shell.ceiling_height_m" }, r.DiffPaths.ToArray());
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
