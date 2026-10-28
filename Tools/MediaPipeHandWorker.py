"""Read synchronized color frames from stdin and return hand results on stdout.

The parent controls camera access. Frames are processed in memory and never saved.
"""
import json
import struct
import sys
from types import SimpleNamespace

import mediapipe as mp
import numpy as np

from HandPoseGeometry import geometry, points, MAX_CURVED_RATIO, MAX_BENT_RATIO, MIN_PALM_BEND_DEGREES, REQUIRED_CURLED_FINGERS

HEADER = struct.Struct("<Iiiiffiff")
MATCH_RADIUS = 0.20
# Two percentage points of image width/height provide a small deadband around
# competing matches; it is an ambiguity guard, not a measured identity guarantee.
IDENTITY_MARGIN = 0.02


def predict(result, frameId, targetX, targetY, targetJoint=9, otherWristX=-1, otherWristY=-1):
    output = {"frameId": frameId, "label": "Unknown", "score": 0.0, "distance": -1.0,
              "nearestDistance": -1.0, "handCount": len(result.hand_landmarks), "reason": "NoHand", "source": "Full",
              "landmarks": [], "geometryValid": False, "fist": False, "curledFingers": 0,
              "foldDegrees": [], "tipToMcp": [], "straightness": [], "palmBendDegrees": [],
              "maxCurvedRatio": MAX_CURVED_RATIO, "maxBentRatio": MAX_BENT_RATIO,
              "minPalmBendDegrees": MIN_PALM_BEND_DEGREES, "requiredCurledFingers": REQUIRED_CURLED_FINGERS}
    if targetJoint not in (0, 9) or not np.isfinite([targetX, targetY]).all() or not (0 <= targetX <= 1 and 0 <= targetY <= 1):
        output["reason"] = "InvalidTarget"
        return output
    otherValid = np.isfinite([otherWristX, otherWristY]).all() and 0 <= otherWristX <= 1 and 0 <= otherWristY <= 1
    if otherValid and np.hypot(targetX-otherWristX, targetY-otherWristY) <= IDENTITY_MARGIN:
        output["reason"] = "Ambiguous"
        return output
    candidates = []
    nearest = float('inf')
    rejectedOther = False
    for index, landmarks in enumerate(result.hand_landmarks):
        if len(landmarks) != 21:
            continue
        anchor, wrist = landmarks[targetJoint], landmarks[0]
        distance = float(np.hypot(anchor.x - targetX, anchor.y - targetY))
        if not np.isfinite(distance):
            continue
        nearest = min(nearest, distance)
        if distance > MATCH_RADIUS:
            continue
        if otherValid:
            otherDistance = float(np.hypot(wrist.x-otherWristX, wrist.y-otherWristY))
            if not np.isfinite(otherDistance):
                continue
            if otherDistance + IDENTITY_MARGIN < distance:
                rejectedOther = True
                continue
        candidates.append((distance, index, landmarks))
    if np.isfinite(nearest):
        output["nearestDistance"] = nearest
    if not candidates:
        output["reason"] = "OtherHand" if rejectedOther else ("OutsideTarget" if np.isfinite(nearest) else "NoHand")
        return output
    candidates.sort(key=lambda item: item[0])
    if len(candidates) > 1 and candidates[1][0] - candidates[0][0] <= IDENTITY_MARGIN:
        output["reason"] = "Ambiguous"
        return output
    distance, index, landmarks = candidates[0]
    normalized = points(landmarks)
    if not np.isfinite(normalized).all():
        output["reason"] = "InvalidGeometry"
        return output
    output["distance"] = distance
    output["landmarks"] = [v for p in normalized for v in p[:2]]
    if index < len(result.gestures) and result.gestures[index]:
        category = result.gestures[index][0]
        output["label"] = category.category_name or "Unknown"
        output["score"] = float(category.score) if np.isfinite(category.score) else 0.0
    if index < len(result.hand_world_landmarks):
        measured = geometry({"worldLandmarks": points(result.hand_world_landmarks[index])})
        if measured is not None:
            output.update(measured)
            output["geometryValid"] = True
            output["reason"] = "Matched"
            return output
    output["reason"] = "InvalidGeometry"
    return output


def cropResult(result, left, top, side, width, height):
    # Crop landmarks use square ROI coordinates; restore x/y before association.
    hands = []
    for hand in result.hand_landmarks:
        hands.append([SimpleNamespace(x=(left + p.x * side) / width,
                                      y=(top + p.y * side) / height, z=p.z) for p in hand])
    return SimpleNamespace(hand_landmarks=hands, hand_world_landmarks=result.hand_world_landmarks,
                           gestures=result.gestures)


def readExact(stream, count):
    chunks = []
    while count:
        chunk = stream.read(count)
        if not chunk:
            return None
        chunks.append(chunk)
        count -= len(chunk)
    return b"".join(chunks)


def main():
    base = mp.tasks.BaseOptions(model_asset_path=sys.argv[1])
    options = mp.tasks.vision.GestureRecognizerOptions(base_options=base,
        running_mode=mp.tasks.vision.RunningMode.VIDEO, num_hands=2)
    cropOptions = mp.tasks.vision.GestureRecognizerOptions(base_options=base,
        running_mode=mp.tasks.vision.RunningMode.IMAGE, num_hands=2)
    with mp.tasks.vision.GestureRecognizer.create_from_options(options) as recognizer, \
         mp.tasks.vision.GestureRecognizer.create_from_options(cropOptions) as cropRecognizer:
        stream = sys.stdin.buffer
        while True:
            header = readExact(stream, HEADER.size)
            if header is None:
                return
            frameId, width, height, length, targetX, targetY, targetJoint, otherX, otherY = HEADER.unpack(header)
            if width <= 0 or height <= 0 or length != width * height * 4 or length > 16_000_000:
                raise ValueError("Invalid color frame dimensions")
            payload = readExact(stream, length)
            if payload is None:
                return
            rgba = np.frombuffer(payload, dtype=np.uint8).reshape(height, width, 4)
            rgb = np.ascontiguousarray(rgba[:, :, :3])
            image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
            result = recognizer.recognize_for_video(image, int(frameId) * 100)
            output = predict(result, frameId, targetX, targetY, targetJoint, otherX, otherY)
            # Cropping cannot resolve a known identity conflict; avoid the extra
            # inference as well as avoiding a misleading one-hand crop decision.
            if not output["geometryValid"] and output["reason"] not in ("InvalidTarget", "Ambiguous"):
                side = max(1, min(width, height) // 2)
                left = max(0, min(width-side, round(targetX*width-side/2)))
                top = max(0, min(height-side, round(targetY*height-side/2)))
                cropped = np.ascontiguousarray(rgb[top:top+side, left:left+side])
                cropImage = mp.Image(image_format=mp.ImageFormat.SRGB, data=cropped)
                crop = cropResult(cropRecognizer.recognize(cropImage), left, top, side, width, height)
                fallback = predict(crop, frameId, targetX, targetY, targetJoint, otherX, otherY)
                fallback["source"] = "Crop"
                fallback["fullReason"] = output["reason"]
                output = fallback
            print(json.dumps(output, separators=(",", ":"), allow_nan=False), flush=True)


if __name__ == "__main__":
    main()
