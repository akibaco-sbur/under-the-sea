using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns configured fish prefabs and gives each one independent, correlated
/// stochastic swimming. The rig's Root bone is translated after animation evaluation.
/// Swimming bounds use this component's local XYZ volume.
/// </summary>
[DefaultExecutionOrder(-100)]
public sealed class SummonFish : MonoBehaviour
{
    private static readonly float[] CandidateYawOffsets = { -110f, -70f, -35f, 0f, 35f, 70f, 110f };
    private static readonly float[] CandidatePitchOffsets = { -50f, -25f, 0f, 25f, 50f };

    public enum LocalForwardAxis
    {
        PositiveX,
        NegativeX,
        PositiveZ,
        NegativeZ
    }

    [Serializable]
    public sealed class TextureAssignment
    {
        [Tooltip("Renderer path below the fish prefab. Leave empty to match all renderers.")]
        public string rendererPath;
        [Min(0)] public int materialIndex;
        public Texture texture;
        [Tooltip("Leave blank to use _BaseMap or _MainTex automatically.")]
        public string textureProperty;
    }

    [Serializable]
    public sealed class FishSpecies
    {
        [Tooltip("Name used by SummonByName. If empty, the prefab name is used.")]
        public string speciesName;
        [Tooltip("The prefab's Animator and its controller are copied with the prefab. Configure animation on this prefab.")]
        public GameObject prefab;
        [Min(0)] public int initialCount = 5;
        public LocalForwardAxis forwardAxis = LocalForwardAxis.PositiveZ;
        [Min(0.01f)] public float maxSpeed = 1.5f;
        [Min(0.01f)] public float maxAcceleration = 0.8f;
        [Min(1f)] public float maxAngularVelocity = 70f;
        public TextureAssignment[] textureAssignments = Array.Empty<TextureAssignment>();
        [Tooltip("Multiple MonoBehaviour components can be assigned. Their types are added if missing; Animator components should be configured on the fish prefab.")]
        public MonoBehaviour[] behavioursToAdd = Array.Empty<MonoBehaviour>();
    }

    private sealed class FishAgent
    {
        public GameObject instance;
        public Transform rootBone;
        public Vector3 rootOffsetInInstance;
        public Vector3 position;
        public Vector3 spawnPosition;
        public Vector3 heading;
        public Vector3 targetHeading;
        public Quaternion baseRotation;
        public FishSpecies species;
        public float speed;
        public float speedPreference;
        public float turnRate;
        public float cruiseRemaining;
        public float planningCooldown;
        public bool isTurning;
        public float accelerationNoise;
        public bool textureAssignmentsApplied;
    }

    private struct ObstacleSphere
    {
        public Vector3 center;
        public float radius;
    }

    [Header("Fish types")]
    [SerializeField] private FishSpecies[] species = Array.Empty<FishSpecies>();
    [SerializeField] private bool spawnInitialCountsOnStart = true;

    [Header("Swimming area (this object's local XYZ volume)")]
    [SerializeField, Min(0.1f)] private float halfWidth = 8f;
    [SerializeField, Min(0.1f)] private float halfDepth = 5f;
    [SerializeField] private float localHeight;

    [Header("Vertical movement (local Y)")]
    [SerializeField, Min(0f)] private float halfHeight = 2f;

    [Header("Obstacle avoidance")]
    [Tooltip("Assign scenery objects or empty parent objects. Each child Renderer becomes a padded sphere avoidance volume.")]
    [SerializeField] private Transform[] obstacleRoots = Array.Empty<Transform>();
    [Tooltip("Extra padding around each renderer bounds, in local swimming-area units.")]
    [SerializeField, Min(0f)] private float obstaclePadding = 0.35f;
    [Tooltip("Additional clearance around the predicted fish path, in local swimming-area units.")]
    [SerializeField, Min(0f)] private float fishClearance = 0.3f;
    [Tooltip("Scales the complete obstacle volume, including its padding and fish clearance. Lower this to make fish pass closer to rocks.")]
    [SerializeField, Range(0.4f, 1f)] private float obstacleVolumeScale = 0.75f;
    [SerializeField, Min(0.1f)] private float obstacleLookAhead = 2.5f;
    [Tooltip("Number of simulated future points per candidate steering direction. Higher values give smoother, more careful avoidance at extra CPU cost.")]
    [SerializeField, Range(8, 48)] private int trajectorySamples = 24;
    [Tooltip("How often a fish checks whether its current route remains safe. Route changes are held between checks.")]
    [SerializeField, Min(0.05f)] private float routeCheckInterval = 0.25f;

