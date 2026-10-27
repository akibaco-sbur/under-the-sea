"""Four-finger closure measurements shared by the live worker and benchmarks.

Angles and ratios use predicted world landmarks, not image-plane area. The
measurements are invariant to rigid rotation and uniform scale of those points;
the landmark model itself can still fail under rotation or occlusion.
"""
import argparse
import csv
import gzip
import json
import time
from pathlib import Path

import numpy as np

MAX_CURVED_RATIO = 0.85
MAX_BENT_RATIO = 0.95
MIN_PALM_BEND_DEGREES = 45.0
REQUIRED_CURLED_FINGERS = 4


def points(values):
    return [[float(p.x), float(p.y), float(p.z)] for p in values]


def categories(values):
    return [{"name": p.category_name, "score": float(p.score)} for p in values]


def extract(take, output, model):
    import cv2
    import mediapipe as mp
    with (take / "Frames.csv").open(newline="", encoding="utf-8-sig") as file:
        rows = list(csv.DictReader(file))
    cap = cv2.VideoCapture(str(take / "Kinect.mkv"))
    if not cap.isOpened():
        raise RuntimeError("Could not open recording")
    options = mp.tasks.vision.GestureRecognizerOptions(
        base_options=mp.tasks.BaseOptions(model_asset_path=str(model)),
        running_mode=mp.tasks.vision.RunningMode.VIDEO, num_hands=2)
    started = time.perf_counter()
    processing = 0.0
    count = 0
    output.parent.mkdir(parents=True, exist_ok=True)
    with gzip.open(output, "xt", encoding="utf-8") as dest, mp.tasks.vision.GestureRecognizer.create_from_options(options) as recognizer:
        while count < len(rows):
            ok, frame = cap.read()
            if not ok:
                break
            frameId = int(rows[count]["frame"])
            rgb = cv2.cvtColor(cv2.resize(frame, (640, 360), interpolation=cv2.INTER_NEAREST), cv2.COLOR_BGR2RGB)
            image = mp.Image(image_format=mp.ImageFormat.SRGB, data=np.ascontiguousarray(rgb))
            tick = time.perf_counter()
            result = recognizer.recognize_for_video(image, frameId * 100)
            processing += time.perf_counter() - tick
            hands = []
            for index, hand in enumerate(result.hand_landmarks):
                hands.append({"landmarks": points(hand), "worldLandmarks": points(result.hand_world_landmarks[index]),
                              "gestures": categories(result.gestures[index]), "handedness": categories(result.handedness[index])})
            record = {"frame": frameId, "timestampMs": frameId * 100,
                      "sourceTimestampUs": rows[count]["colorTimestampUs"], "hands": hands}
            dest.write(json.dumps(record, separators=(",", ":")) + "\n")
            count += 1
            if count % 300 == 0:
                print(f"{take.name}: {count}/{len(rows)}", flush=True)
    cap.release()
    print(json.dumps({"take": take.name, "frames": count, "csvRows": len(rows),
                      "elapsedSeconds": time.perf_counter() - started,
                      "recognitionSeconds": processing}), flush=True)


def geometry(hand):
    try:
        p = np.asarray(hand["worldLandmarks"], dtype=float)
    except (KeyError, TypeError, ValueError):
        return None
    if p.shape != (21, 3) or not np.isfinite(p).all():
        return None
    scale = np.linalg.norm(p[9] - p[0])
    if not np.isfinite(scale) or scale < 1e-6:
        return None
    normal = np.cross(p[5] - p[0], p[17] - p[0])
    normalLength = np.linalg.norm(normal)
    if not np.isfinite(normalLength) or normalLength < 1e-6:
        return None
    normal /= normalLength
    folds = []
    reaches = []
    straightness = []
    palmBends = []
    for base in (5, 9, 13, 17):
        # Curl angle is zero when adjacent bone vectors point in the same direction.
        bones = np.diff(p[base:base + 4], axis=0)
        norms = np.linalg.norm(bones, axis=1)
        if not np.isfinite(norms).all() or np.any(norms < 1e-6):
            return None
        cosines = np.sum(bones[:-1] * bones[1:], axis=1) / (norms[:-1] * norms[1:])
        folds.append(float(np.degrees(np.arccos(np.clip(cosines, -1, 1))).sum()))
        reaches.append(float(np.linalg.norm(p[base + 3] - p[base]) / scale))
        straightness.append(float(np.linalg.norm(p[base + 3] - p[base]) / norms.sum()))
        palmBends.append(float(np.degrees(np.arcsin(np.clip(abs(np.dot(normal, bones[0])) / norms[0], 0, 1)))))
    if not np.isfinite(folds + reaches + straightness + palmBends).all():
        return None
    # A shortened bone-chain chord captures knuckle curling. A finger that bends
    # mostly at its base can also close: require both a non-straight chain and
    # >=45 degrees out of the palm plane. Sideways finger spreading stays in it.
    curled = sum(ratio <= MAX_CURVED_RATIO or
                 (ratio <= MAX_BENT_RATIO and angle >= MIN_PALM_BEND_DEGREES)
                 for ratio, angle in zip(straightness, palmBends))
    # Requiring all four avoids accepting an explicitly extended index finger.
    # Thumb pose is not classified; thumbs-up remains a known negative-data gap.
    return {"foldDegrees": folds, "tipToMcp": reaches, "curledFingers": curled,
            "straightness": straightness, "palmBendDegrees": palmBends,
            "fist": curled >= REQUIRED_CURLED_FINGERS}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("take", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--model", type=Path, default=Path(".Local/MediaPipe/gesture_recognizer.task"))
    args = parser.parse_args()
    extract(args.take, args.output, args.model)


if __name__ == "__main__":
    main()
