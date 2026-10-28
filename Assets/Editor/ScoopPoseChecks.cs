using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Deterministic sensor-space checks callable from the Unity Editor.</summary>
public static class ScoopPoseChecks
{
    public static object Run()
    {
        var failures = new List<string>();
        int checks = 0;
        void Check(bool condition, string name)
        {
            checks++;
            if (!condition) failures.Add(name);
        }
        KinectScoopSolver.JointSample Joint(Vector3 position, int confidence = 2) =>
            new KinectScoopSolver.JointSample(position, confidence);
        bool Near(Vector3 a, Vector3 b, float tolerance = 0.0001f) => Vector3.Distance(a, b) <= tolerance;

        var chest = new Vector3(0f, 0f, 1.5f);
        // Kinect sensor-space Y increases downwards, so an upward hand has
        // a smaller Y than the chest.
        var shoulder = new Vector3(-0.2f, -0.1f, 1.45f);
        var elbow = new Vector3(-0.25f, -0.1f, 1.35f);
        var wrist = new Vector3(-0.35f, -0.05f, 1.25f);
        var direction = (wrist - elbow).normalized;
        var input = new KinectScoopSolver.Input
        {
            Chest = Joint(chest), Shoulder = Joint(shoulder), Elbow = Joint(elbow),
            Wrist = Joint(wrist), Hand = Joint(wrist + direction * 0.09f)
        };
        var solver = new KinectScoopSolver();
        Check(solver.Step(input, 0f, out var measured), "valid input is tracked");
        Check(!measured.IsEstimated, "plausible measured hand is blended");
        Check(Near(measured.Wrist, -(wrist - chest)), "sensor axes map to negative camera-local axes");
        Check(measured.Wrist.x > 0f, "physical right maps to camera right");
        Check(measured.Wrist.y > 0f, "physical upward maps to camera up");
        Check(measured.Wrist.z > 0f, "sensor approach maps to camera forward");
        Check(Near(measured.ForearmDirection, -direction), "forearm direction matches camera coordinates");

        solver.Reset();
        var missingHand = input;
        missingHand.Hand = Joint(Vector3.zero, 0);
        Check(solver.Step(missingHand, 0f, out var estimated), "missing hand uses forearm");
        Check(estimated.IsEstimated, "missing hand reports estimated");
        Check(Near(estimated.Hand, -(wrist + direction * 0.09f - chest)), "fallback extends forearm by 9 cm");

        solver.Reset();
        var farHand = input;
        farHand.Hand = Joint(wrist + direction * 0.5f);
        Check(solver.Step(farHand, 0f, out var far), "implausible hand distance preserves forearm tracking");
        Check(far.IsEstimated && Near(far.Hand, estimated.Hand), "implausible hand distance rejected");
        solver.Reset();
        var backwardHand = input;
        backwardHand.Hand = Joint(wrist - direction * 0.09f);
        Check(solver.Step(backwardHand, 0f, out var backward) && backward.IsEstimated,
            "backward hand measurement rejected");

        solver.Reset();
        Check(solver.Step(input, 0f, out _), "jump test initializes");
        var jump = input;
        Vector3 perpendicular = Vector3.Cross(direction, Vector3.up).normalized;
        jump.Hand = Joint(wrist + direction * 0.04f + perpendicular * 0.13f);
        Check(solver.Step(jump, 0.067f, out var jumped) && jumped.IsEstimated,
            "sudden wrist-relative hand offset rejected");

        solver.Reset();
        var missingElbow = input;
        missingElbow.Elbow = Joint(elbow, 1);
        Check(!solver.Step(missingElbow, 0.067f, out _), "low-confidence elbow invalidates tracking");
        var missingWrist = input;
        missingWrist.Wrist = Joint(wrist, 0);
        Check(!solver.Step(missingWrist, 0.067f, out _), "missing wrist invalidates tracking");
        var nanWrist = input;
        nanWrist.Wrist = Joint(new Vector3(float.NaN, wrist.y, wrist.z));
        Check(!solver.Step(nanWrist, 0.067f, out _), "NaN wrist invalidates tracking");
        var nanHand = input;
        nanHand.Hand = Joint(new Vector3(float.NaN, 0f, 1f));
        Check(solver.Step(nanHand, 0f, out var invalidHand) && invalidHand.IsEstimated,
            "NaN hand falls back to forearm");
        var zeroForearm = input;
        zeroForearm.Wrist = Joint(elbow);
        Check(!solver.Step(zeroForearm, 0.067f, out _), "degenerate forearm invalidates tracking");

        solver.Reset();
        Check(solver.Step(input, 0f, out _), "recovery test initializes");
        Check(!solver.Step(missingWrist, 0.067f, out _), "loss resets previous output");
        var recovered = input;
        recovered.Wrist = Joint(wrist + new Vector3(-0.10f, 0f, 0f));
        var recoveredWrist = recovered.Wrist.Position;
        var recoveredDirection = (recoveredWrist - elbow).normalized;
        recovered.Hand = Joint(recoveredWrist + recoveredDirection * 0.09f);
        Check(solver.Step(recovered, 0.067f, out var recoveredOutput), "valid frame recovers");
        Check(Near(recoveredOutput.Wrist, -(recoveredWrist - chest)), "recovery does not retain stale position");

        if (failures.Count != 0) throw new InvalidOperationException(
            $"Scoop pose checks: {failures.Count}/{checks} failed: " + string.Join("; ", failures));
        return new { Passed = checks, Failed = failures.Count };
    }
}
