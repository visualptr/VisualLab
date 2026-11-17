using System;
using VisualLab.Runtime;
using VisualLab.Runtime.Payloads;

namespace VisualLab.Runtime.Nodes;

/// <summary>event.signal: fires when a signal with its name is sent.</summary>
public sealed class SignalNode : PayloadNode
{
    private string _name;

    /// <summary>Gets the payload type that carries the outputs.</summary>
    protected override Type PayloadType => typeof(SignalCall);

    /// <summary>Starts listening for the signal.</summary>
    /// <param name="runtime">The graph runtime.</param>
    /// <param name="node">The runtime node.</param>
    public override void Start(GraphRuntime runtime, RuntimeNode node)
    {
        _name = Setting(node, "name", string.Empty).Trim();
        if (_name.Length == 0)
            throw new ArgumentException("a signal needs a name");

        SignalBus.Listen(_name, runtime, node);
    }

    /// <summary>Stops listening for the signal.</summary>
    /// <param name="runtime">The graph runtime.</param>
    /// <param name="node">The runtime node.</param>
    public override void Stop(GraphRuntime runtime, RuntimeNode node)
    {
        if (_name != null)
            SignalBus.Forget(_name, node);

        _name = null;
    }
}
