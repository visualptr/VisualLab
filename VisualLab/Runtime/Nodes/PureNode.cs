using System;
using VisualLab.Runtime;

namespace VisualLab.Runtime.Nodes;

/// <summary>Reads a built-in pure node output.</summary>
/// <param name="runtime">The graph runtime.</param>
/// <param name="node">The runtime node.</param>
/// <param name="pin">The pin ID.</param>
/// <param name="frame">The current execution frame.</param>
/// <returns>The output value.</returns>
public delegate object PureFn(GraphRuntime runtime, RuntimeNode node, string pin, ExecutionFrame frame);

/// <summary>A hand-written node evaluated on demand.</summary>
public sealed class PureNode : NodeImplementation
{
    private readonly PureFn _eval;
    private readonly Type _outputType;

    /// <summary>Initializes a new instance of the <see cref="PureNode"/> class.</summary>
    /// <param name="eval">The output callback.</param>
    /// <param name="outputType">The output type, or object if omitted.</param>
    public PureNode(PureFn eval, Type outputType = null)
    {
        _eval = eval;
        _outputType = outputType ?? typeof(object);
    }

    /// <summary>Gets a value indicating whether the node is evaluated without an execution wire.</summary>
    public override bool Pure => true;

    /// <summary>Evaluates the requested output.</summary>
    /// <param name="runtime">The graph runtime.</param>
    /// <param name="node">The runtime node.</param>
    /// <param name="pin">The pin ID.</param>
    /// <param name="frame">The current execution frame.</param>
    /// <returns>The output value.</returns>
    public override object Eval(GraphRuntime runtime, RuntimeNode node, string pin, ExecutionFrame frame) => _eval(runtime, node, pin, frame);

    /// <summary>Gets the configured output type.</summary>
    /// <param name="pin">The pin ID.</param>
    /// <returns>The output type.</returns>
    public override Type OutputType(string pin) => _outputType;
}
