using System;
using System.Collections.Generic;
using System.Linq;
using VisualLab.Bindings;
using VisualLab.Graphs;

namespace VisualLab.Runtime.Nodes;

/// <summary>Provides a local function's scoped parameter values.</summary>
public sealed class FunctionNode : NodeImplementation
{
    private RuntimeNode _node;

    /// <summary>Initializes the function signature.</summary>
    /// <param name="runtime">The owning runtime.</param>
    /// <param name="node">The entry node.</param>
    public override void Start(GraphRuntime runtime, RuntimeNode node) => _node = node;

    /// <summary>Reads a parameter in this invocation.</summary>
    /// <param name="runtime">The owning runtime.</param>
    /// <param name="node">The entry node.</param>
    /// <param name="pin">The parameter identifier.</param>
    /// <param name="frame">The invocation frame.</param>
    /// <returns>The argument value.</returns>
    public override object Eval(GraphRuntime runtime, RuntimeNode node, string pin, ExecutionFrame frame) => frame.EventArgs is Dictionary<string, object> arguments && arguments.TryGetValue(pin, out object value) ? value : GraphRuntime.Default(OutputType(pin));

    /// <summary>Gets a parameter's CLR type.</summary>
    /// <param name="pin">The parameter identifier.</param>
    /// <returns>The concrete CLR type.</returns>
    public override Type OutputType(string pin) => TypeResolver.ForPin(_node?.Graph.Parameters.FirstOrDefault(parameter => parameter.Id == pin)?.Type);
}
