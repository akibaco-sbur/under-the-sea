"""Replay Kinect RGB frames through the actual local worker, without reading labels.

Use the MediaPipe environment's Python. The take directory must contain Kinect.mkv
and Frames.csv. Target coordinates are normalized to the color image. A targets
CSV has frame,x,y,confidence; optional targetJoint,otherWristX,otherWristY
columns carry the wrist association used by Unity. confidence > 0 enables a target, otherwise the
worker receives an invalid target and returns UNKNOWN. Each recorded frame must
have one target row, including frames skipped by --stride. Output files are never
overwritten. This offline replay does not simulate the live queue or freshness.
"""
import argparse
import csv
import json
import math
import statistics
import struct
import subprocess
import sys
import time
from pathlib import Path

import cv2

ROOT = Path(__file__).resolve().parent.parent
HEADER = struct.Struct('<Iiiiffiff')


def readFrames(take):
    with (take / 'Frames.csv').open(encoding='utf-8-sig', newline='') as file:
        frames = list(csv.DictReader(file))
    if not frames:
        raise ValueError('Frames.csv is empty')
    for index, row in enumerate(frames):
        if int(row['frame']) != index:
            raise ValueError(f'Expected consecutive frame {index}')
        int(row['colorTimestampUs'])
    return frames


def readTargets(path, count, defaultJoint=9, defaultOtherX=-1, defaultOtherY=-1):
    with path.open(encoding='utf-8-sig', newline='') as file:
        rows = list(csv.DictReader(file))
    targets = {}
    for row in rows:
        frame = int(row['frame'])
        x, y, confidence = (float(row[key]) for key in ('x', 'y', 'confidence'))
        if frame in targets or frame < 0 or frame >= count:
            raise ValueError(f'Duplicate or out-of-range target frame {frame}')
        if not all(math.isfinite(v) for v in (x, y, confidence)):
            raise ValueError(f'Non-finite target at frame {frame}')
        if confidence > 0 and not (0 <= x <= 1 and 0 <= y <= 1):
            raise ValueError(f'Target outside image at frame {frame}')
        joint = int(row.get('targetJoint') or defaultJoint)
        otherX = float(row.get('otherWristX') or defaultOtherX)
        otherY = float(row.get('otherWristY') or defaultOtherY)
        if joint not in (0, 9) or not all(math.isfinite(v) for v in (otherX, otherY)):
            raise ValueError(f'Invalid association metadata at frame {frame}')
        targets[frame] = (x, y, joint, otherX, otherY) if confidence > 0 else (-1.0, -1.0, joint, -1.0, -1.0)
    if len(targets) != count:
        raise ValueError(f'Target CSV has {len(targets)} rows; expected {count}')
    return targets


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('take', type=Path)
    parser.add_argument('output_dir', type=Path)
    parser.add_argument('--stride', type=int, default=1)
    parser.add_argument('--width', type=int, default=1280, help='Input width; 1280 matches live capture, 640 reproduces earlier experiments')
    parser.add_argument('--target-x', type=float)
    parser.add_argument('--target-y', type=float)
    parser.add_argument('--target-joint', type=int, choices=(0, 9), default=9, help='0 matches wrist, 9 matches palm')
    parser.add_argument('--other-wrist-x', type=float, default=-1)
    parser.add_argument('--other-wrist-y', type=float, default=-1)
    parser.add_argument('--targets-csv', type=Path)
    args = parser.parse_args()
    if args.stride < 1:
        parser.error('--stride must be positive')
    if args.width not in (640, 1280):
        parser.error('--width must be 640 or 1280')
    if args.targets_csv:
        if args.target_x is not None or args.target_y is not None:
            parser.error('choose --targets-csv or both --target-x and --target-y')
    elif args.target_x is None or args.target_y is None or not all(
            math.isfinite(v) and 0 <= v <= 1 for v in (args.target_x, args.target_y)):
        parser.error('provide normalized --target-x and --target-y, or --targets-csv')
    frames = readFrames(args.take)
    if not all(math.isfinite(v) for v in (args.other_wrist_x, args.other_wrist_y)):
        parser.error('other wrist coordinates must be finite')
    targets = readTargets(args.targets_csv, len(frames), args.target_joint, args.other_wrist_x, args.other_wrist_y) if args.targets_csv else None
    args.output_dir.mkdir(parents=True, exist_ok=True)
    # Reserve every output before starting; exclusive creation protects prior results.
    paths = [args.output_dir / name for name in ('Predictions.csv', 'Timing.json', 'WorkerStderr.log')]
    if any(path.exists() for path in paths):
        raise FileExistsError('Output exists; choose a different output directory')
    with paths[0].open('x', encoding='utf-8', newline='') as csvFile, \
         paths[1].open('x', encoding='utf-8') as timingFile, \
         paths[2].open('x', encoding='utf-8') as errorFile:
        cap = cv2.VideoCapture(str(args.take / 'Kinect.mkv'))
        if not cap.isOpened():
            raise RuntimeError('Could not open Kinect.mkv')
        started = time.perf_counter()
        worker = subprocess.Popen([sys.executable, '-u', str(ROOT / 'Tools/MediaPipeHandWorker.py'),
                                   str(ROOT / '.Local/MediaPipe/gesture_recognizer.task')],
                                  stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=errorFile)
        spawned = time.perf_counter()
        output, latencies = [], []
        firstResponse = None
        try:
            for index, row in enumerate(frames):
                ok, frame = cap.read()
                if not ok:
                    raise RuntimeError(f'Video ended early at frame {index}')
                if index % args.stride:
                    continue
                width, height = args.width, args.width * 9 // 16
                rgba = cv2.cvtColor(cv2.resize(frame, (width, height), interpolation=cv2.INTER_NEAREST), cv2.COLOR_BGR2RGBA)
                x, y, targetJoint, otherX, otherY = targets[index] if targets is not None else (args.target_x, args.target_y, args.target_joint, args.other_wrist_x, args.other_wrist_y)
                sequence = len(output) + 1
                tick = time.perf_counter()
                worker.stdin.write(HEADER.pack(sequence, width, height, rgba.nbytes, x, y, targetJoint, otherX, otherY))
                worker.stdin.write(rgba.tobytes())
                worker.stdin.flush()
                line = worker.stdout.readline()
                ended = time.perf_counter()
                if not line:
                    raise RuntimeError(f'Worker stopped at frame {index}')
                result = json.loads(line)
                if result['frameId'] != sequence:
                    raise RuntimeError(f'Worker frame mismatch at frame {index}')
                if firstResponse is None:
                    firstResponse = ended - started
                latencies.append((ended - tick) * 1000)
                label, score = result['label'], result['score']
                old = 'GRAB' if label == 'Closed_Fist' and score >= .65 else ('NON_GRAB' if label == 'Open_Palm' and score >= .65 else 'UNKNOWN')
                predicted = ('GRAB' if result['fist'] else 'NON_GRAB') if result['geometryValid'] else 'UNKNOWN'
                output.append(dict(frame=index, colorTimestampUs=row['colorTimestampUs'], predicted=predicted,
                                   oldPredicted=old, label=label, score=score, distance=result['distance'],
                                   geometryValid=result['geometryValid'], fist=result['fist'], curledFingers=result['curledFingers'],
                                   source=result.get('source'), reason=result.get('reason'), fullReason=result.get('fullReason'),
                                   handCount=result.get('handCount'), nearestDistance=result.get('nearestDistance')))
                if sequence % 300 == 0:
                    print(f'Processed {sequence} frames', flush=True)
            if cap.read()[0]:
                raise RuntimeError('Video has more frames than Frames.csv')
        finally:
            cap.release()
            worker.stdin.close()
            try:
                worker.wait(timeout=20)
            except subprocess.TimeoutExpired:
                worker.kill()
                worker.wait()
                raise RuntimeError('Worker did not exit after EOF')
        if worker.returncode:
            raise RuntimeError(f'Worker exited with code {worker.returncode}')
        expected = (len(frames) + args.stride - 1) // args.stride
        if len(output) != expected:
            raise RuntimeError(f'Expected {expected} responses, got {len(output)}')
        writer = csv.DictWriter(csvFile, fieldnames=output[0].keys())
        writer.writeheader()
        writer.writerows(output)
        summary = dict(decoded=len(frames), processed=len(output), stride=args.stride, width=args.width,
                       startupFirstResponseSeconds=firstResponse, spawnSeconds=spawned-started,
                       wallSeconds=time.perf_counter()-started,
                       roundtripMedianMs=statistics.median(latencies),
                       roundtripP95Ms=sorted(latencies)[int(.95*(len(latencies)-1))],
                       roundtripMeanMs=statistics.mean(latencies))
        json.dump(summary, timingFile, indent=2)
        timingFile.write('\n')
        print(json.dumps(summary), flush=True)


if __name__ == '__main__':
    main()
