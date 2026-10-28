using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

/// <summary>Verifies actual sphere/box overlap without relying on physics callbacks.</summary>
public static class ScoopContactChecks
{
    public static object Run()
    {
        var failures = new List<string>();
        int checks = 0;
        void Check(bool value, string name)
        {
            checks++;
            if (!value) failures.Add(name);
        }
        var preview = EditorSceneManager.NewPreviewScene();
        GameObject hand = null;
        GameObject food = null;
        try
        {
            // Preview scenes cannot be active; move temporary objects before
            // adding components, without saving or switching the user's scene.
            hand = new GameObject("ScoopContactCheckHand") { hideFlags = HideFlags.HideAndDontSave };
            food = new GameObject("ScoopContactCheckFood") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(hand, preview);
            SceneManager.MoveGameObjectToScene(food, preview);
            var sphere = hand.AddComponent<SphereCollider>();
            sphere.radius = .065f;
            sphere.isTrigger = true;
            var box = food.AddComponent<BoxCollider>();
            box.size = Vector3.one;
            box.isTrigger = true;
            food.transform.localScale = Vector3.one * .055f;
            // Newly created collider shapes must receive their scaled geometry
            // before querying them in Edit Mode (there is no physics tick here).
            Physics.SyncTransforms();
            hand.transform.position = Vector3.zero;
            Check(ScoopFeedingInteraction.CheckContact(sphere, box), "center overlap");
            hand.transform.position = new Vector3(.09f, 0f, 0f);
            Check(ScoopFeedingInteraction.CheckContact(sphere, box), "side edge overlap");
            hand.transform.position = new Vector3(.12f, 0f, 0f);
            Check(!ScoopFeedingInteraction.CheckContact(sphere, box), "separated sideways");
            hand.transform.position = new Vector3(0f, 0f, .20f);
            Check(!ScoopFeedingInteraction.CheckContact(sphere, box), "screen overlap with separated depth");
        }
        finally
        {
            if (hand != null) UnityEngine.Object.DestroyImmediate(hand);
            if (food != null) UnityEngine.Object.DestroyImmediate(food);
            EditorSceneManager.ClosePreviewScene(preview);
        }
        if (failures.Count != 0) throw new InvalidOperationException(
            $"Scoop contact checks: {failures.Count}/{checks} failed: " + string.Join("; ", failures));
        return new { Passed = checks, Failed = failures.Count };
    }
}
