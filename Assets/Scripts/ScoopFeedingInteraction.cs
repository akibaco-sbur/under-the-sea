using UnityEngine;

public enum ScoopFeedingState
{
    AwaitEntry,
    Armed,
    Held,
    Offered
}

/// <summary>State transitions without scene objects or physics dependencies.</summary>
public sealed class ScoopFeedingLogic
{
    private const float PickupRadius = 0.22f;
    private const float LiftHeight = 0.06f;
    private const float ContactCancelRadius = 0.12f;
    private const float ContactCancelDrop = 0.12f;
    private const float OfferDistance = 0.25f;
    private const float OfferForward = 0.18f;
    private const float OfferSeconds = 5f;
    private const float ReturnHeight = 0.16f;

    private bool trackingPreviously;
    private bool leftBowlWhileHeld;
    private Vector3 contactPosition;
    private float liftProgress;
    private float offerElapsed;

    public ScoopFeedingState State { get; private set; } = ScoopFeedingState.AwaitEntry;
    public bool IsTracking { get; private set; }
    public bool FoodAvailableToFish => IsTracking && State == ScoopFeedingState.Offered;
    public bool IsOffering => IsTracking && (State == ScoopFeedingState.Offered ||
        (State == ScoopFeedingState.Held && offerElapsed > 0f));
    public float LiftProgress => State == ScoopFeedingState.Armed ? liftProgress : 0f;
    public float OfferProgress => State == ScoopFeedingState.Offered ? 1f :
        State == ScoopFeedingState.Held ? Mathf.Clamp01(offerElapsed / OfferSeconds) : 0f;

    public string Guide
    {
        get
        {
            if (!IsTracking)
                return "手をカメラに見せてください";
            switch (State)
            {
                case ScoopFeedingState.Armed:
                    return "餌に触れました。そのまま持ち上げる";
                case ScoopFeedingState.Held:
                    return "前へ差し出して5秒待つ";
                case ScoopFeedingState.Offered:
                    return "魚が食べに来ます";
                default:
                    return "餌に手を近づける";
            }
        }
    }

    public void Reset()
    {
        State = ScoopFeedingState.AwaitEntry;
        leftBowlWhileHeld = false;
        contactPosition = Vector3.zero;
        liftProgress = 0f;
        offerElapsed = 0f;
        trackingPreviously = false;
        IsTracking = false;
    }

    public void Tick(Vector3 handPosition, bool tracking, float dt, Vector3 bowlPosition, Vector3 forward, bool touchingFood = false)
    {
        if (!tracking || !IsFinite(handPosition) || !IsFinite(bowlPosition))
        {
            IsTracking = false;
            trackingPreviously = false;
            // The offer requires five continuous seconds of tracked presentation.
            // Keep held food, but never make it available on reacquisition alone.
            offerElapsed = 0f;
            if (State == ScoopFeedingState.Offered)
                State = ScoopFeedingState.Held;
            // A lift must be continuous across the entry and lift movements.
            if (State == ScoopFeedingState.Armed)
            {
                State = ScoopFeedingState.AwaitEntry;
                liftProgress = 0f;
            }
            return;
        }

        IsTracking = true;
        Vector3 offset = handPosition - bowlPosition;
        Vector3 horizontal = Vector3.ProjectOnPlane(offset, Vector3.up);
        bool inside = horizontal.sqrMagnitude <= PickupRadius * PickupRadius;

        if (!trackingPreviously)
        {
            trackingPreviously = true;
            // Initial contact may arm; reacquisition alone cannot lift or offer.
            if (State == ScoopFeedingState.AwaitEntry && touchingFood)
            {
                contactPosition = handPosition;
                liftProgress = 0f;
                State = ScoopFeedingState.Armed;
            }
            return;
        }

        switch (State)
        {
            case ScoopFeedingState.AwaitEntry:
                if (touchingFood)
                {
                    contactPosition = handPosition;
                    liftProgress = 0f;
                    State = ScoopFeedingState.Armed;
                }
                break;

            case ScoopFeedingState.Armed:
                // Keep the original horizontal contact point but allow a lower
                // lift baseline while the hand remains in contact with food.
                if (touchingFood && handPosition.y < contactPosition.y)
                    contactPosition.y = handPosition.y;
                Vector3 movement = handPosition - contactPosition;
                Vector3 sideways = Vector3.ProjectOnPlane(movement, Vector3.up);
                if (sideways.sqrMagnitude > ContactCancelRadius * ContactCancelRadius ||
                    movement.y < -ContactCancelDrop)
                {
                    State = ScoopFeedingState.AwaitEntry;
                    liftProgress = 0f;
                }
                else
                {
                    liftProgress = Mathf.Clamp01(movement.y / LiftHeight);
                    if (movement.y >= LiftHeight)
                    {
                        State = ScoopFeedingState.Held;
                        leftBowlWhileHeld = false;
                        offerElapsed = 0f;
                    }
                }
                break;

            case ScoopFeedingState.Held:
            case ScoopFeedingState.Offered:
                if (!inside)
                    leftBowlWhileHeld = true;
                if (leftBowlWhileHeld && inside && handPosition.y <= bowlPosition.y + ReturnHeight)
                {
                    Reset();
                    IsTracking = true;
                    trackingPreviously = true;
                    return;
                }

                Vector3 horizontalForward = Vector3.ProjectOnPlane(forward, Vector3.up);
                if (!IsFinite(horizontalForward) || horizontalForward.sqrMagnitude < 0.000001f)
                    horizontalForward = Vector3.forward;
                horizontalForward.Normalize();
                bool offering = horizontal.sqrMagnitude >= OfferDistance * OfferDistance &&
                                Vector3.Dot(horizontal, horizontalForward) >= OfferForward;
                if (!offering)
                {
                    offerElapsed = 0f;
                    if (State == ScoopFeedingState.Offered)
                        State = ScoopFeedingState.Held;
                }
                else if (State == ScoopFeedingState.Held && !float.IsNaN(dt) && !float.IsInfinity(dt) && dt > 0f)
                {
                    offerElapsed += dt;
                    if (offerElapsed >= OfferSeconds)
                        State = ScoopFeedingState.Offered;
                }
                break;
        }
    }