    [SerializeField, HideInInspector] private int randomSeed;

    private readonly List<FishAgent> activeFish = new List<FishAgent>();
    private System.Random random;
    private Transform fishContainer;
    private readonly List<ObstacleSphere> obstacleSpheres = new List<ObstacleSphere>();

    public int FishCount => activeFish.Count;

    private void Awake()
    {
        int seed = randomSeed != 0 ? randomSeed : GetInstanceID();
        random = new System.Random(seed);
        RebuildObstacleVolumes();
        GameObject container = new GameObject("Summoned Fish");
        fishContainer = container.transform;
        fishContainer.SetParent(transform, false);
    }

    private void RebuildObstacleVolumes()
    {
        obstacleSpheres.Clear();
        if (obstacleRoots == null) return;

        var seen = new HashSet<Renderer>();
        foreach (Transform obstacleRoot in obstacleRoots)
        {
            if (!obstacleRoot) continue;
            Renderer[] renderers = obstacleRoot.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                if (!renderer || !seen.Add(renderer)) continue;
                Bounds worldBounds = renderer.bounds;
                Vector3 min = worldBounds.min;
                Vector3 max = worldBounds.max;
                Bounds localBounds = new Bounds(transform.InverseTransformPoint(new Vector3(min.x, min.y, min.z)), Vector3.zero);
                for (int corner = 1; corner < 8; corner++)
                {
                    Vector3 point = new Vector3(
                        (corner & 1) == 0 ? min.x : max.x,
                        (corner & 2) == 0 ? min.y : max.y,
                        (corner & 4) == 0 ? min.z : max.z);
                    localBounds.Encapsulate(transform.InverseTransformPoint(point));
                }
                obstacleSpheres.Add(new ObstacleSphere
                {
                    center = localBounds.center,
                    radius = (localBounds.extents.magnitude + obstaclePadding) * obstacleVolumeScale
                });
            }
        }

