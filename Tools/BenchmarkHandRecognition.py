"""Score immutable frame labels against offline prediction CSVs, without interpolation.

Examples and interpretation: Docs/HandRecognitionBenchmark.md.
"""
import argparse
import csv
import hashlib
import json
from pathlib import Path


def rows(path):
    with path.open(encoding="utf-8-sig", newline="") as f:
        result = list(csv.DictReader(f))
    ids = [int(row["frame"]) for row in result]
    if len(ids) != len(set(ids)):
        raise ValueError(f"duplicate frame in {path}")
    return {int(row["frame"]): row for row in result}


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def state(row):
    # Existing sampled CSVs have predicted; full probe CSV has gesture and score.
    if "predicted" in row:
        value = row["predicted"].strip().upper()
        mapping = {"CLOSED": "GRAB", "GRAB": "GRAB", "OPEN": "NON_GRAB", "NON_GRAB": "NON_GRAB", "UNKNOWN": "UNKNOWN"}
        if value not in mapping:
            raise ValueError(f"unrecognized predicted state: {value!r}")
        return mapping[value]
    gesture = row["gesture"]
    score = float(row["score"])
    if score >= 0.65 and gesture == "Closed_Fist":
        return "GRAB"
    if score >= 0.65 and gesture == "Open_Palm":
        return "NON_GRAB"
    return "UNKNOWN"


def ratio(num, den):
    return None if not den else round(num / den, 6)


def summarize(entries):
    pos = [e for e in entries if e["label"] == "CLOSED"]
    neg = [e for e in entries if e["label"] == "OPEN"]
    n = len(entries)
    grabs = sum(e["prediction"] == "GRAB" for e in pos)
    false = sum(e["prediction"] == "GRAB" for e in neg)
    unknown = sum(e["prediction"] == "UNKNOWN" for e in entries)
    return {"n": n, "closed": len(pos), "open": len(neg),
            "closed_grab": grabs, "open_grab": false,
            "unknown": unknown, "coverage": ratio(n - unknown, n),
            "grab_recall": ratio(grabs, len(pos)),
            "false_grab_rate": ratio(false, len(neg))}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--frames", type=Path, required=True)
    parser.add_argument("--labels", type=Path, help="ReviewedLabels.csv; explicit frame labels only")
    parser.add_argument("--provisional-probe", action="store_true", help="Use instructed PROBE labels (not ground truth)")
    parser.add_argument("--prediction", type=Path, action="append", required=True, help="One CSV per method; identical scoring frames required")
    parser.add_argument("--output", type=Path, required=True, help="Output JSON; never overwrite")
    args = parser.parse_args()
    if bool(args.labels) == args.provisional_probe:
        parser.error("choose exactly one of --labels and --provisional-probe")
    frames = rows(args.frames)
    labels = rows(args.labels) if args.labels else {i: r for i, r in frames.items() if r.get("stage") == "PROBE"}
    entries = []
    excluded = 0
    for frame, label in labels.items():
        if frame not in frames:
            raise ValueError(f"label frame {frame} is absent from Frames.csv")
        source = frames[frame]
        for key in ("colorTimestampUs",):
            if label.get(key) and source.get(key) and label[key] != source[key]:
                raise ValueError(f"timestamp mismatch at frame {frame}")
        actual = label.get("reviewedLabel", "") if args.labels else label.get("suggestedLabel", "")
        if actual == "UNSCORED":
            excluded += 1
            continue
        if actual not in ("OPEN", "CLOSED"):
            raise ValueError(f"invalid label {actual!r} at frame {frame}")
        entries.append({"frame": frame, "label": actual,
                        "condition": label.get("condition") or source.get("condition") or "unspecified"})
    if not entries:
        raise ValueError("no scored frames")
    report = {"label_provenance": "visual reviewed samples" if args.labels else "provisional instructed PROBE; frame labels unverified",
              "label_file_sha256": sha256(args.labels or args.frames), "frames_sha256": sha256(args.frames),
              "excluded_unscored": excluded, "methods": {}}
    for path in args.prediction:
        pred = rows(path)
        missing = [e["frame"] for e in entries if e["frame"] not in pred]
        if missing:
            raise ValueError(f"{path}: missing {len(missing)} scored predictions, first {missing[:5]}")
        evaluated = []
        for e in entries:
            row = pred[e["frame"]]
            if row.get("colorTimestampUs") and row["colorTimestampUs"] != frames[e["frame"]].get("colorTimestampUs"):
                raise ValueError(f"prediction timestamp mismatch at frame {e['frame']}")
            evaluated.append({**e, "prediction": state(row)})
        conditions = sorted({e["condition"] for e in evaluated})
        report["methods"][str(path)] = {"prediction_sha256": sha256(path),
            "overall": summarize(evaluated),
            "conditions": {condition: summarize([e for e in evaluated if e["condition"] == condition]) for condition in conditions}}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open("x", encoding="utf-8") as f:
        json.dump(report, f, ensure_ascii=False, indent=2)
        f.write("\n")
    print(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
