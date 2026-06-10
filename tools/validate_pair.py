#!/usr/bin/env python3
"""
validate_pair.py — the single-variable isolation gate (the diff-validator).

Phase 1 deliverable (PLAN.md guardrail #2): a control/treatment RoomSpec pair
is only accepted if the two specs differ in EXACTLY the declared
manipulated_variables — nothing more, nothing less.

Checks
------
1. Each spec validates against spec/room_spec.schema.json.
2. The experiment blocks are coherent: shared pair_id, exactly one control +
   one treatment, identical manipulated_variables lists.
3. The stimulus diff (everything outside experiment/provenance) is covered by
   the declared variables, and every declared variable actually differs.

Violation codes
---------------
    schema_invalid      — spec fails room_spec.schema.json
    missing_experiment  — spec lacks the experiment fields a pair needs
    pair_id_mismatch    — the two specs do not share a pair_id
    condition_invalid   — pair is not exactly one control + one treatment
    declared_mismatch   — manipulated_variables differ between the two specs
    undeclared_change   — a stimulus field differs that was not declared
    declared_unchanged  — a declared variable does not actually differ

CLI
---
    py tools/validate_pair.py <control.spec.json> <treatment.spec.json> [--schema PATH]
        Exit 0 → PASS
        Exit 1 → violations (printed as structured JSON to stdout)
        Exit 2 → CLI / IO error (printed to stderr)

Library
-------
    from validate_pair import validate_pair
    result = validate_pair(spec_a, spec_b, schema)
    # result is {"ok": bool, "violations": [...], "diff": {path: [a, b]}, "notes": [...]}
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any

from jsonschema import Draft202012Validator

# Fields that are allowed (or required) to differ within a pair: they describe
# the experiment and the file's history, not the stimulus a participant sees.
NON_STIMULUS_PREFIXES = ("experiment", "provenance")

# Guardrail #2's coupled-variable documentation, in executable form: changing
# the key inevitably changes the listed properties too. Reported as notes, not
# violations — the researcher must account for them, the gate can't decouple them.
COUPLED_VARS = {
    "shell.ceiling_height_m": "also changes room volume and the window-to-wall ratio",
    "shell.contour": "also changes floor area near corners and total wall arc length",
    "shell.width_m": "also changes room volume and aspect ratio",
    "shell.length_m": "also changes room volume and aspect ratio",
    "lighting.warmth": "perceived brightness shifts with color temperature",
    "lighting.intensity": "shadow depth and apparent texture contrast shift with brightness",
}

MISSING = object()  # sentinel for "path absent in this spec"


def _violation(code: str, path: str | None, message: str) -> dict[str, Any]:
    return {"code": code, "path": path, "message": message}


def flatten(node: Any, prefix: str = "") -> dict[str, Any]:
    """Flatten nested JSON into {dotted.path or list[i]: leaf_value}."""
    if isinstance(node, dict):
        out: dict[str, Any] = {}
        for key, value in node.items():
            out.update(flatten(value, f"{prefix}.{key}" if prefix else key))
        return out
    if isinstance(node, list):
        out = {}
        for i, value in enumerate(node):
            out.update(flatten(value, f"{prefix}[{i}]"))
        return out
    return {prefix: node}


def _is_covered(path: str, declared: list[str]) -> bool:
    """A declared variable covers its own path and everything nested under it."""
    return any(
        path == var or path.startswith(var + ".") or path.startswith(var + "[")
        for var in declared
    )


def stimulus_diff(spec_a: dict, spec_b: dict) -> dict[str, list[Any]]:
    """All leaf paths (outside experiment/provenance) whose values differ."""
    flat_a, flat_b = flatten(spec_a), flatten(spec_b)
    diff: dict[str, list[Any]] = {}
    for path in sorted(set(flat_a) | set(flat_b)):
        if path.split(".")[0].split("[")[0] in NON_STIMULUS_PREFIXES:
            continue
        a, b = flat_a.get(path, MISSING), flat_b.get(path, MISSING)
        if a is not b and a != b:
            diff[path] = [None if a is MISSING else a, None if b is MISSING else b]
    return diff


def validate_pair(spec_a: dict, spec_b: dict, schema: dict) -> dict[str, Any]:
    violations: list[dict[str, Any]] = []
    validator = Draft202012Validator(schema)

    for label, spec in (("spec_a", spec_a), ("spec_b", spec_b)):
        for err in validator.iter_errors(spec):
            violations.append(_violation("schema_invalid", f"{label}:{err.json_path}", err.message))
    if violations:
        return {"ok": False, "violations": violations, "diff": {}, "notes": []}

    exp_a, exp_b = spec_a.get("experiment", {}), spec_b.get("experiment", {})
    for label, exp in (("spec_a", exp_a), ("spec_b", exp_b)):
        for field in ("condition", "manipulated_variables", "pair_id"):
            if field not in exp:
                violations.append(_violation(
                    "missing_experiment", f"{label}:experiment.{field}",
                    f"a pair spec must declare experiment.{field}"))
    if violations:
        return {"ok": False, "violations": violations, "diff": {}, "notes": []}

    if exp_a["pair_id"] != exp_b["pair_id"]:
        violations.append(_violation(
            "pair_id_mismatch", "experiment.pair_id",
            f"{exp_a['pair_id']!r} vs {exp_b['pair_id']!r} — these specs are not a pair"))

    conditions = {exp_a["condition"], exp_b["condition"]}
    if conditions != {"control", "treatment"}:
        violations.append(_violation(
            "condition_invalid", "experiment.condition",
            f"pair must be exactly one control + one treatment, got {sorted(conditions)}"))

    if sorted(exp_a["manipulated_variables"]) != sorted(exp_b["manipulated_variables"]):
        violations.append(_violation(
            "declared_mismatch", "experiment.manipulated_variables",
            f"{exp_a['manipulated_variables']} vs {exp_b['manipulated_variables']}"))
        return {"ok": False, "violations": violations, "diff": {}, "notes": []}

    declared = exp_a["manipulated_variables"]
    diff = stimulus_diff(spec_a, spec_b)

    for path, (a, b) in diff.items():
        if not _is_covered(path, declared):
            violations.append(_violation(
                "undeclared_change", path,
                f"differs ({a!r} vs {b!r}) but is not in manipulated_variables — "
                f"the pair is confounded"))

    for var in declared:
        if not any(_is_covered(path, [var]) for path in diff):
            violations.append(_violation(
                "declared_unchanged", var,
                "declared as manipulated but identical in both specs"))

    notes = [f"coupled: {var} {COUPLED_VARS[var]}" for var in declared if var in COUPLED_VARS]
    return {"ok": not violations, "violations": violations, "diff": diff, "notes": notes}


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Validate a control/treatment RoomSpec pair.")
    parser.add_argument("spec_a", type=Path)
    parser.add_argument("spec_b", type=Path)
    parser.add_argument("--schema", type=Path,
                        default=Path(__file__).resolve().parent.parent / "spec" / "room_spec.schema.json")
    args = parser.parse_args(argv)

    try:
        spec_a = json.loads(args.spec_a.read_text(encoding="utf-8"))
        spec_b = json.loads(args.spec_b.read_text(encoding="utf-8"))
        schema = json.loads(args.schema.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2

    result = validate_pair(spec_a, spec_b, schema)
    if result["ok"]:
        print(f"PASS — pair differs only in: {', '.join(result['diff']) or '(nothing)'}")
        for note in result["notes"]:
            print(f"note: {note}")
        return 0
    print(json.dumps({"ok": False, "violations": result["violations"]}, indent=2))
    return 1


if __name__ == "__main__":
    sys.exit(main())
