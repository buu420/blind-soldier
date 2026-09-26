"""Join the original guide checklist to an exact native route-audit report.

This produces an index for review, not a whole-game pass. Raw native game data
and the downloaded guide are not inputs and are never written into this file.
"""
import argparse
from collections import Counter
import json
from pathlib import Path


def field_ids(spec):
    result = set()
    for part in spec.split(","):
        ends = [int(value) for value in part.split("-")]
        result.update(range(ends[0], ends[-1] + 1))
    return result


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("report", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    guide = json.loads(Path(__file__).with_name("guide-chapters.json").read_text(encoding="utf-8"))
    audit = json.loads(args.report.read_text(encoding="utf-8"))
    fields = {f["id"]: f for f in audit["fields"]}
    if len(guide["chapters"]) != 43 or [c["order"] for c in guide["chapters"]] != list(range(2, 45)):
        raise ValueError("The chapter ledger must retain all 43 chronological chapters in order.")
    rows = []
    for chapter in guide["chapters"]:
        scope = field_ids(chapter["fields"])
        native_fields = [fields[f] for f in sorted(scope) if f in fields]
        targets = [t for f in native_fields for t in f["targets"]]
        if not native_fields or not targets:
            raise ValueError(f"Chapter {chapter['order']} has no native audit witnesses.")
        categories = Counter(t["Category"] for t in targets)
        assessments = Counter(t["summary"] for t in targets)
        candidates = [
            {"field": f["id"], "name": f["name"], "target": t["Id"], "label": t["Label"],
             "category": t["Category"], "assessment": t["summary"],
             "staticDetourWitnesses": sum(bool(a.get("staticOutAndBack")) for a in t["attempts"])}
            for f in native_fields for t in f["targets"]
            if t["summary"] in ("unreachable", "partial-geometry", "unverified", "no-native-arrivals")]
        rows.append({**chapter, "nativeFields": [f["id"] for f in native_fields],
                     "targetPositions": len(targets), "categories": dict(categories),
                     "assessments": dict(assessments), "reviewCandidates": candidates,
                     "liveCompletionCertified": False})

    result = {"schema": 1, "runtime": audit["runtime"], "assemblySha256": audit["assemblySha256"],
              "archive": audit["archive"], "chapters": rows,
              "scope": "Every chapter read and mapped to native fields. Counts are target-position witnesses, not independent quests or verified live routes. Overlapping chapters repeat fields intentionally.",
              "checkpointCount": sum(len(c["checkpoints"]) for c in rows)}
    args.output.with_suffix(".json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    text = ["# Guide route audit ledger", "",
            f"All 43 walkthrough chapters were read; {result['checkpointCount']} original checkpoints are recorded below.", "",
            "This is a review index, not an assertion that every route can be completed in game. "
            "Each chapter maps to the native fields tested by the collector. Static geometry cannot establish "
            "which NPC is visible, which line is enabled, or which puzzle state the player has reached. "
            "A failed entrance remains visible even when another entrance works. Candidate counts include "
            "hidden actor positions, automatic entry movements and optional puzzle states; they are not bug counts.", "",
            f"Runtime: {audit['runtime']}; assembly SHA-256: `{audit['assemblySha256']}`.", "",
            "| Chapter | Fields read | Target positions | Ordinary geometry | Script-assisted geometry | Manual activity guidance | Candidates needing state review |",
            "|---|---:|---:|---:|---:|---:|---:|"]
    for c in rows:
        a = c["assessments"]
        text.append(f"| [{c['title']}]({c['url']}) | {len(c['nativeFields'])} | {c['targetPositions']} | "
                    f"{a.get('geometry-from-all-arrivals',0)} | {a.get('script-assisted-geometry',0)} | "
                    f"{a.get('manual-interaction',0)} | {len(c['reviewCandidates'])} |")
    for c in rows:
        text += ["", f"## {c['title']}", "", f"[Guide chapter]({c['url']}); native field scope: `{c['fields']}`.", ""]
        text += [f"- {point}." for point in c["checkpoints"]]
    args.output.write_text("\n".join(text) + "\n", encoding="utf-8")
    print(json.dumps({"chapters": len(rows), "checkpoints": result["checkpointCount"],
                      "fieldsMapped": len(set(f for c in rows for f in c["nativeFields"])),
                      "output": str(args.output)}))


if __name__ == "__main__":
    main()
