using System;
using VisualLab.Bindings;
using VisualLab.Logging;
using VisualLab.Runtime;

namespace VisualLab.Runtime.Nodes;

/// <summary>Property/field get (pure, output `value`) or set (action, input `value`).</summary>
public sealed class PropertyNode : NodeImplementation
{
    private readonly Binding _binding;

    /// <summary>Initializes a new instance of the <see cref="PropertyNode"/> class.</summary>
    /// <param name="binding">The resolved LabAPI binding.</param>
    public PropertyNode(Binding binding)
    {
        _binding = binding;
    }

    /// <summary>Gets a value indicating whether the node is evaluated without an execution wire.</summary>
    public override bool Pure => !IsSet && _binding.Pure;

    private bool IsSet => _binding.Access == "set";

    private Type ValueType => _binding.Property?.PropertyType ?? _binding.Field.FieldType;

    private Type DeclaringType => _binding.Property?.DeclaringType ?? _binding.Field.DeclaringType;

    /// <summary>Reads the bound property or field.</summary>
    /// <param name="runtime">The graph runtime.</param>
    /// <param name="node">The runtime node.</param>
    /// <param name="pin">The pin ID.</param>
    /// <param name="frame">The current execution frame.</param>
    /// <returns>The property or field value.</returns>
    public override object Eval(GraphRuntime runtime, RuntimeNode node, string pin, ExecutionFrame frame)
    {
        if (!TryTarget(runtime, node, frame, out object target))
            return GraphRuntime.Default(ValueType);

        return Read(target);
    }

    /// <summary>Writes the bound property or field and continues the chain.</summary>
    /// <param name="runtime">The graph runtime.</param>
    /// <param name="node">The runtime node.</param>
    /// <param name="frame">The current execution frame.</param>
    public override void Run(GraphRuntime runtime, RuntimeNode node, ExecutionFrame frame)
    {
        if (TryTarget(runtime, node, frame, out object target))
        {
            if (IsSet)
            {
                object value = runtime.In(node, "value", frame, ValueType);
                if (_binding.Property != null)
                    _binding.Property.SetValue(target, value);
                else
                    _binding.Field.SetValue(target, value);
            }
            else
            {
                runtime.SetOutput(node, "value", frame, Read(target));
            }
        }

        runtime.Follow(node, "then", frame);
    }

    /// <summary>Gets the CLR type of the bound value.</summary>
    /// <param name="pin">The pin ID.</param>
    /// <returns>The value type.</returns>
    public override Type OutputType(string pin) => ValueType;

    private object Read(object target) => _binding.Property != null ? _binding.Property.GetValue(target) : _binding.Field.GetValue(target);

    private bool TryTarget(GraphRuntime runtime, RuntimeNode node, ExecutionFrame frame, out object target)
    {
        target = null;
        if (_binding.Static)
            return true;

        target = runtime.Raw(node, "target", frame);
        if (target is Unset || target == null || !Values.IsValid(target))
        {
            runtime.LogOnce("null", LogLevel.Warn, "target is empty (nothing was wired, or the event had none); skipped", node.Id);
            return false;
        }

        if (!DeclaringType.IsInstanceOfType(target))
            throw new InvalidCastException($"target is a {target.GetType().Name}, expected {DeclaringType.Name}");

        return true;
    }
}
