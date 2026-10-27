using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Deterministic feeding interaction checks callable from the Unity Editor.</summary>
public static class ScoopInteractionChecks
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

        Vector3 bowl = Vector3.zero;
        Vector3 forward = Vector3.forward;
        Vector3 outside = new Vector3(0.4f, 0f, 0f);
        Vector3 inside = Vector3.zero;
        Vector3 lifted = new Vector3(0f, 0.13f, 0f);
        Vector3 offer = new Vector3(0f, 0.13f, 0.3f);
        void Step(ScoopFeedingLogic state, Vector3 position, bool tracked = true, float dt = 0.1f) =>
            state.Tick(position, tracked, dt, bowl, forward);
        ScoopFeedingLogic Held()
        {
            var state = new ScoopFeedingLogic();
            Step(state, outside);
            Step(state, inside);
            Step(state, lifted);
            return state;
        }
        ScoopFeedingLogic Offered()
        {
            var state = Held();
            Step(state, offer);
            for (int i = 0; i < 51; i++) Step(state, offer);
            return state;
        }

        var normal = Held();
        Check(normal.State == ScoopFeedingState.Held, "entry and lift picks up food");
        Step(normal, offer);
        for (int i = 0; i < 51; i++) Step(normal, offer);
        Check(normal.FoodAvailableToFish, "five seconds presents food");
        normal.ConsumeFood();
        Check(normal.State == ScoopFeedingState.AwaitEntry && !normal.FoodAvailableToFish,
            "consumption returns food to waiting state");
        Step(normal, outside);
        Step(normal, inside);
        Step(normal, lifted);
        Check(normal.State == ScoopFeedingState.Held, "food can be picked up again after consumption");

        var returned = Held();
        Step(returned, offer);
        Step(returned, inside);
        Check(returned.State == ScoopFeedingState.AwaitEntry, "food can be returned after leaving bowl");

        var lost = new ScoopFeedingLogic();
        Step(lost, outside, false);
        Step(lost, inside, false);
        Step(lost, lifted, false);
        Check(lost.State == ScoopFeedingState.AwaitEntry, "untracked movement cannot pick up food");

        var high = new ScoopFeedingLogic();
        Step(high, new Vector3(0.4f, 0.5f, 0f));
        Step(high, new Vector3(0f, 0.5f, 0f));
        Step(high, new Vector3(0f, 0.7f, 0f));
        Check(high.State == ScoopFeedingState.AwaitEntry, "movement above the bowl cannot pick up food");

        var reacquired = Offered();
        Step(reacquired, offer, false);
        Step(reacquired, inside);
        Check(!reacquired.FoodAvailableToFish && reacquired.State == ScoopFeedingState.Held,
            "reacquisition at the bowl cannot immediately offer food");

        var initialInside = new ScoopFeedingLogic();
        Step(initialInside, inside);
        Step(initialInside, lifted);
        Step(initialInside, inside);
        Check(initialInside.State != ScoopFeedingState.Held && !initialInside.FoodAvailableToFish,
            "starting inside the bowl cannot pick up food");

        var interrupted = Held();
        Step(interrupted, offer);
        for (int i = 0; i < 30; i++) Step(interrupted, offer);
        Check(interrupted.OfferProgress > 0f && !interrupted.FoodAvailableToFish,
            "partial presentation advances progress");
        Step(interrupted, offer, false);
        Check(interrupted.State == ScoopFeedingState.Held && interrupted.OfferProgress == 0f &&
              !interrupted.FoodAvailableToFish, "tracking loss resets presentation progress");
        Step(interrupted, offer);
        for (int i = 0; i < 30; i++) Step(interrupted, offer);
        Check(!interrupted.FoodAvailableToFish, "reacquired presentation requires a new five seconds");
        for (int i = 0; i < 22; i++) Step(interrupted, offer);
        Check(interrupted.FoodAvailableToFish, "continuous presentation succeeds after reacquisition");

        if (failures.Count != 0) throw new InvalidOperationException(
            $"Scoop interaction checks: {failures.Count}/{checks} failed: " + string.Join("; ", failures));
        return new { Passed = checks, Failed = failures.Count };
    }
}
