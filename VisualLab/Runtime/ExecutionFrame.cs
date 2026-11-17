using System;
using System.Collections.Generic;
using VisualLab.Runtime.Nodes;

namespace VisualLab.Runtime;

/// <summary>Tracks values and execution for one event chain.</summary>
public sealed class ExecutionFrame
{
    /// <summary>Gets or sets the node that started the chain.</summary>
    public RuntimeNode EventNode { get; set; }

    /// <summary>Gets or sets the arguments of the current event.</summary>
    public object EventArgs { get; set; }

    /// <summary>Gets the output values produced by the current chain.</summary>
    public Dictionary<string, object> Outputs { get; } = new (StringComparer.Ordinal);

    /// <summary>Gets or sets a value indicating whether the current chain has stopped.</summary>
    public bool Stopped { get; set; }

    /// <summary>Gets or sets the number of execution steps taken.</summary>
    public int Steps { get; set; }

    /// <summary>Gets or sets the current execution depth.</summary>
    public int Depth { get; set; }

    /// <summary>Gets or sets the synchronous function call depth.</summary>
    public int FunctionDepth { get; set; }

    /// <summary>Gets or sets the trace being recorded, if any.</summary>
    public TraceRecord Trace { get; set; }

    /// <summary>Gets the output pins currently being evaluated.</summary>
    public HashSet<string> Evaluating { get; } = new ();

    /// <summary>Creates a lookup key for an output pin.</summary>
    /// <param name="node">The runtime node.</param>
    /// <param name="pin">The pin ID.</param>
    /// <returns>The node and pin key.</returns>
    public static string Key(RuntimeNode node, string pin) => node.Id + "\n" + pin;

    /// <summary>Copies event data and output values for a delayed chain.</summary>
    /// <returns>The copied frame.</returns>
    public ExecutionFrame Snapshot()
    {
        ExecutionFrame copy = new () { EventNode = EventNode, EventArgs = EventArgs };
        foreach (KeyValuePair<string, object> pair in Outputs)
            copy.Outputs[pair.Key] = pair.Value;

        return copy;
    }
}
