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
        Vector3 inside = Vector3.zero;
        Vector3 lifted = new Vector3(0f, 0.07f, 0f);
        Vector3 offer = new Vector3(0f, 0.13f, 0.3f);
        void Step(ScoopFeedingLogic state, Vector3 position, bool tracked = true, float dt = 0.1f, bool touching = false) =>
            state.Tick(position, tracked, dt, bowl, Vector3.forward, touching);
        ScoopFeedingLogic Held()
        {
            var state = new ScoopFeedingLogic();
            Step(state, inside, touching: true);
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

        var initialContact = new ScoopFeedingLogic();
        Step(initialContact, inside, touching: true);
        Check(initialContact.State == ScoopFeedingState.Armed, "initial contact arms pickup");
        Step(initialContact, inside, touching: true);
        Check(initialContact.State == ScoopFeedingState.Armed, "contact alone cannot pick up food");
        Step(initialContact, new Vector3(.08f, .03f, 0f));
        Check(initialContact.State == ScoopFeedingState.Armed && initialContact.LiftProgress > 0f,
            "partial diagonal lift shows progress");
        Step(initialContact, new Vector3(.10f, .07f, 0f));
        Check(initialContact.State == ScoopFeedingState.Held, "diagonal lift picks up food");

        var noContact = new ScoopFeedingLogic();
        Step(noContact, inside);
        Step(noContact, lifted);
        Check(noContact.State == ScoopFeedingState.AwaitEntry, "movement without contact cannot pick up food");
        var far = new ScoopFeedingLogic();
        Step(far, inside, touching: true);
        Step(far, new Vector3(.25f, 0f, 0f));
        Step(far, new Vector3(.25f, .07f, 0f));
        Check(far.State == ScoopFeedingState.AwaitEntry, "distant old contact cannot pick up food");
        var below = new ScoopFeedingLogic();
        Step(below, inside, touching: true);
        Step(below, new Vector3(0f, -.13f, 0f));
        Check(below.State == ScoopFeedingState.AwaitEntry, "moving down cancels old contact");
        var dipping = new ScoopFeedingLogic();
        Step(dipping, inside, touching: true);
        Step(dipping, new Vector3(0f, -.04f, 0f), touching: true);
        Step(dipping, new Vector3(.10f, .021f, 0f));
        Check(dipping.State == ScoopFeedingState.Held,
            "continuous contact lowers lift baseline without shifting horizontal origin");
        var horizontalOrigin = new ScoopFeedingLogic();
        Step(horizontalOrigin, inside, touching: true);
        Step(horizontalOrigin, new Vector3(.08f, -.02f, 0f), touching: true);
        Step(horizontalOrigin, new Vector3(.13f, .05f, 0f));
        Check(horizontalOrigin.State == ScoopFeedingState.AwaitEntry,
            "contact while lowering does not shift horizontal origin");
        var lost = new ScoopFeedingLogic();
        Step(lost, inside, touching: true);
        Step(lost, inside, false);
        Step(lost, lifted);
        Check(lost.State == ScoopFeedingState.AwaitEntry, "tracking loss cancels an unheld pickup");
        Step(lost, inside, touching: true);
        Check(lost.State == ScoopFeedingState.Armed, "reacquired contact only arms pickup");

        var normal = Held();
        Check(normal.State == ScoopFeedingState.Held, "contact and lift picks up food");
        Step(normal, offer);
        for (int i = 0; i < 51; i++) Step(normal, offer);
        Check(normal.FoodAvailableToFish, "five seconds presents food");
        normal.ConsumeFood();
        Check(normal.State == ScoopFeedingState.AwaitEntry && !normal.FoodAvailableToFish,
            "consumption returns food to waiting state");
        Step(normal, inside, touching: true);
        Step(normal, lifted);
        Check(normal.State == ScoopFeedingState.Held, "food can be picked up again after consumption");

        var returned = Held();
        Step(returned, offer);
        Step(returned, inside);
        Check(returned.State == ScoopFeedingState.AwaitEntry, "food can be returned after leaving bowl");
        var untracked = new ScoopFeedingLogic();
        Step(untracked, inside, false, touching: true);
        Step(untracked, lifted, false);
        Check(untracked.State == ScoopFeedingState.AwaitEntry, "untracked contact cannot pick up food");
        var reacquired = Offered();
        Step(reacquired, offer, false);
        Step(reacquired, inside);
        Check(!reacquired.FoodAvailableToFish && reacquired.State == ScoopFeedingState.Held,
            "reacquisition at the bowl cannot immediately offer food");
        var interrupted = Held();
        Step(interrupted, offer);
        for (int i = 0; i < 30; i++) Step(interrupted, offer);
        Check(interrupted.OfferProgress > 0f && !interrupted.FoodAvailableToFish, "partial presentation advances progress");
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

