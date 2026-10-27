"""Synthetic geometry and worker contract tests; no camera or recording required."""
import io
import json
import math
import struct
import unittest
from unittest.mock import patch
from types import SimpleNamespace

import numpy as np

from HandPoseGeometry import geometry
import MediaPipeHandWorker as worker
from MediaPipeHandWorker import predict, cropResult


def skeleton(extendedIndex=None):
    p = np.zeros((21, 3), dtype=float)
    p[0] = (0, 0, 0)
    for index, base in enumerate((5, 9, 13, 17)):
        origin = np.array([(index - 1) * 0.3, 1.0, 0.0])
        p[base] = origin
        if index == extendedIndex:
            offsets = ((0, .4, 0), (0, .8, 0), (0, 1.2, 0))
        else:
            offsets = ((0, .4, 0), (.4, .4, 0), (.4, 0, 0))
        p[base + 1:base + 4] = origin + np.array(offsets)
    return p


def landmarkList(values):
    return [SimpleNamespace(x=float(p[0]), y=float(p[1]), z=float(p[2])) for p in values]


def resultFor(world=None, palm=(.45, .35), gesture=None):
    normalized = np.full((21, 3), .1)
    normalized[9] = (palm[0], palm[1], 0)
    if world is None:
        world = skeleton()
    return SimpleNamespace(hand_landmarks=[landmarkList(normalized)],
                           hand_world_landmarks=[landmarkList(world)],
                           gestures=[[gesture] if gesture else []])


class GeometryTests(unittest.TestCase):
    def testFourCurledAndAnyOneExtended(self):
        closed = geometry({'worldLandmarks': skeleton()})
        self.assertTrue(closed['fist'])
        self.assertEqual(closed['curledFingers'], 4)
        for finger in range(4):
            with self.subTest(finger=finger):
                extended = geometry({'worldLandmarks': skeleton(finger)})
                self.assertFalse(extended['fist'])
                self.assertEqual(extended['curledFingers'], 3)

    def testPalmBendAndSidewaysSpreadAreDistinct(self):
        # Identical finger-chain shape: one points out of the palm plane,
        # the other spreads sideways in that plane. The chord ratio lies
        # between the two thresholds so the palm-plane test decides.
        bent = skeleton()
        base = bent[5].copy()
        vectors = np.array([[0, 0, .4], [0, .2, .3464101615], [0, .3464101615, .2]])
        bent[6:9] = base + np.cumsum(vectors, axis=0)
        spread = skeleton()
        planar = vectors[:, [2, 1, 0]]
        spread[6:9] = base + np.cumsum(planar, axis=0)
        bentResult = geometry({'worldLandmarks': bent})
        spreadResult = geometry({'worldLandmarks': spread})
        self.assertGreater(bentResult['straightness'][0], .85)
        self.assertLessEqual(bentResult['straightness'][0], .95)
        self.assertAlmostEqual(bentResult['straightness'][0], spreadResult['straightness'][0])
        self.assertGreaterEqual(bentResult['palmBendDegrees'][0], 45)
        self.assertLess(spreadResult['palmBendDegrees'][0], 45)
        self.assertTrue(bentResult['fist'])
        self.assertFalse(spreadResult['fist'])

    def testRotationTranslationAndScaleInvariant(self):
        rng = np.random.default_rng(42)
        for extended in (None, 0, 1, 2, 3):
            original = skeleton(extended)
            expected = geometry({'worldLandmarks': original})
            for _ in range(20):
                rotation, _ = np.linalg.qr(rng.normal(size=(3, 3)))
                if np.linalg.det(rotation) < 0:
                    rotation[:, 0] *= -1
                transformed = original @ rotation * 3.7 + [4, -2, 8]
                actual = geometry({'worldLandmarks': transformed})
                self.assertEqual(expected['fist'], actual['fist'])
                np.testing.assert_allclose(expected['foldDegrees'], actual['foldDegrees'], atol=1e-5)
                np.testing.assert_allclose(expected['tipToMcp'], actual['tipToMcp'], atol=1e-10)

    def testMalformedNonfiniteAndDegenerateAreUnknown(self):
        bad = [None, [], [[0, 0, 0]] * 20, [[0, 0]] * 21, np.zeros((21, 3))]
        for value in (math.nan, math.inf, -math.inf):
            p = skeleton(); p[4, 0] = value; bad.append(p)
        p = skeleton(); p[6] = p[5]; bad.append(p)
        p = skeleton(); p[0] = p[9]; bad.append(p)
        for value in bad:
            with self.subTest(value=str(value)[:30]):
                self.assertIsNone(geometry({'worldLandmarks': value}))
        self.assertIsNone(geometry({}))


