"""Fixture honesty tests — keep the golden fixtures true forever.

1. diff_vectors.json must reproduce EXACTLY through the reference validator
   (tools/validate_pair.py). If this fails, either the validator changed
   behavior (contract change — follow the COORDINATION.md rule and regenerate)
   or the fixture was hand-edited (never do that).
2. response_rows.json valid rows must pass response_log.schema.json; invalid
   rows must fail.
3. Embedded fixture specs must themselves be schema-valid where expected.
4. seam_messages.json structural sanity (full seam behavior is tested in
   Unity EditMode against the same file).
"""

import json
from pathlib import Path

import pytest
from jsonschema import Draft202012Validator

from validate_pair import validate_pair

ROOT = Path(__file__).resolve().parent.parent
FIXTURES = ROOT / "spec" / "fixtures"

room_schema = json.loads((ROOT / "spec" / "room_spec.schema.json").read_text(encoding="utf-8"))
response_schema = json.loads((ROOT / "spec" / "response_log.schema.json").read_text(encoding="utf-8"))
study_schema = json.loads((ROOT / "spec" / "study.schema.json").read_text(encoding="utf-8"))
diff_vectors = json.loads((FIXTURES / "diff_vectors.json").read_text(encoding="utf-8"))
response_rows = json.loads((FIXTURES / "response_rows.json").read_text(encoding="utf-8"))
seam_messages = json.loads((FIXTURES / "seam_messages.json").read_text(encoding="utf-8"))


# ---------- diff vectors reproduce through the reference validator ----------

@pytest.mark.parametrize("case", diff_vectors["cases"], ids=lambda c: c["name"])
def test_diff_vector_reproduces(case):
    result = validate_pair(case["spec_a"], case["spec_b"], room_schema)
    assert result["ok"] == case["expected"]["ok"], case["name"]
    assert sorted({v["code"] for v in result["violations"]}) == case["expected"]["violation_codes"]
    assert sorted(result["diff"].keys()) == case["expected"]["diff_paths"]
    assert len(result["notes"]) == case["expected"]["notes_count"]


def test_diff_vectors_cover_every_violation_code():
    """The fixture set must exercise the full frozen vocabulary."""
    covered = set()
    for case in diff_vectors["cases"]:
        covered.update(case["expected"]["violation_codes"])
    frozen = {
        "schema_invalid", "missing_experiment", "pair_id_mismatch",
        "condition_invalid", "declared_mismatch", "undeclared_change",
        "declared_unchanged",
    }
    assert covered == frozen


def test_diff_vectors_has_a_passing_case():
    assert any(c["expected"]["ok"] for c in diff_vectors["cases"])


# ---------- response rows ----------

response_validator = Draft202012Validator(response_schema)


@pytest.mark.parametrize("entry", response_rows["valid"], ids=lambda e: e["name"])
def test_valid_response_row(entry):
    errors = list(response_validator.iter_errors(entry["row"]))
    assert errors == [], f"{entry['name']}: {[e.message for e in errors]}"


@pytest.mark.parametrize("entry", response_rows["invalid"], ids=lambda e: e["name"])
def test_invalid_response_row(entry):
    errors = list(response_validator.iter_errors(entry["row"]))
    assert errors, f"{entry['name']} should fail ({entry['reason']}) but passed"


# ---------- schemas are themselves valid + embedded specs conform ----------

def test_schemas_are_valid_jsonschema():
    for schema in (room_schema, response_schema, study_schema):
        Draft202012Validator.check_schema(schema)


def test_seam_fixture_specs_are_schema_valid():
    """RoomSpecs embedded in valid seam messages must pass the room schema."""
    room_validator = Draft202012Validator(room_schema)
    for entry in seam_messages["valid"]:
        payload = entry["message"].get("payload", {})
        if "spec" in payload:
            errors = list(room_validator.iter_errors(payload["spec"]))
            assert errors == [], f"{entry['name']}: {[e.message for e in errors]}"


# ---------- seam message structural sanity ----------

def test_seam_messages_shape():
    for entry in seam_messages["valid"]:
        msg = entry["message"]
        assert msg["seam_version"] == 1
        assert isinstance(msg["kind"], str) and msg["kind"]
        assert "payload" in msg
    for entry in seam_messages["invalid"]:
        assert entry["expect_error_code"] in {
            "unsupported_seam_version", "bad_request",
        }, entry["name"]
