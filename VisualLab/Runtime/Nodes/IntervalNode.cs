using System;
using System.Collections.Generic;
using LabApi.Features.Wrappers;
using MEC;
using VisualLab.Runtime;

namespace VisualLab.Runtime.Nodes;

/// <summary>event.interval: fires every `seconds` while a round is in progress.</summary>
public sealed class IntervalNode : NodeImplementation
{
    private CoroutineHandle _handle;

    /// <summary>Starts the repeating event timer.</summary>
    /// <param name="runtime">The graph runtime.</param>
    /// <param name="node">The runtime node.</param>
    public override void Start(GraphRuntime runtime, RuntimeNode node)
    {
        object configured = node.Graph.Values.TryGetValue("seconds", out object seconds) ? seconds : 30.0;
        float interval = Math.Max(0.1f, (float)Values.Coerce(configured, typeof(float)));
        _handle = Timing.RunCoroutine(Loop(runtime, node, interval));
        runtime.Track(_handle);
    }

    /// <summary>Stops the repeating event timer.</summary>
    /// <param name="runtime">The graph runtime.</param>
    /// <param name="node">The runtime node.</param>
    public override void Stop(GraphRuntime runtime, RuntimeNode node)
    {
        Timing.KillCoroutines(_handle);
        runtime.Untrack(_handle);
    }

    private static IEnumerator<float> Loop(GraphRuntime runtime, RuntimeNode node, float seconds)
    {
        while (runtime.Running)
        {
            yield return Timing.WaitForSeconds(seconds);
            if (runtime.Running && Round.IsRoundInProgress)
                runtime.Fire(node, null);
        }
    }
}