class WorkerTests(unittest.TestCase):
    def assertUnknown(self, output):
        self.assertFalse(output['geometryValid'])
        self.assertFalse(output['fist'])
        self.assertEqual(output['label'], 'Unknown')
        self.assertEqual(output['landmarks'], [])

    def testInvalidTargetAndNoHand(self):
        result = resultFor()
        for target in ((math.nan, .35), (.45, math.inf), (-.01, .35), (1.01, .35), (.45, -1), (.45, 2)):
            with self.subTest(target=target):
                self.assertUnknown(predict(result, 7, *target))
        empty = SimpleNamespace(hand_landmarks=[], hand_world_landmarks=[], gestures=[])
        self.assertUnknown(predict(empty, 7, .45, .35))
        self.assertUnknown(predict(result, 7, .9, .9))

    def testGeometryInvalidDoesNotBecomeFist(self):
        result = resultFor()
        result.hand_world_landmarks = []
        output = predict(result, 7, .45, .35)
        self.assertFalse(output['geometryValid'])
        self.assertFalse(output['fist'])
        result = resultFor(); result.hand_world_landmarks[0][0].x = math.nan
        output = predict(result, 7, .45, .35)
        self.assertFalse(output['geometryValid'])
        self.assertFalse(output['fist'])

    def testValidAndOneExtended(self):
        closed = predict(resultFor(), 7, .45, .35)
        self.assertTrue(closed['geometryValid'])
        self.assertTrue(closed['fist'])
        for finger in range(4):
            output = predict(resultFor(skeleton(finger)), 7, .45, .35)
            self.assertTrue(output['geometryValid'])
            self.assertFalse(output['fist'])

    def testWristTargetAndOtherHandRejection(self):
        result = resultFor()
        result.hand_landmarks[0][0].x, result.hand_landmarks[0][0].y = .48, .35
        # The default still matches landmark 9; wrist matching uses landmark 0.
        self.assertTrue(predict(result, 7, .45, .35)['geometryValid'])
        self.assertAlmostEqual(predict(result, 7, .48, .35, targetJoint=0)['distance'], 0)
        output = predict(result, 7, .40, .35, targetJoint=0, otherWristX=.48, otherWristY=.35)
        self.assertFalse(output['geometryValid'])
        self.assertEqual(output['reason'], 'OtherHand')
        self.assertEqual(predict(result, 7, .48, .35, targetJoint=0, otherWristX=.49, otherWristY=.35)['reason'], 'Ambiguous')

    def testCompetingHandsAmbiguousAndNoHistory(self):
        result = resultFor()
        other = resultFor().hand_landmarks[0]
        other[0].x, other[0].y = .46, .35
        result.hand_landmarks[0][0].x, result.hand_landmarks[0][0].y = .44, .35
        result.hand_landmarks.append(other)
        result.hand_world_landmarks.append(landmarkList(skeleton()))
        result.gestures.append([])
        output = predict(result, 7, .45, .35, targetJoint=0)
        self.assertEqual(output['reason'], 'Ambiguous')
        self.assertFalse(output['geometryValid'])
        self.assertFalse(output['fist'])
        closed = predict(resultFor(), 8, .45, .35)
        self.assertTrue(closed['fist'])
        empty = SimpleNamespace(hand_landmarks=[], hand_world_landmarks=[], gestures=[])
        self.assertUnknown(predict(empty, 9, .45, .35))

    def testCropCoordinatesRestoreOriginalSpace(self):
        result = resultFor()
        result.hand_landmarks[0][0] = SimpleNamespace(x=.25, y=.75, z=-.3)
        restored = cropResult(result, left=100, top=20, side=200, width=1000, height=500)
        p = restored.hand_landmarks[0][0]
        self.assertAlmostEqual(p.x, .15)
        self.assertAlmostEqual(p.y, .34)
        self.assertAlmostEqual(p.z, -.3)
        self.assertEqual(result.hand_landmarks[0][0].x, .25)

    def runWorker(self, fullResult, cropResultValue):
        class Recognizer:
            def __init__(self, result):
                self.result = result
                self.calls = 0
            def __enter__(self): return self
            def __exit__(self, *args): return False
            def recognize_for_video(self, image, timestamp):
                self.calls += 1
                return self.result
            def recognize(self, image):
                self.calls += 1
                return self.result
        full = Recognizer(fullResult)
        crop = Recognizer(cropResultValue)
        packet = worker.HEADER.pack(1, 16, 16, 1024, .45, .35, 9, -1, -1) + bytes(1024)
        inputStream = SimpleNamespace(buffer=io.BytesIO(packet))
        outputStream = io.StringIO()
        with patch.object(worker.sys, 'argv', ['worker', 'dummy']), patch.object(worker.sys, 'stdin', inputStream), \
             patch.object(worker.sys, 'stdout', outputStream), \
             patch.object(worker.mp.tasks.vision.GestureRecognizer, 'create_from_options', side_effect=[full, crop]):
            worker.main()
        return json.loads(outputStream.getvalue()), full.calls, crop.calls

    def testFullNonGrabIsNotOverwrittenByCrop(self):
        full = resultFor(skeleton(0))
        output, fullCalls, cropCalls = self.runWorker(full, resultFor())
        self.assertEqual(fullCalls, 1)
        self.assertEqual(cropCalls, 0)
        self.assertTrue(output['geometryValid'])
        self.assertFalse(output['fist'])
        self.assertEqual(output['source'], 'Full')

    def testCropFallbackProducesOriginalSpaceCoordinates(self):
        empty = SimpleNamespace(hand_landmarks=[], hand_world_landmarks=[], gestures=[])
        crop = resultFor()
        # For a 16x16 input, ROI is 8x8 at left=3, top=2.
        crop.hand_landmarks[0][9].x = .525
        crop.hand_landmarks[0][9].y = .45
        output, _, cropCalls = self.runWorker(empty, crop)
        self.assertEqual(cropCalls, 1)
        self.assertTrue(output['geometryValid'])
        self.assertTrue(output['fist'])
        self.assertEqual(output['source'], 'Crop')
        self.assertEqual(output['fullReason'], 'NoHand')
        self.assertAlmostEqual(output['landmarks'][18], .45, places=6)
        self.assertAlmostEqual(output['landmarks'][19], .35, places=6)

    def testCropCannotResolveFullAmbiguity(self):
        full = resultFor()
        full.hand_landmarks.append(resultFor().hand_landmarks[0])
        full.hand_world_landmarks.append(landmarkList(skeleton()))
        full.gestures.append([])
        output, _, cropCalls = self.runWorker(full, resultFor())
        self.assertEqual(cropCalls, 0)
        self.assertFalse(output['geometryValid'])
        self.assertFalse(output['fist'])
        self.assertEqual(output['reason'], 'Ambiguous')
        self.assertEqual(output['source'], 'Full')

    def testNonfiniteNormalizedLandmarksAreUnknown(self):
        result = resultFor(); result.hand_landmarks[0][4].x = math.nan
        self.assertUnknown(predict(result, 7, .45, .35))
        result = resultFor(palm=(math.nan, .35))
        self.assertUnknown(predict(result, 7, .45, .35))


if __name__ == '__main__':
    unittest.main()