        if (obstacleRoots.Length > 0 && obstacleSpheres.Count == 0)
            Debug.LogWarning("SummonFish: obstacle roots contain no Renderer components; no obstacles will be avoided.", this);
    }

    private void Start()
    {
        if (spawnInitialCountsOnStart)
            SummonConfiguredFish();
    }

    /// <summary>Spawn the configured initial count for every species.</summary>
    public void SummonConfiguredFish()
    {
        if (species == null) return;
        for (int i = 0; i < species.Length; i++)
            Summon(i, species[i] != null ? species[i].initialCount : 0);
    }

    /// <summary>Spawn count fish of a species by its Inspector list index.</summary>
    public int Summon(int speciesIndex, int count = 1)
    {
        if (species == null || speciesIndex < 0 || speciesIndex >= species.Length)
        {
            Debug.LogWarning("SummonFish: species index is out of range.", this);
            return 0;
        }
        return SummonSpecies(species[speciesIndex], count);
    }

    /// <summary>Spawn count fish by speciesName, or by prefab name if unset.</summary>
    public int SummonByName(string speciesName, int count = 1)
    {
        if (species == null || string.IsNullOrWhiteSpace(speciesName)) return 0;
        for (int i = 0; i < species.Length; i++)
        {
            FishSpecies item = species[i];
            if (item == null || !item.prefab) continue;
            string itemName = string.IsNullOrWhiteSpace(item.speciesName) ? item.prefab.name : item.speciesName;
            if (string.Equals(itemName, speciesName, StringComparison.OrdinalIgnoreCase))
                return SummonSpecies(item, count);
        }
        Debug.LogWarning("SummonFish: no species named '" + speciesName + "' is configured.", this);
        return 0;
    }

    private int SummonSpecies(FishSpecies item, int count)
    {
        if (item == null || !item.prefab || count <= 0) return 0;
        int spawned = 0;
        for (int i = 0; i < count; i++)
        {
            GameObject instance = Instantiate(item.prefab, fishContainer);
            Vector3 spawn = RandomPointInArea();
            instance.transform.localPosition = spawn;
            instance.transform.localRotation = item.prefab.transform.localRotation;
            instance.transform.localScale = item.prefab.transform.localScale;
            AddConfiguredBehaviours(instance, item);

            Transform rootBone = FindRootBone(instance.transform);
            if (!rootBone)
            {
                Debug.LogError("SummonFish: prefab '" + item.prefab.name +
                    "' has no child Transform named 'Root'. This fish was removed.", item.prefab);
                Destroy(instance);
                continue;
            }

            Vector3 rootOffset = instance.transform.InverseTransformPoint(rootBone.position);
            var agent = new FishAgent
            {
                instance = instance,
                rootBone = rootBone,
                rootOffsetInInstance = rootOffset,
                position = spawn,
                spawnPosition = spawn,
                heading = RandomDirection(),
                targetHeading = Vector3.zero,
                baseRotation = instance.transform.rotation,
                species = item,
                speedPreference = NextFloat(0.38f, 0.68f),
                speed = item.maxSpeed * NextFloat(0.3f, 0.55f),
                accelerationNoise = 0f,
                cruiseRemaining = NextFloat(1.5f, 4.0f),
                planningCooldown = NextFloat(0f, 0.15f),
                isTurning = false
            };
            agent.targetHeading = agent.heading;
            activeFish.Add(agent);
            spawned++;
        }
        return spawned;
    }

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        for (int i = activeFish.Count - 1; i >= 0; i--)
        {
            FishAgent fish = activeFish[i];
            if (!fish.instance || !fish.rootBone)
            {
                activeFish.RemoveAt(i);
                continue;
            }
            if (!fish.textureAssignmentsApplied)
            {
                // Run after billboard Start has created its output renderer so
                // per-fish textures reach both source and displayed meshes.
                ApplyTextureAssignments(fish.instance.transform, fish.species);
                fish.textureAssignmentsApplied = true;
            }
            SimulateFish(fish, dt);
        }
    }

    private void SimulateFish(FishAgent fish, float dt)
    {
        float maxSpeed = Mathf.Max(0.01f, fish.species.maxSpeed);
        float maxAcceleration = Mathf.Max(0.01f, fish.species.maxAcceleration);
        float maxAngularVelocity = Mathf.Max(1f, fish.species.maxAngularVelocity);

        // Correlated speed preference: it drifts gradually around each fish's
        // own preferred pace instead of switching between timed swim modes.
        const float speedMemory = 2.8f;
        const float speedNoise = 0.11f;
        fish.speedPreference += ((0.54f - fish.speedPreference) / speedMemory) * dt +
            speedNoise * Mathf.Sqrt(dt) * NextGaussian();
        fish.speedPreference = Mathf.Clamp(fish.speedPreference, 0.2f, 0.88f);
        float targetSpeed = maxSpeed * fish.speedPreference * (fish.isTurning ? 0.72f : 1f);

        // Random acceleration is filtered over time, then constrained by the
        // species' maximum acceleration. This keeps motion smooth and bounded.
        const float accelerationMemory = 0.75f;
        fish.accelerationNoise += (-fish.accelerationNoise / accelerationMemory) * dt +
            maxAcceleration * 0.16f * Mathf.Sqrt(dt) * NextGaussian();
        float speedError = targetSpeed - fish.speed;
        float accelerationCommand = speedError / 0.8f + fish.accelerationNoise;
        accelerationCommand = Mathf.Clamp(accelerationCommand, -maxAcceleration, maxAcceleration);
        fish.speed = Mathf.Clamp(fish.speed + accelerationCommand * dt, 0f, maxSpeed);

        fish.planningCooldown -= dt;
        float remainingTurn = Vector3.Angle(fish.heading, fish.targetHeading);
        if (!fish.isTurning && remainingTurn < 2f)
        {
            fish.cruiseRemaining -= dt;
            if (fish.cruiseRemaining <= 0f)
                fish.targetHeading = PickNextCourse(fish.heading);
        }

        if (fish.planningCooldown <= 0f)
        {
            float horizon = Mathf.Clamp(obstacleLookAhead / Mathf.Max(fish.speed, 0.1f), 0.8f, 2.5f);
            Vector3 safeTarget = ChooseSafeHeading(fish.position, fish.heading, fish.targetHeading,
                fish.targetHeading, fish.speed, fish.turnRate, maxAngularVelocity, horizon);
            if (Vector3.Angle(safeTarget, fish.targetHeading) > 8f)
                fish.targetHeading = safeTarget;
            fish.planningCooldown = routeCheckInterval;
        }

        remainingTurn = Vector3.Angle(fish.heading, fish.targetHeading);
        if (remainingTurn < 1f)
        {
            fish.heading = fish.targetHeading;
            fish.turnRate = 0f;
            if (fish.isTurning)
            {
                fish.isTurning = false;
                fish.cruiseRemaining = NextFloat(1.5f, 4.0f);
            }
        }
        else
        {
            fish.isTurning = true;
            // Accelerate into the turn and brake as the target direction nears,
            // producing a smooth curved arc instead of frame-by-frame steering.
            float turnAcceleration = maxAngularVelocity * 1.5f;
            float brakingRate = Mathf.Sqrt(2f * turnAcceleration * remainingTurn);
            float desiredTurnRate = Mathf.Min(maxAngularVelocity, brakingRate);
            fish.turnRate = Mathf.MoveTowards(fish.turnRate, desiredTurnRate, turnAcceleration * dt);
            fish.heading = Vector3.RotateTowards(fish.heading, fish.targetHeading,
                fish.turnRate * Mathf.Deg2Rad * dt, 0f).normalized;
        }

        fish.position += fish.heading * fish.speed * dt;
        KeepInsideArea(fish, maxAngularVelocity, dt);
        ApplyFishPose(fish);
    }

    private Vector3 ChooseSafeHeading(Vector3 start, Vector3 currentHeading, Vector3 preferredHeading,
        Vector3 previousPlan, float speed, float currentTurnRate, float maxAngularVelocity, float horizon)
    {
        Vector3 bestHeading = preferredHeading;
        float bestCost = float.PositiveInfinity;
        int steps = Mathf.Max(8, trajectorySamples);
        float stepTime = horizon / steps;
        float turnAcceleration = maxAngularVelocity * 1.5f;
        float maxSafetyDistance = Mathf.Max(0.25f, speed * stepTime * 2f);

        // Try reachable 3D steering choices and score their complete short paths.
        // Stable candidate ordering plus a cost for changing the previous plan
        // prevents the left/right switching that made the old point probe jitter.
        foreach (float yaw in CandidateYawOffsets)
        {
            foreach (float pitch in CandidatePitchOffsets)
            {
                Vector3 candidate = RotateHeading(preferredHeading, yaw, pitch);
                float preferredChange = Vector3.Angle(candidate, preferredHeading);
                float planChange = Vector3.Angle(candidate, previousPlan);
                float cost = preferredChange * preferredChange * 0.004f +
                             planChange * planChange * 0.0025f;

                Vector3 simulatedPosition = start;
                Vector3 simulatedHeading = currentHeading;
                float simulatedTurnRate = currentTurnRate;
                for (int step = 0; step < steps; step++)
                {
                    float angleRemaining = Vector3.Angle(simulatedHeading, candidate);
                    float brakingRate = Mathf.Sqrt(2f * turnAcceleration * angleRemaining);
                    float desiredTurnRate = Mathf.Min(maxAngularVelocity, brakingRate);
                    simulatedTurnRate = Mathf.MoveTowards(simulatedTurnRate, desiredTurnRate,
                        turnAcceleration * stepTime);
                    simulatedHeading = Vector3.RotateTowards(simulatedHeading, candidate,
                        simulatedTurnRate * Mathf.Deg2Rad * stepTime, 0f).normalized;
                    simulatedPosition += simulatedHeading * speed * stepTime;

                    Vector3 clamped = new Vector3(
                        Mathf.Clamp(simulatedPosition.x, -halfWidth, halfWidth),
                        Mathf.Clamp(simulatedPosition.y, localHeight - halfHeight, localHeight + halfHeight),
                        Mathf.Clamp(simulatedPosition.z, -halfDepth, halfDepth));
                    float boundaryError = (simulatedPosition - clamped).magnitude;
                    if (boundaryError > 0f)
                        cost += 80f + boundaryError * boundaryError * 50f;

                    for (int obstacle = 0; obstacle < obstacleSpheres.Count; obstacle++)
                    {
                        ObstacleSphere sphere = obstacleSpheres[obstacle];
                        float safeDistance = sphere.radius + fishClearance * obstacleVolumeScale;
                        float clearance = Vector3.Distance(simulatedPosition, sphere.center) - safeDistance;
                        if (clearance < 0f)
                            cost += 90f + clearance * clearance * 40f;
                        else if (clearance < maxSafetyDistance)
                        {
                            float proximity = (maxSafetyDistance - clearance) / maxSafetyDistance;
                            cost += proximity * proximity * 1.5f;
                        }
                    }
                }

                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestHeading = candidate;
                }
            }
        }
        return bestHeading;
    }

    private Vector3 PickNextCourse(Vector3 currentHeading)
    {
        float turnSide = NextFloat(0f, 1f) < 0.5f ? -1f : 1f;
        float yaw = turnSide * NextFloat(28f, 105f);
        float pitch = NextFloat(-24f, 24f);
        return RotateHeading(currentHeading, yaw, pitch);
    }

    private void KeepInsideArea(FishAgent fish, float maxAngularVelocity, float dt)
    {
        Vector3 clamped = new Vector3(
            Mathf.Clamp(fish.position.x, -halfWidth, halfWidth),
            Mathf.Clamp(fish.position.y, localHeight - halfHeight, localHeight + halfHeight),
            Mathf.Clamp(fish.position.z, -halfDepth, halfDepth));
        if ((clamped - fish.position).sqrMagnitude > 0.000001f)
        {
            fish.position = clamped;
            Vector3 inward = (new Vector3(-clamped.x, localHeight - clamped.y, -clamped.z)).normalized;
            if (inward.sqrMagnitude > 0.0001f)
                fish.heading = Vector3.RotateTowards(fish.heading, inward,
                    maxAngularVelocity * Mathf.Deg2Rad * dt, 0f).normalized;
        }
    }

    private void ApplyFishPose(FishAgent fish)
    {
        Vector3 localForward = GetForwardAxis(fish.species.forwardAxis);
        Vector3 desiredWorldForward = transform.TransformDirection(fish.heading);
        if (desiredWorldForward.sqrMagnitude > 0.0001f)
        {
            desiredWorldForward.Normalize();
            Vector3 desiredWorldUp = Vector3.ProjectOnPlane(fish.baseRotation * Vector3.up, desiredWorldForward);
            if (desiredWorldUp.sqrMagnitude < 0.0001f)
                desiredWorldUp = Vector3.ProjectOnPlane(fish.instance.transform.up, desiredWorldForward);
            if (desiredWorldUp.sqrMagnitude < 0.0001f)
                desiredWorldUp = Vector3.ProjectOnPlane(fish.baseRotation * Vector3.right, desiredWorldForward);

            // Build a full orientation frame, not a from-to rotation. A stable up
            // vector removes the arbitrary roll axis during a near-180-degree turn.
            Quaternion modelForwardFrame = Quaternion.LookRotation(localForward, Vector3.up);
            Quaternion targetFrame = Quaternion.LookRotation(desiredWorldForward, desiredWorldUp.normalized);
            Quaternion targetRotation = targetFrame * Quaternion.Inverse(modelForwardFrame);
            float maxTurn = fish.species.maxAngularVelocity * Time.deltaTime;
            fish.instance.transform.rotation = Quaternion.RotateTowards(
                fish.instance.transform.rotation, targetRotation, maxTurn);
        }

        Vector3 displacement = fish.position - fish.spawnPosition;
        Vector3 movedRoot = fish.instance.transform.TransformPoint(fish.rootOffsetInInstance) +
                            transform.TransformVector(displacement);
        fish.rootBone.position = movedRoot;
    }

    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || obstacleSpheres.Count == 0) return;
        Matrix4x4 previousMatrix = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(1f, 0.65f, 0.1f, 0.8f);
        foreach (ObstacleSphere sphere in obstacleSpheres)
            Gizmos.DrawWireSphere(sphere.center, sphere.radius + fishClearance * obstacleVolumeScale);
        Gizmos.matrix = previousMatrix;
    }

    private Vector3 RandomDirection()
    {
        float angle = NextFloat(0f, Mathf.PI * 2f);
        float vertical = NextFloat(-0.3f, 0.3f);
        float planar = Mathf.Sqrt(1f - vertical * vertical);
        return new Vector3(Mathf.Cos(angle) * planar, vertical, Mathf.Sin(angle) * planar);
    }

    private Vector3 RandomPointInArea()
    {
        return new Vector3(NextFloat(-halfWidth, halfWidth),
            localHeight + NextFloat(-halfHeight, halfHeight), NextFloat(-halfDepth, halfDepth));
    }

    private float NextFloat(float min, float max)
    {
        return min + (float)random.NextDouble() * (max - min);
    }

    private float NextGaussian()
    {
        double u1 = Math.Max(1e-8, random.NextDouble());
        double u2 = random.NextDouble();
        return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
    }

    private static Vector3 RotateHeading(Vector3 direction, float yawDegrees, float pitchDegrees)
    {
        Vector3 result = Quaternion.AngleAxis(yawDegrees, Vector3.up) * direction;
        Vector3 right = Vector3.Cross(Vector3.up, result);
        if (right.sqrMagnitude < 0.0001f)
            right = Vector3.Cross(Vector3.forward, result);
        return (Quaternion.AngleAxis(pitchDegrees, right.normalized) * result).normalized;
    }

    private static Vector3 GetForwardAxis(LocalForwardAxis axis)
    {
        switch (axis)
        {
            case LocalForwardAxis.PositiveX: return Vector3.right;
            case LocalForwardAxis.NegativeX: return Vector3.left;
            case LocalForwardAxis.NegativeZ: return Vector3.back;
            default: return Vector3.forward;
        }
    }

    private void ApplyTextureAssignments(Transform instanceRoot, FishSpecies item)
    {
        if (item.textureAssignments == null || item.textureAssignments.Length == 0)
            return;

        Renderer[] renderers = instanceRoot.GetComponentsInChildren<Renderer>(true);
        foreach (TextureAssignment assignment in item.textureAssignments)
        {
            if (assignment == null || !assignment.texture)
                continue;

            Transform assignmentTarget = string.IsNullOrWhiteSpace(assignment.rendererPath)
                ? instanceRoot
                : FindRelativeTransform(instanceRoot, assignment.rendererPath);
            if (!assignmentTarget)
            {
                Debug.LogWarning("SummonFish: renderer path '" + assignment.rendererPath + "' was not found.", instanceRoot);
                continue;
            }

            bool matched = false;
            foreach (Renderer renderer in renderers)
            {
                if (renderer.transform != assignmentTarget && !renderer.transform.IsChildOf(assignmentTarget))
                    continue;

                Material[] sharedMaterials = renderer.sharedMaterials;
                int slot = assignment.materialIndex;
                if (slot < 0 || slot >= sharedMaterials.Length || !sharedMaterials[slot])
                    continue;

                Material material = sharedMaterials[slot];
                string property = assignment.textureProperty;
                if (string.IsNullOrWhiteSpace(property))
                {
                    if (material.HasProperty("_BaseMap")) property = "_BaseMap";
                    else if (material.HasProperty("_MainTex")) property = "_MainTex";
                    else
                    {
                        Debug.LogWarning("SummonFish: material '" + material.name +
                            "' has no _BaseMap or _MainTex texture property.", renderer);
                        continue;
                    }
                }
                else if (!material.HasProperty(property))
                {
                    Debug.LogWarning("SummonFish: material '" + material.name +
                        "' has no texture property '" + property + "'.", renderer);
                    continue;
                }

                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block, slot);
                block.SetTexture(property, assignment.texture);
                renderer.SetPropertyBlock(block, slot);
                matched = true;
            }

            if (!matched)
                Debug.LogWarning("SummonFish: texture assignment did not match a renderer/material slot. Path='" +
                    assignment.rendererPath + "', index=" + assignment.materialIndex + ".", instanceRoot);
        }
    }

    private void AddConfiguredBehaviours(GameObject instance, FishSpecies item)
    {
        if (item.behavioursToAdd == null)
            return;

        foreach (MonoBehaviour template in item.behavioursToAdd)
        {
            if (!template)
                continue;

            Type componentType = template.GetType();
            if (componentType.IsAbstract || componentType.ContainsGenericParameters)
            {
                Debug.LogWarning("SummonFish: cannot add abstract or open generic behaviour '" +
                    componentType.Name + "'.", instance);
                continue;
            }

            Behaviour existing = instance.GetComponentInChildren(componentType, true) as Behaviour;
            if (existing)
            {
                // A registered behaviour is expected to run on every spawned fish,
                // even if its source prefab kept it disabled for template use.
                existing.enabled = true;
                continue;
            }

            instance.AddComponent(componentType);
        }
    }

    private static Transform FindRelativeTransform(Transform root, string path)
    {
        Transform match = root.Find(path);
        if (match) return match;
        if (string.Equals(root.name, path, StringComparison.OrdinalIgnoreCase)) return root;

        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        foreach (Transform child in children)
            if (string.Equals(child.name, path, StringComparison.OrdinalIgnoreCase))
                return child;
        return null;
    }

    private static Transform FindRootBone(Transform instanceRoot)
    {
        Transform[] children = instanceRoot.GetComponentsInChildren<Transform>(true);
        foreach (Transform child in children)
            if (child != instanceRoot && child.name == "Root")
                return child;
        return null;
    }

    private void OnDestroy()
    {
        if (fishContainer)
            Destroy(fishContainer.gameObject);
    }
}
