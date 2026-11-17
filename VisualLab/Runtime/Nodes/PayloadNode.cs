using System;
using System.Reflection;
using VisualLab.Runtime;

namespace VisualLab.Runtime.Nodes;

/// <summary>An event node whose outputs are the properties of a payload object.</summary>
public abstract class PayloadNode : NodeImplementation
{
    /// <summary>Gets the payload type that carries the outputs.</summary>
    protected abstract Type PayloadType { get; }

    /// <summary>Reads an output from the current payload.</summary>
    /// <param name="runtime">The graph runtime.</param>
    /// <param name="node">The runtime node.</param>
    /// <param name="pin">The pin ID.</param>
    /// <param name="frame">The current execution frame.</param>
    /// <returns>The output value.</returns>
    public override object Eval(GraphRuntime runtime, RuntimeNode node, string pin, ExecutionFrame frame)
    {
        object payload = frame.EventArgs;
        if (payload == null)
            return GraphRuntime.Default(OutputType(pin));

        PropertyInfo property = payload.GetType().GetProperty(pin);
        return property?.GetValue(payload);
    }

    /// <summary>Gets the CLR type of a payload output.</summary>
    /// <param name="pin">The pin ID.</param>
    /// <returns>The output type.</returns>
    public override Type OutputType(string pin) => PayloadType.GetProperty(pin)?.PropertyType ?? typeof(object);

    /// <summary>Reads a literal configured on the node.</summary>
    /// <typeparam name="T">The requested value type.</typeparam>
    /// <param name="node">The runtime node.</param>
    /// <param name="pin">The pin ID.</param>
    /// <param name="fallback">The value used when the literal is missing.</param>
    /// <returns>The configured value.</returns>
    protected static T Setting<T>(RuntimeNode node, string pin, T fallback)
    {
        if (!node.Graph.Values.TryGetValue(pin, out object value) || value == null)
            return fallback;

        return (T)Values.Coerce(value, typeof(T));
    }
}
