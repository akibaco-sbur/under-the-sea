using System.Collections.Generic;
using UnityEngine;

/// <summary>Temporarily suspends animation evaluation without resetting playback state.</summary>
internal sealed class DiverFeedingAnimationPause
{
    struct State
    {
        internal bool Enabled, KeepState, WriteDefaults;
    }
    readonly Dictionary<Animator, State> originalStates = new Dictionary<Animator, State>();

    internal void Pause(Animator[] animators)
    {
        if (animators == null) return;
        foreach (Animator animator in animators)
        {
            if (!animator || originalStates.ContainsKey(animator)) continue;
            originalStates.Add(animator, new State { Enabled = animator.enabled,
                KeepState = animator.keepAnimatorStateOnDisable, WriteDefaults = animator.writeDefaultValuesOnDisable });
            animator.keepAnimatorStateOnDisable = true;
            animator.writeDefaultValuesOnDisable = false;
            // Speed=0 would still evaluate/write animated arm bones each frame.
            animator.enabled = false;
        }
    }

    internal void Restore()
    {
        foreach (var state in originalStates)
            if (state.Key)
            {
                state.Key.enabled = state.Value.Enabled;
                state.Key.keepAnimatorStateOnDisable = state.Value.KeepState;
                state.Key.writeDefaultValuesOnDisable = state.Value.WriteDefaults;
            }
        originalStates.Clear();
    }
}
