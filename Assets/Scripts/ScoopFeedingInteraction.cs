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
    private const float LiftHeight = 0.12f;
    private const float EntryMinHeight = -0.12f;
    private const float EntryMaxHeight = 0.10f;
    private const float OfferDistance = 0.25f;
    private const float OfferForward = 0.18f;
    private const float OfferSeconds = 5f;
    private const float ReturnHeight = 0.16f;

    private bool trackingPreviously;
    private bool sawOutside;
    private bool leftBowlWhileHeld;
    private float entryHeight;
    private float offerElapsed;

    public ScoopFeedingState State { get; private set; } = ScoopFeedingState.AwaitEntry;
    public bool IsTracking { get; private set; }
    public bool FoodAvailableToFish => IsTracking && State == ScoopFeedingState.Offered;
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
                    return "手を上げて餌をすくう";
                case ScoopFeedingState.Held:
                    return "前へ差し出して5秒待つ";
                case ScoopFeedingState.Offered:
                    return "魚が食べに来ます";
                default:
                    return "皿の外から手を入れる";
            }
        }
    }

    public void Reset()
    {
        State = ScoopFeedingState.AwaitEntry;
        sawOutside = false;
        leftBowlWhileHeld = false;
        entryHeight = 0f;
        offerElapsed = 0f;
        trackingPreviously = false;
        IsTracking = false;
    }

    public void Tick(Vector3 handPosition, bool tracking, float dt, Vector3 bowlPosition, Vector3 forward)
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
                sawOutside = false;
            }
            return;
        }

        IsTracking = true;
        Vector3 offset = handPosition - bowlPosition;
        Vector3 horizontal = Vector3.ProjectOnPlane(offset, Vector3.up);
        bool inside = horizontal.sqrMagnitude <= PickupRadius * PickupRadius;
        bool insideEntry = inside && offset.y >= EntryMinHeight && offset.y <= EntryMaxHeight;

        if (!trackingPreviously)
        {
            trackingPreviously = true;
            if (State == ScoopFeedingState.AwaitEntry)
                sawOutside = !insideEntry;
            // Reacquisition alone cannot pick up, return, or advance the offer.
            return;
        }

        switch (State)
        {
            case ScoopFeedingState.AwaitEntry:
                if (!insideEntry)
                    sawOutside = true;
                else if (sawOutside)
                {
                    entryHeight = handPosition.y;
                    State = ScoopFeedingState.Armed;
                }
                break;

            case ScoopFeedingState.Armed:
                if (!inside)
                {
                    State = ScoopFeedingState.AwaitEntry;
                    sawOutside = true;
                }
                else if (handPosition.y - entryHeight >= LiftHeight)
                {
                    State = ScoopFeedingState.Held;
                    leftBowlWhileHeld = false;
                    offerElapsed = 0f;
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

    public ScoopFeedingState State => logic.State;
    public string Guide => logic.Guide;
    public bool FoodAvailableToFish => logic.FoodAvailableToFish;
    public float OfferProgress => logic.OfferProgress;
    public Vector3 FoodPosition => food != null ? food.position : Vector3.zero;

    public void Configure(Transform hand, Transform food, Transform bowl, Camera camera)
    {
        this.hand = hand;
        this.food = food;
        this.bowl = bowl;
        viewCamera = camera;
        if (food != null && bowl != null)
            foodHomeLocal = bowl.InverseTransformPoint(food.position);
        logic.Reset();
    }

    public void Tick(Vector3 handPosition, bool tracking, float dt)
    {
        if (bowl == null || food == null)
            return;
        Vector3 forward = viewCamera != null ? viewCamera.transform.forward : Vector3.forward;
        logic.Tick(handPosition, tracking, dt, bowl.position, forward);
        if (logic.IsTracking && hand != null)
            hand.position = handPosition;
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
}
