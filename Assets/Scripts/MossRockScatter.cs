using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Creates a reproducible, adjustable arrangement of MossRock model prefabs
/// in the local XZ area around this GameObject.
/// </summary>
public sealed class MossRockScatter : MonoBehaviour
{
    public enum DistributionPattern { Uniform, Clustered }

    [Header("Rock variants")]
    [SerializeField] private GameObject[] rockPrefabs = Array.Empty<GameObject>();
    [SerializeField, Min(0)] private int count = 35;

    [Header("Area (local XZ units)")]
    [SerializeField, Min(0.1f)] private float areaWidth = 16f;
    [SerializeField, Min(0.1f)] private float areaDepth = 10f;
    [SerializeField] private float localHeight;

    [Header("SeaBed placement")]
    [SerializeField] private Transform seaBed;
    [SerializeField, Range(0f, 0.5f)] private float maximumBuriedFraction = 0.15f;
    [SerializeField, Min(0f)] private float minimumContactOverlap = 0.001f;

    [Header("Random appearance")]
    [SerializeField] private DistributionPattern pattern = DistributionPattern.Clustered;
    [SerializeField, Min(1)] private int clusterCount = 5;
    [SerializeField, Min(0f)] private float clusterSpread = 1.8f;
    [SerializeField, Min(0f)] private float minimumSpacing = 0.9f;
    [SerializeField, Min(1f)] private float maximumScaleMultiplier = 1.5f;
    [SerializeField, Range(0f, 45f)] private float maximumTilt = 12f;
    [SerializeField] private int seed = 1;

    private const string GeneratedRootName = "Generated Moss Rocks";

    private struct PlacedRock
    {
        public Vector2 position;
        public float scale;
    }

    /// <summary>Regenerate only objects previously created by this component.</summary>
    public void Generate()
    {
        if (rockPrefabs == null || rockPrefabs.Length == 0)
        {
            Debug.LogWarning("MossRockScatter: add one or more MossRock model prefabs first.", this);
            return;
        }

        if (!seaBed)
        {
            GameObject namedSeaBed = GameObject.Find("SeaBed");
            if (namedSeaBed) seaBed = namedSeaBed.transform;
        }
        Collider[] seaBedColliders = seaBed ? seaBed.GetComponentsInChildren<Collider>() : Array.Empty<Collider>();
        bool canPlaceOnSeaBed = seaBedColliders.Length > 0;
        if (!canPlaceOnSeaBed)
            Debug.LogWarning("MossRockScatter: SeaBed (or a Collider on it) was not found. Rocks will use Local Height instead.", this);

        ClearGenerated();
        Transform generatedRoot = CreateGeneratedRoot();
        var random = new System.Random(seed == 0 ? Environment.TickCount : seed);
        Vector2[] clusterCenters = CreateClusterCenters(random);
        var placed = new List<PlacedRock>(count);
        int attemptsPerRock = Mathf.Max(40, count * 3);

        for (int i = 0; i < count; i++)
        {
            bool accepted = false;
            for (int attempt = 0; attempt < attemptsPerRock; attempt++)
            {
                Vector2 position = SamplePosition(random, clusterCenters);
                // Keep 1x as the baseline and vary each rock up to the chosen maximum.
                float scale = RandomRange(random, 1f, Mathf.Max(1f, maximumScaleMultiplier));
                if (!HasEnoughSpace(position, scale, placed))
                    continue;

                GameObject prefab = rockPrefabs[random.Next(rockPrefabs.Length)];
                if (!prefab)
                    continue;

                Vector3 worldSurface = default;
                if (canPlaceOnSeaBed)
                {
                    Vector3 probe = transform.TransformPoint(new Vector3(position.x, localHeight, position.y));
                    if (!TryGetSeaBedSurface(seaBedColliders, probe, out worldSurface))
                        continue;
                }

                GameObject rock = InstantiateRock(prefab, generatedRoot);
                rock.name = prefab.name;
                rock.transform.localPosition = new Vector3(position.x, localHeight, position.y);
                Quaternion variation = Quaternion.Euler(
                    RandomRange(random, -maximumTilt, maximumTilt),
                    RandomRange(random, 0f, 360f),
                    RandomRange(random, -maximumTilt, maximumTilt));
                rock.transform.localRotation = prefab.transform.localRotation * variation;
                rock.transform.localScale = prefab.transform.localScale * scale;

                if (canPlaceOnSeaBed)
                {
                    Bounds bounds = GetCombinedRendererBounds(rock);
                    float maxBurial = bounds.size.y * Mathf.Clamp(maximumBuriedFraction, 0f, 0.5f);
                    float minBurial = Mathf.Min(minimumContactOverlap, maxBurial);
                    float burial = RandomRange(random, minBurial, maxBurial);
                    float deltaY = worldSurface.y - burial - bounds.min.y;
                    rock.transform.position += Vector3.up * deltaY;
                }

                placed.Add(new PlacedRock { position = position, scale = scale });
                accepted = true;
                break;
            }

            if (!accepted)
                break;
        }

        if (placed.Count < count)
            Debug.LogWarning("MossRockScatter placed " + placed.Count + " of " + count +
                " rocks. Increase the area or reduce minimum spacing.", this);
        else
            Debug.Log("MossRockScatter placed " + placed.Count + " rocks.", this);
    }

