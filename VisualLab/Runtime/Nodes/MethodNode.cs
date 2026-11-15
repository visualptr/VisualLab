using System;
using System.Linq;
using System.Reflection;
using VisualLab.Bindings;
using VisualLab.Logging;
using VisualLab.Runtime;

namespace VisualLab.Runtime.Nodes;

/// <summary>Method call: inputs are the C# parameters, `result` the return value, out params their own outputs.</summary>
public sealed class MethodNode : NodeImplementation
{
    private readonly Binding _binding;

    /// <summary>Initializes a new instance of the <see cref="MethodNode"/> class.</summary>
    /// <param name="binding">The resolved LabAPI binding.</param>
    public MethodNode(Binding binding)
    {
        _binding = binding;
    }

    /// <summary>Gets a value indicating whether the node is evaluated without an execution wire.</summary>
    public override bool Pure => _binding.Pure;

    /// <summary>Calls the method and reads its requested output.</summary>
    /// <param name="runtime">The graph runtime.</param>
    /// <param name="node">The runtime node.</param>
    /// <param name="pin">The pin ID.</param>
    /// <param name="frame">The current execution frame.</param>
    /// <returns>The method result or output argument.</returns>
    public override object Eval(GraphRuntime runtime, RuntimeNode node, string pin, ExecutionFrame frame)
    {
        object[] args = Invoke(runtime, node, frame, out object result);
        if (args == null)
            return GraphRuntime.Default(OutputType(pin));

        return pin == "result" ? result : OutArg(args, pin);
    }

    /// <summary>Calls the method, stores its outputs, and continues the chain.</summary>
    /// <param name="runtime">The graph runtime.</param>
    /// <param name="node">The runtime node.</param>
    /// <param name="frame">The current execution frame.</param>
    public override void Run(GraphRuntime runtime, RuntimeNode node, ExecutionFrame frame)
    {
        object[] args = Invoke(runtime, node, frame, out object result);
        if (args != null)
        {
            runtime.SetOutput(node, "result", frame, result);
            for (int i = 0; i < _binding.MethodParams.Length; i++)
            {
                if (_binding.MethodParams[i].IsOut)
                    runtime.SetOutput(node, _binding.MethodParams[i].Name, frame, args[i]);
            }
        }

        runtime.Follow(node, "then", frame);
    }

    /// <summary>Gets the CLR type of a method output.</summary>
    /// <param name="pin">The pin ID.</param>
    /// <returns>The output type.</returns>
    public override Type OutputType(string pin)
    {
        if (pin == "result")
            return _binding.Method.ReturnType;

        ParameterInfo parameter = _binding.MethodParams.FirstOrDefault(p => p.Name == pin);
        return parameter?.ParameterType.IsByRef == true ? parameter.ParameterType.GetElementType() : parameter?.ParameterType ?? typeof(object);
    }

    private object[] Invoke(GraphRuntime runtime, RuntimeNode node, ExecutionFrame frame, out object result)
    {
        result = null;
        ParameterInfo[] parameters = _binding.MethodParams;
        object[] args = new object[parameters.Length];
        object target = null;

        if (!_binding.Static || _binding.Extension)
        {
            target = runtime.Raw(node, "target", frame);
            if (target is Unset || target == null || !Values.IsValid(target))
            {
                runtime.LogOnce("null", LogLevel.Warn, "target is empty (nothing was wired, or the event had none); skipped", node.Id);
                return null;
            }
        }

        for (int i = 0; i < parameters.Length; i++)
        {
            ParameterInfo parameter = parameters[i];
            if (_binding.Extension && i == 0)
            {
                args[0] = Values.Coerce(target, parameter.ParameterType);
                continue;
            }

            if (parameter.IsOut)
                continue;

            string pinId = i < _binding.Parameters.Count ? _binding.Parameters[i].Name : parameter.Name;
            object value = runtime.Raw(node, pinId, frame);
            if (value is Unset)
            {
                if (parameter.HasDefaultValue)
                {
                    args[i] = parameter.DefaultValue is DBNull ? GraphRuntime.Default(parameter.ParameterType) : parameter.DefaultValue;
                    continue;
                }

                if (parameter.GetCustomAttributes(typeof(ParamArrayAttribute), false).Length > 0)
                {
                    args[i] = Array.CreateInstance(parameter.ParameterType.GetElementType(), 0);
                    continue;
                }
            }

            args[i] = Values.Coerce(value, parameter.ParameterType);
        }

        if (!_binding.Static && !_binding.Method.DeclaringType.IsInstanceOfType(target))
            throw new InvalidCastException($"target is a {target.GetType().Name}, expected {_binding.Method.DeclaringType.Name}");

        result = _binding.Method.Invoke(_binding.Static ? null : target, args);
        return args;
    }

    private object OutArg(object[] args, string pin)
    {
        for (int i = 0; i < _binding.MethodParams.Length; i++)
        {
            if (_binding.MethodParams[i].Name == pin)
                return args[i];
        }

        return null;
    }
}
