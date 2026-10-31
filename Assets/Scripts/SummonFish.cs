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
        [Tooltip("Offset from the Root bone to the mouth, in prefab-local units. Zero uses the Root position for the bite check.")]
        public Vector3 feedingMouthLocalOffset;
    }

    internal sealed class FishAgent
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
        public StripedFishAnimatedQuadBillboard materialSwitcher;
        public bool returningFromFeeding;
        public GatheringMember gathering;
    }

    internal sealed class GatheringMember
    {
        internal SummonFish School;
        internal FishAgent Fish;
        internal FeedingGroup Group;
        internal int Variant, Stage;
        internal float Side, RearDepth, RoamTime, BiteTime;
        internal Vector3 Offset;
    }

    public sealed class FeedingGroup
    {
        internal readonly List<GatheringMember> Members = new List<GatheringMember>();
        internal Transform Viewer, Food;
        internal Color FoodColor;
        internal float Distance, Radius;
        internal bool Active = true;
        internal readonly FishFeedingRace Race = new FishFeedingRace();
        public bool IsOffering { get; private set; }
        public bool HasWinner => Race.HasWinner;
        public int Count
        {
            get
            {
                int count = 0;
                foreach (var member in Members)
                    if (member.School && member.School.isActiveAndEnabled && member.Fish.instance &&
                        member.Fish.instance.activeInHierarchy && member.Fish.gathering == member) count++;
                return count;
            }
        }
        public void SetOffering(bool offering)
        {
            if (!Active || HasWinner || IsOffering == offering) return;
            IsOffering = offering;
            foreach (var member in Members)
            {
                if (!member.School || !member.Fish.instance) continue;
                member.BiteTime = 0f;
                member.School.ResetGatheringRoute(member);
            }
        }
        public void Cancel()
        {
            Active = false;
            Race.Cancel();
            IsOffering = false;
            foreach (var member in Members)
                if (member.Fish.gathering == member)
                {
                    member.Fish.gathering = null;
                    member.Fish.returningFromFeeding = true;
                    member.Fish.planningCooldown = member.Fish.cruiseRemaining = 0f;
                }
            Members.Clear();
        }
    }

    /// <summary>Reserve the nearest eligible fish across all schools, before food is offered.</summary>
    public static FeedingGroup GatherForFeeding(Transform viewer, Transform food, int foodColorIndex,
        Color foodColor, int maximumFish, float distance, float radius, float searchRadius)
    {
        if (!viewer || !food || !food.gameObject.activeInHierarchy || maximumFish <= 0) return null;
        var candidates = new List<GatheringMember>();
        foreach (var school in FindObjectsByType<SummonFish>(FindObjectsSortMode.None))
        {
            if (!school.isActiveAndEnabled || school.gameObject.scene != viewer.gameObject.scene) continue;
            foreach (var fish in school.activeFish)
            {
                if (!fish.instance || !fish.instance.activeInHierarchy || !fish.rootBone || fish.gathering != null || fish == school.feedingFish) continue;
                if ((school.GetFeedingMouthPosition(fish) - viewer.position).sqrMagnitude > searchRadius * searchRadius) continue;
                if (!fish.materialSwitcher) fish.materialSwitcher = fish.instance.GetComponentInChildren<StripedFishAnimatedQuadBillboard>();
                if (!fish.materialSwitcher || !fish.materialSwitcher.isActiveAndEnabled ||
                    !fish.materialSwitcher.TryGetFeedingVariant(foodColorIndex, foodColor, out int variant)) continue;
                candidates.Add(new GatheringMember { School = school, Fish = fish, Variant = variant });
            }
        }
        candidates.Sort((a,b) => (a.School.GetFeedingMouthPosition(a.Fish)-viewer.position).sqrMagnitude.CompareTo(
            (b.School.GetFeedingMouthPosition(b.Fish)-viewer.position).sqrMagnitude));
        if (candidates.Count == 0) return null;
        var group = new FeedingGroup { Viewer = viewer, Food = food, FoodColor = foodColor,
            Distance = Mathf.Max(1.5f,distance), Radius = Mathf.Clamp(radius,.2f,Mathf.Max(.2f,distance-.8f)) };
        for (int i=0;i<Mathf.Min(maximumFish,candidates.Count);i++)
        {
            var member = candidates[i]; member.Group = group; member.Fish.gathering = member;
            member.Fish.returningFromFeeding = false; group.Members.Add(member);
            member.School.PickGatheringGoal(member); member.School.ResetGatheringRoute(member);
        }
        Debug.Log("[FishFeeding] " + group.Count + " nearby fish are gathering in front of the camera.");
        return group;
    }

    private void PickGatheringGoal(GatheringMember member)
    {
        float radius = member.Group.Radius;
        member.Offset = new Vector3(NextFloat(-radius,radius),NextFloat(-radius*.45f,radius*.45f),NextFloat(-radius*.45f,radius*.45f));
        member.RoamTime = NextFloat(1.5f,3f);
        member.Fish.planningCooldown = 0f;
    }
    private void ResetGatheringRoute(GatheringMember member)
    {
        var group = member.Group;
        if (!group.Viewer || !group.Food) return;
        Vector3 forward = FishFeedingApproach.Forward(group.Viewer.forward);
        Vector3 mouth = GetFeedingMouthPosition(member.Fish);
        float depth = Vector3.Dot(mouth-group.Viewer.position,forward);
        member.Side = FishFeedingApproach.Side(mouth,group.Viewer.position,forward);
        member.RearDepth = Mathf.Min(-.5f,depth);
        member.Stage = depth < 0 ? 0 : group.IsOffering && FishFeedingApproach.InFront(mouth,group.Food.position,forward) ? 3 : 2;
        member.Fish.planningCooldown = 0;
    }

    private void SimulateGathering(GatheringMember member,float dt)
    {
        dt = Mathf.Min(dt,.08f);
        var group = member.Group; var fish = member.Fish;
        if (!group.Active || !group.Viewer || !group.Food || !group.Food.gameObject.activeInHierarchy)
        { group.Cancel(); return; }
        Vector3 forward = FishFeedingApproach.Forward(group.Viewer.forward);
        Vector3 right = Vector3.Cross(Vector3.up,forward);
        Vector3 center = group.Viewer.position + forward*group.Distance;
        Vector3 mouth = GetFeedingMouthPosition(fish);
        if (!group.IsOffering)
        {
            member.RoamTime -= dt;
            if (member.Stage == 3 && (member.RoamTime<=0 || Vector3.Distance(mouth,center+right*member.Offset.x+Vector3.up*member.Offset.y+forward*member.Offset.z)<.45f))
                PickGatheringGoal(member);
        }
        Vector3 destination = group.IsOffering ? group.Food.position : center+right*member.Offset.x+Vector3.up*member.Offset.y+forward*member.Offset.z;
        if ((member.Stage == 3 && group.IsOffering && !FishFeedingApproach.InFront(mouth,destination,forward)) ||
            (member.Stage >= 2 && Vector3.Dot(mouth-group.Viewer.position,forward)<-.1f)) ResetGatheringRoute(member);
        Vector3 goal = destination;
        if (member.Stage == 0)
        {
            goal = FishFeedingApproach.SideWaypoint(group.Viewer.position,forward,destination,member.Side,feedingSideClearance,member.RearDepth);
            if (Vector3.Distance(mouth,goal)<=feedingWaypointDistance) { member.Stage=1; fish.planningCooldown=0; }
        }
        if (member.Stage == 1)
        {
            goal = FishFeedingApproach.SideFrontWaypoint(group.Viewer.position,forward,destination,member.Side,feedingSideClearance,feedingFrontDistance);
            if (Vector3.Distance(mouth,goal)<=feedingWaypointDistance) { member.Stage=2; fish.planningCooldown=0; }
        }
        if (member.Stage == 2)
        {
            goal = group.IsOffering ? FishFeedingApproach.FrontWaypoint(destination,forward,feedingFrontDistance) : destination;
            if (Vector3.Distance(mouth,goal)<=feedingWaypointDistance) { member.Stage=3; fish.planningCooldown=0; goal=destination; }
        }
        bool inGatheringArea = !group.IsOffering && (mouth-center).sqrMagnitude < (group.Radius+1f)*(group.Radius+1f);
        SimulateApproach(fish,fish.position+transform.InverseTransformVector(goal-mouth),dt,inGatheringArea ? .4f : 1f);
        bool canEat = group.IsOffering && member.Stage==3 && Vector3.Dot(group.Food.position-group.Viewer.position,forward)>.05f &&
            FishFeedingApproach.CanBite(GetFeedingMouthPosition(fish),group.Food.position,forward,transform.TransformDirection(fish.heading),feedingBiteDistance);
        member.BiteTime = canEat ? member.BiteTime + dt : 0;
        // All simulation runs on Unity's main thread. The first successful bite releases every reservation immediately.
        if (canEat && member.BiteTime>=feedingBiteSeconds && !group.HasWinner && group.Active &&
            fish.materialSwitcher && group.Race.TryConsume(() => fish.materialSwitcher.TryApplyFeedingColor(member.Variant,group.FoodColor)))
        {
            group.Food.gameObject.SetActive(false);
            Debug.Log("[FishFeeding] " + fish.instance.name + " won the food; other fish return to normal swimming.",fish.instance);
            group.Cancel();
        }
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
    [Header("Feeding")]
    [SerializeField, Min(0.01f)] private float feedingBiteDistance = 0.35f;
    [SerializeField, Min(0f)] private float feedingBiteSeconds = 0.3f;
    [SerializeField, Min(0.5f), InspectorName("餌の前方で待つ距離")]
    private float feedingFrontDistance = 1.2f;
    [SerializeField, Min(0.5f), InspectorName("ダイバーの横を通る距離")]
    private float feedingSideClearance = 1.2f;
    [SerializeField, Min(0.05f), InspectorName("回り込み地点の到着距離")]
    private float feedingWaypointDistance = 0.35f;
    private FishAgent feedingFish;
    private Transform feedingTarget;
    private Transform feedingViewer;
    private int feedingApproachStage; // 0: move sideways, 1: pass beside diver, 2: front of food, 3: bite
    private float feedingApproachSide, feedingRearDepth;
    private int feedingVariantIndex;
    private Color? feedingFoodColor;
    private float feedingBiteElapsed;
    public bool FeedingCompleted { get; private set; }
    public bool HasActiveFeedingRequest => feedingTarget != null &&
        (FeedingCompleted || (feedingFish != null && feedingFish.instance && feedingFish.rootBone &&
            feedingFish.instance.activeInHierarchy && feedingFish.materialSwitcher && feedingFish.materialSwitcher.isActiveAndEnabled));

    public int FishCount => activeFish.Count;

    public bool TryGetFeedingCandidateDistance(Vector3 foodPosition, int foodColorIndex, out float squaredDistance,
        Color? foodColor = null, Transform viewer = null, bool frontOnly = false)
    {
        if (!viewer && Camera.main) viewer = Camera.main.transform;
        return FindFeedingCandidate(foodPosition, foodColorIndex, foodColor, viewer, frontOnly, out _, out _, out squaredDistance);
    }

    private bool FindFeedingCandidate(Vector3 foodPosition, int foodColorIndex, Color? foodColor, Transform viewer, bool frontOnly,
        out FishAgent candidate, out int variantIndex, out float squaredDistance)
    {
        candidate = null;
        variantIndex = -1;
        squaredDistance = float.PositiveInfinity;
        if (feedingTarget != null || !viewer) return false;
        Vector3 forward = FishFeedingApproach.Forward(viewer.forward);
        if (Vector3.Dot(foodPosition - viewer.position, forward) < 0.05f) return false;
        bool candidateInFront = false;
        foreach (FishAgent fish in activeFish)
        {
            if (!fish.instance || !fish.instance.activeInHierarchy || !fish.rootBone) continue;
            if (!fish.materialSwitcher) fish.materialSwitcher = fish.instance.GetComponentInChildren<StripedFishAnimatedQuadBillboard>();
            if (!fish.materialSwitcher || !fish.materialSwitcher.isActiveAndEnabled) continue;
            int variant;
            bool canFeed = foodColor.HasValue
                ? fish.materialSwitcher.TryGetFeedingVariant(foodColorIndex, foodColor.Value, out variant)
                : fish.materialSwitcher.TryGetFeedingVariant(foodColorIndex, out variant);
            if (!canFeed) continue;
            Vector3 mouth = GetFeedingMouthPosition(fish);
            bool inFront = FishFeedingApproach.InFront(mouth, foodPosition, forward);
            if (frontOnly && !inFront) continue;
            float distance = (mouth - foodPosition).sqrMagnitude;
            if (!FishFeedingApproach.PreferCandidate(inFront, distance, candidate != null, candidateInFront, squaredDistance)) continue;
            squaredDistance = distance;
            candidate = fish;
            candidateInFront = inFront;
            variantIndex = variant;
        }
        return candidate != null;
    }

    public bool TryBeginFeeding(Transform foodTarget, int foodColorIndex, Color? foodColor = null, Transform viewer = null)
    {
        if (!viewer && Camera.main) viewer = Camera.main.transform;
        if (!foodTarget || !FindFeedingCandidate(foodTarget.position, foodColorIndex, foodColor, viewer, false,
            out FishAgent candidate, out int variantIndex, out _)) return false;
        feedingFish = candidate;
        feedingTarget = foodTarget;
        feedingViewer = viewer;
        ResetFeedingApproach(GetFeedingMouthPosition(candidate), FishFeedingApproach.Forward(viewer.forward));
        feedingVariantIndex = variantIndex;
        feedingFoodColor = foodColor;
        feedingBiteElapsed = 0f;
        FeedingCompleted = false;
        feedingFish.planningCooldown = 0f;
        Debug.Log($"[FishFeeding] {candidate.instance.name} is approaching food color {foodColorIndex} (material variant {variantIndex}).", candidate.instance);
        return true;
    }

    public void CancelFeeding(Transform foodTarget)
    {
        if (feedingTarget != foodTarget) return;
        if (feedingFish != null)
        {
            feedingFish.returningFromFeeding = true;
            feedingFish.planningCooldown = 0f;
            feedingFish.cruiseRemaining = 0f;
        }
        feedingFish = null;
        feedingTarget = null;
        feedingViewer = null;
        feedingApproachStage = 0;
        feedingBiteElapsed = 0f;
        FeedingCompleted = false;
    }

    private Vector3 GetFeedingMouthPosition(FishAgent fish)
    {
        return fish.instance.transform.TransformPoint(fish.rootOffsetInInstance) +
            transform.TransformVector(fish.position - fish.spawnPosition) +
            fish.instance.transform.TransformVector(fish.species.feedingMouthLocalOffset);
    }

    private void ResetFeedingApproach(Vector3 mouth, Vector3 forward)
    {
        float depth = Vector3.Dot(mouth - feedingViewer.position, forward);
        feedingApproachSide = FishFeedingApproach.Side(mouth, feedingViewer.position, forward);
        feedingRearDepth = Mathf.Min(-0.5f, depth);
        // Even frontal fish visit the front waypoint so the final approach points toward the diver.
        feedingApproachStage = depth < 0f ? 0 : 2;
        feedingBiteElapsed = 0f;
        if (feedingFish != null) feedingFish.planningCooldown = 0f;
    }

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
        if (fish.gathering != null) { SimulateGathering(fish.gathering,dt); return; }
        if (fish == feedingFish && feedingTarget && !FeedingCompleted)
        {
            if (!feedingViewer) { CancelFeeding(feedingTarget); return; }
            Vector3 forward = FishFeedingApproach.Forward(feedingViewer.forward);
            Vector3 mouth = GetFeedingMouthPosition(fish);
            if (Vector3.Dot(feedingTarget.position - feedingViewer.position, forward) < 0.05f)
            { CancelFeeding(feedingTarget); return; }
            // Turning the view or moving the hand must never allow a rear-side bite.
            if ((feedingApproachStage == 3 && !FishFeedingApproach.InFront(mouth, feedingTarget.position, forward)) ||
                (feedingApproachStage == 2 && Vector3.Dot(mouth - feedingViewer.position, forward) < -0.1f))
                ResetFeedingApproach(mouth, forward);
            Vector3 goal = feedingTarget.position;
            if (feedingApproachStage == 0)
            {
                goal = FishFeedingApproach.SideWaypoint(feedingViewer.position, forward, feedingTarget.position,
                    feedingApproachSide, Mathf.Max(0.5f, feedingSideClearance), feedingRearDepth);
                if (Vector3.Distance(mouth, goal) <= feedingWaypointDistance)
                { feedingApproachStage = 1; fish.planningCooldown = 0f; }
            }
            if (feedingApproachStage == 1)
            {
                goal = FishFeedingApproach.SideFrontWaypoint(feedingViewer.position, forward, feedingTarget.position,
                    feedingApproachSide, Mathf.Max(0.5f, feedingSideClearance), Mathf.Max(0.5f, feedingFrontDistance));
                if (Vector3.Distance(mouth, goal) <= feedingWaypointDistance)
                { feedingApproachStage = 2; fish.planningCooldown = 0f; }
            }
            if (feedingApproachStage == 2)
            {
                float distance = Mathf.Max(feedingBiteDistance + feedingWaypointDistance + 0.1f, feedingFrontDistance);
                goal = FishFeedingApproach.FrontWaypoint(feedingTarget.position, forward, distance);
                if (Vector3.Distance(mouth, goal) <= feedingWaypointDistance && FishFeedingApproach.InFront(mouth, feedingTarget.position, forward))
                { feedingApproachStage = 3; fish.planningCooldown = 0f; goal = feedingTarget.position; }
            }
            Vector3 target = fish.position + transform.InverseTransformVector(goal - mouth);
            SimulateApproach(fish, target, dt);
            if (feedingApproachStage == 3 && FishFeedingApproach.CanBite(GetFeedingMouthPosition(fish),
                feedingTarget.position, forward, transform.TransformDirection(fish.heading), feedingBiteDistance))
            {
                feedingBiteElapsed += dt;
                if (feedingBiteElapsed >= feedingBiteSeconds)
                {
                    if (fish.materialSwitcher && fish.materialSwitcher.TryApplyFeedingColor(feedingVariantIndex, feedingFoodColor))
                    {
                        Debug.Log($"[FishFeeding] {fish.instance.name} ate the food and changed material.", fish.instance);
                        FeedingCompleted = true;
                        fish.returningFromFeeding = true;
                        fish.planningCooldown = 0f;
                        fish.cruiseRemaining = 0f;
                        feedingFish = null;
                    }
                    else CancelFeeding(feedingTarget);
                }
            }
            else feedingBiteElapsed = 0f;
            return;
        }
        if (fish.returningFromFeeding)
        {
            Vector3 clamped = new Vector3(Mathf.Clamp(fish.position.x, -halfWidth, halfWidth),
                Mathf.Clamp(fish.position.y, localHeight - halfHeight, localHeight + halfHeight),
                Mathf.Clamp(fish.position.z, -halfDepth, halfDepth));
            if ((clamped - fish.position).sqrMagnitude > 0.000001f)
            {
                Vector3 inner = new Vector3(Mathf.Clamp(fish.position.x, -halfWidth * 0.9f, halfWidth * 0.9f),
                    Mathf.Clamp(fish.position.y, localHeight - halfHeight * 0.9f, localHeight + halfHeight * 0.9f),
                    Mathf.Clamp(fish.position.z, -halfDepth * 0.9f, halfDepth * 0.9f));
                SimulateApproach(fish, inner, dt);
                return;
            }
            fish.returningFromFeeding = false;
        }
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

    private void SimulateApproach(FishAgent fish, Vector3 target, float dt, float speedScale = 1f)
    {
        dt = Mathf.Min(dt, 0.08f);
        Vector3 delta = target - fish.position;
        float distance = delta.magnitude;
        if (distance < 0.0001f) { fish.speed = 0f; ApplyFishPose(fish); return; }
        Vector3 desired = delta / distance;
        float maxAngularVelocity = Mathf.Max(1f, fish.species.maxAngularVelocity);
        fish.planningCooldown -= dt;
        if (fish.planningCooldown <= 0f)
        {
            fish.targetHeading = ChooseSafeHeading(fish.position, fish.heading, desired, fish.targetHeading,
                Mathf.Max(0.1f, fish.speed), fish.turnRate, maxAngularVelocity, 0.8f, true);
            fish.planningCooldown = routeCheckInterval;
        }
        fish.turnRate = Mathf.MoveTowards(fish.turnRate, maxAngularVelocity, maxAngularVelocity * 1.5f * dt);
        fish.heading = Vector3.RotateTowards(fish.heading, fish.targetHeading,
            fish.turnRate * Mathf.Deg2Rad * dt, 0f).normalized;
        float acceleration = Mathf.Max(0.01f, fish.species.maxAcceleration);
        float targetSpeed = Mathf.Min(fish.species.maxSpeed * speedScale, Mathf.Sqrt(2f * acceleration * distance));
        targetSpeed *= Mathf.Clamp01(Vector3.Dot(fish.heading, desired) + 0.1f);
        fish.speed = Mathf.MoveTowards(fish.speed, targetSpeed, acceleration * dt);
        fish.position += fish.heading * Mathf.Min(fish.speed * dt, distance);
        ApplyFishPose(fish);
    }

    private Vector3 ChooseSafeHeading(Vector3 start, Vector3 currentHeading, Vector3 preferredHeading,
        Vector3 previousPlan, float speed, float currentTurnRate, float maxAngularVelocity, float horizon,
        bool allowOutsideArea = false)
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
                    if (!allowOutsideArea && boundaryError > 0f)
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

            Component added = instance.AddComponent(componentType);
            if (added is StripedFishAnimatedQuadBillboard striped && template is StripedFishAnimatedQuadBillboard stripedTemplate)
                striped.CopyMaterialVariantsFrom(stripedTemplate);
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