    public void ConsumeFood()
    {
        if (FoodAvailableToFish)
            Reset();
    }

    private static bool IsFinite(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
               !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
               !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}

/// <summary>Connects the scoop state to separate hand, food, and bowl visuals.</summary>
public sealed class ScoopFeedingInteraction : MonoBehaviour
{
    private readonly ScoopFeedingLogic logic = new ScoopFeedingLogic();
    private Transform hand;
    private Transform food;
    private Transform bowl;
    private Camera viewCamera;
    private Vector3 foodHomeLocal;
    private SphereCollider handCollider;
    private BoxCollider foodCollider;

    public ScoopFeedingState State => logic.State;
    public string Guide => logic.Guide;
    public bool FoodAvailableToFish => logic.FoodAvailableToFish;
    public bool IsOffering => logic.IsOffering;
    public float OfferProgress => logic.OfferProgress;
    public float LiftProgress => logic.LiftProgress;
    public bool IsTouchingFood { get; private set; }
    public string PickupHint
    {
        get
        {
            if (hand == null || food == null || IsTouchingFood)
                return string.Empty;
            Vector3 delta = food.position - hand.position;
            Vector3 local = viewCamera != null ? viewCamera.transform.InverseTransformDirection(delta) : delta;
            float x = Mathf.Abs(local.x), y = Mathf.Abs(local.y), z = Mathf.Abs(local.z);
            if (z >= x && z >= y) return local.z >= 0f ? "もう少し奥へ" : "もう少し手前へ";
            if (y >= x) return local.y >= 0f ? "もう少し上へ" : "もう少し下へ";
            return local.x >= 0f ? "もう少し右へ" : "もう少し左へ";
        }
    }
    public Vector3 FoodPosition => food != null ? food.position : Vector3.zero;

    public void Configure(Transform hand, Transform food, Transform bowl, Camera camera)
    {
        this.hand = hand;
        this.food = food;
        this.bowl = bowl;
        viewCamera = camera;
        if (food != null && bowl != null)
            foodHomeLocal = bowl.InverseTransformPoint(food.position);
        handCollider = hand != null ? hand.GetComponent<SphereCollider>() : null;
        if (hand != null && handCollider == null)
        {
            handCollider = hand.gameObject.AddComponent<SphereCollider>();
            handCollider.center = Vector3.zero;
            handCollider.radius = 0.065f;
            handCollider.isTrigger = true;
        }
        foodCollider = food != null ? food.GetComponent<BoxCollider>() : null;
        if (food != null && foodCollider == null)
        {
            foodCollider = food.gameObject.AddComponent<BoxCollider>();
            foodCollider.size = Vector3.one;
            foodCollider.isTrigger = true;
        }
        IsTouchingFood = false;
        logic.Reset();
    }

    public static bool CheckContact(Collider handCollider, Collider foodCollider)
    {
        if (handCollider == null || foodCollider == null || !handCollider.enabled || !foodCollider.enabled ||
            !handCollider.gameObject.activeInHierarchy || !foodCollider.gameObject.activeInHierarchy)
            return false;
        return Physics.ComputePenetration(handCollider, handCollider.transform.position, handCollider.transform.rotation,
            foodCollider, foodCollider.transform.position, foodCollider.transform.rotation, out _, out _);
    }

    public void Tick(Vector3 handPosition, bool tracking, float dt)
    {
        if (bowl == null || food == null)
            return;
        Vector3 forward = viewCamera != null ? viewCamera.transform.forward : Vector3.forward;
        bool validPosition = !float.IsNaN(handPosition.x) && !float.IsInfinity(handPosition.x) &&
                             !float.IsNaN(handPosition.y) && !float.IsInfinity(handPosition.y) &&
                             !float.IsNaN(handPosition.z) && !float.IsInfinity(handPosition.z);
        if (tracking && validPosition && hand != null)
            hand.position = handPosition;
        IsTouchingFood = tracking && validPosition &&
                         (logic.State == ScoopFeedingState.AwaitEntry || logic.State == ScoopFeedingState.Armed) &&
                         CheckContact(handCollider, foodCollider);
        logic.Tick(handPosition, tracking, dt, bowl.position, forward, IsTouchingFood);
        if (logic.State == ScoopFeedingState.Held || logic.State == ScoopFeedingState.Offered)
        {
            if (logic.IsTracking)
                food.position = handPosition + Vector3.up * 0.045f;
        }
        else
        {
            food.position = bowl.TransformPoint(foodHomeLocal);
        }
    }

    public void ConsumeFood()
    {
        logic.ConsumeFood();
        if (food != null && bowl != null && logic.State == ScoopFeedingState.AwaitEntry)
            food.position = bowl.TransformPoint(foodHomeLocal);
    }

    /// <summary>Reset the in-progress scoop attempt and return food to the bowl.</summary>
    public void ResetInteraction()
    {
        logic.Reset();
        IsTouchingFood = false;
        if (food != null && bowl != null)
            food.position = bowl.TransformPoint(foodHomeLocal);
    }
}
