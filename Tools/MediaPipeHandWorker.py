"""Read synchronized color frames from stdin and return hand results on stdout.

The parent controls camera access. Frames are processed in memory and never saved.
"""
import json
import struct
import sys

import mediapipe as mp
import numpy as np

HEADER = struct.Struct("<Iiiiff")


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
    options = mp.tasks.vision.GestureRecognizerOptions(
        base_options=mp.tasks.BaseOptions(model_asset_path=sys.argv[1]),
        running_mode=mp.tasks.vision.RunningMode.VIDEO,
        num_hands=2,
    )
    with mp.tasks.vision.GestureRecognizer.create_from_options(options) as recognizer:
        stream = sys.stdin.buffer
        while True:
            header = readExact(stream, HEADER.size)
            if header is None:
                return
            frameId, width, height, length, targetX, targetY = HEADER.unpack(header)
            if width <= 0 or height <= 0 or length != width * height * 4 or length > 16_000_000:
                raise ValueError("Invalid color frame dimensions")
            payload = readExact(stream, length)
            if payload is None:
                return
            rgba = np.frombuffer(payload, dtype=np.uint8).reshape(height, width, 4)
            # MediaPipe image owns a contiguous RGB array. No image is written to disk.
            rgb = np.ascontiguousarray(rgba[:, :, :3])
            image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
            result = recognizer.recognize_for_video(image, int(frameId) * 100)
            best = None
            for index, landmarks in enumerate(result.hand_landmarks):
                # Match hand center, not handedness: mirror conventions vary across inputs.
                palm = landmarks[9]
                distance = ((palm.x - targetX) ** 2 + (palm.y - targetY) ** 2) ** 0.5
                if distance > 0.20 or (best is not None and distance >= best[0]):
                    continue
                best = (distance, index, landmarks)
            output = {"frameId": frameId, "label": "Unknown", "score": 0.0, "distance": -1.0, "landmarks": []}
            if best is not None:
                distance, index, landmarks = best
                output["distance"] = distance
                output["landmarks"] = [v for p in landmarks for v in (p.x, p.y)]
                if index < len(result.gestures) and result.gestures[index]:
                    category = result.gestures[index][0]
                    output["label"] = category.category_name or "Unknown"
                    output["score"] = float(category.score)
            print(json.dumps(output, separators=(",", ":")), flush=True)


if __name__ == "__main__":
    main()
