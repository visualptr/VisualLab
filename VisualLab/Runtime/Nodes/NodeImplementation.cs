using System;
using VisualLab.Runtime;

namespace VisualLab.Runtime.Nodes;

/// <summary>Defines how a node runs and reads values.</summary>
public abstract class NodeImplementation
{
    /// <summary>Gets a value indicating whether the node is evaluated without an execution wire.</summary>
    public virtual bool Pure => false;

    /// <summary>Gets a value indicating whether the node's inputs change its event after the chain runs.</summary>
    public virtual bool WritesOutcomes => false;

    /// <summary>Evaluates a node output.</summary>
    /// <param name="runtime">The graph runtime.</param>
    /// <param name="node">The runtime node.</param>
    /// <param name="pin">The pin ID.</param>
    /// <param name="frame">The current execution frame.</param>
    /// <returns>The output value.</returns>
    public virtual object Eval(GraphRuntime runtime, RuntimeNode node, string pin, ExecutionFrame frame) => null;

    /// <summary>Runs the node in the current chain.</summary>
    /// <param name="runtime">The graph runtime.</param>
    /// <param name="node">The runtime node.</param>
    /// <param name="frame">The current execution frame.</param>
    public virtual void Run(GraphRuntime runtime, RuntimeNode node, ExecutionFrame frame)
    {
    }

    /// <summary>Starts the node when its graph starts.</summary>
    /// <param name="runtime">The graph runtime.</param>
    /// <param name="node">The runtime node.</param>
    public virtual void Start(GraphRuntime runtime, RuntimeNode node)
    {
    }

    /// <summary>Stops the node when its graph stops.</summary>
    /// <param name="runtime">The graph runtime.</param>
    /// <param name="node">The runtime node.</param>
    public virtual void Stop(GraphRuntime runtime, RuntimeNode node)
    {
    }

    /// <summary>Gets the CLR type used for an output.</summary>
    /// <param name="pin">The pin ID.</param>
    /// <returns>The output type.</returns>
    public virtual Type OutputType(string pin) => typeof(object);
}