    private static bool TryGetSeaBedSurface(Collider[] colliders, Vector3 position, out Vector3 surface)
    {
        surface = default;
        bool found = false;
        float nearestDistance = float.PositiveInfinity;
        float originY = position.y + 10000f;
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (!collider || !collider.enabled) continue;
            originY = Mathf.Max(originY, collider.bounds.max.y + 10f);
        }

        Ray ray = new Ray(new Vector3(position.x, originY, position.z), Vector3.down);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (!collider || !collider.enabled) continue;
            if (collider.Raycast(ray, out RaycastHit hit, originY - position.y + 10000f)
                && hit.distance < nearestDistance)
            {
                nearestDistance = hit.distance;
                surface = hit.point;
                found = true;
            }
        }
        return found;
    }

    private static Bounds GetCombinedRendererBounds(GameObject rock)
    {
        Renderer[] renderers = rock.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(rock.transform.position, Vector3.zero);
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    /// <summary>Remove only the generated group; hand-placed objects are untouched.</summary>
    public void ClearGenerated()
    {
        Transform existing = transform.Find(GeneratedRootName);
        if (!existing) return;

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            UnityEditor.Undo.DestroyObjectImmediate(existing.gameObject);
            return;
        }
#endif
        Destroy(existing.gameObject);
    }

    private Transform CreateGeneratedRoot()
    {
        var root = new GameObject(GeneratedRootName);
#if UNITY_EDITOR
        if (!Application.isPlaying)
            UnityEditor.Undo.RegisterCreatedObjectUndo(root, "Create Moss Rock Group");
#endif
        root.transform.SetParent(transform, false);
        return root.transform;
    }

    private GameObject InstantiateRock(GameObject prefab, Transform parent)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying && UnityEditor.PrefabUtility.IsPartOfPrefabAsset(prefab))
        {
            GameObject instance = UnityEditor.PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject;
            if (instance)
            {
                UnityEditor.Undo.RegisterCreatedObjectUndo(instance, "Scatter Moss Rock");
                return instance;
            }
        }
#endif
        GameObject clone = Instantiate(prefab, parent);
#if UNITY_EDITOR
        if (!Application.isPlaying)
            UnityEditor.Undo.RegisterCreatedObjectUndo(clone, "Scatter Moss Rock");
#endif
        return clone;
    }

    private Vector2[] CreateClusterCenters(System.Random random)
    {
        int amount = pattern == DistributionPattern.Clustered ? Mathf.Max(1, clusterCount) : 1;
        var centers = new Vector2[amount];
        for (int i = 0; i < amount; i++)
        {
            centers[i] = new Vector2(
                RandomRange(random, -areaWidth * 0.5f, areaWidth * 0.5f),
                RandomRange(random, -areaDepth * 0.5f, areaDepth * 0.5f));
        }
        return centers;
    }

    private Vector2 SamplePosition(System.Random random, Vector2[] clusterCenters)
    {
        float halfWidth = areaWidth * 0.5f;
        float halfDepth = areaDepth * 0.5f;
        if (pattern == DistributionPattern.Uniform)
            return new Vector2(RandomRange(random, -halfWidth, halfWidth),
                               RandomRange(random, -halfDepth, halfDepth));

        Vector2 center = clusterCenters[random.Next(clusterCenters.Length)];
        Vector2 offset = new Vector2(NextGaussian(random), NextGaussian(random)) * clusterSpread;
        return new Vector2(Mathf.Clamp(center.x + offset.x, -halfWidth, halfWidth),
                           Mathf.Clamp(center.y + offset.y, -halfDepth, halfDepth));
    }

    private bool HasEnoughSpace(Vector2 candidate, float scale, List<PlacedRock> placed)
    {
        for (int i = 0; i < placed.Count; i++)
        {
            float required = minimumSpacing * (scale + placed[i].scale) * 0.5f;
            if ((candidate - placed[i].position).sqrMagnitude < required * required)
                return false;
        }
        return true;
    }

    private static float NextGaussian(System.Random random)
    {
        double u1 = Math.Max(1e-8, random.NextDouble());
        double u2 = random.NextDouble();
        return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
    }

    private static float RandomRange(System.Random random, float min, float max)
    {
        return min + (float)random.NextDouble() * (max - min);
    }
}
