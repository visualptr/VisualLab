using System;
using System.Reflection;
using PlayerStatsSystem;
using VisualLab.Bindings;
using VisualLab.Logging;
using VisualLab.Runtime;

namespace VisualLab.Runtime.Nodes;

/// <summary>Subscribes to a LabAPI event; outputs are the event args' properties (plus virtual Damage).</summary>
public sealed class EventNode : NodeImplementation
{
    private readonly Binding _binding;
    private Delegate _handler;

    /// <summary>Initializes a new instance of the <see cref="EventNode"/> class.</summary>
    /// <param name="binding">The resolved LabAPI binding.</param>
    public EventNode(Binding binding)
    {
        _binding = binding;
    }

    /// <summary>Gets a value indicating whether the node's inputs change its event after the chain runs.</summary>
    public override bool WritesOutcomes => true;

    /// <summary>Subscribes the node to its LabAPI event.</summary>
    /// <param name="runtime">The graph runtime.</param>
    /// <param name="node">The runtime node.</param>
    public override void Start(GraphRuntime runtime, RuntimeNode node)
    {
        Action<object> fire = args => runtime.Fire(node, args);
        object sink = _binding.ArgsType != null
            ? Activator.CreateInstance(typeof(Sink<>).MakeGenericType(_binding.ArgsType), fire)
            : new Sink(fire);

        _handler = Delegate.CreateDelegate(_binding.Event.EventHandlerType, sink, nameof(Sink.Handle));
        _binding.Event.AddEventHandler(null, _handler);
    }

    /// <summary>Unsubscribes the node from its LabAPI event.</summary>
    /// <param name="runtime">The graph runtime.</param>
    /// <param name="node">The runtime node.</param>
    public override void Stop(GraphRuntime runtime, RuntimeNode node)
    {
        if (_handler != null)
            _binding.Event.RemoveEventHandler(null, _handler);

        _handler = null;
    }

    /// <summary>Reads a value from the current event.</summary>
    /// <param name="runtime">The graph runtime.</param>
    /// <param name="node">The runtime node.</param>
    /// <param name="pin">The pin ID.</param>
    /// <param name="frame">The current execution frame.</param>
    /// <returns>The event output value.</returns>
    public override object Eval(GraphRuntime runtime, RuntimeNode node, string pin, ExecutionFrame frame)
    {
        object args = frame.EventArgs;
        if (args == null)
            return null;

        PropertyInfo property = args.GetType().GetProperty(pin);
        if (property != null)
            return property.GetValue(args);

        if (pin == "Damage")
            return (args.GetType().GetProperty("DamageHandler")?.GetValue(args) as StandardDamageHandler)?.Damage ?? 0f;

        runtime.LogOnce("out:" + pin, LogLevel.Warn, $"this event has no {pin}", node.Id);
        return null;
    }

    /// <summary>Gets the CLR type of an event output.</summary>
    /// <param name="pin">The pin ID.</param>
    /// <returns>The output type.</returns>
    public override Type OutputType(string pin) => pin == "Damage" ? typeof(float) : _binding.ArgsType?.GetProperty(pin)?.PropertyType ?? typeof(object);

    private sealed class Sink<T>
    {
        private readonly Action<object> _fire;

        /// <summary>Initializes a new instance of the <see cref="Sink{T}"/> class.</summary>
        /// <param name="fire">The event callback.</param>
        public Sink(Action<object> fire)
        {
            _fire = fire;
        }

        /// <summary>Passes the event to the graph callback.</summary>
        /// <param name="args">The event arguments, if any.</param>
        public void Handle(T args) => _fire(args);
    }

    private sealed class Sink
    {
        private readonly Action<object> _fire;

        /// <summary>Initializes a new instance of the <see cref="Sink"/> class.</summary>
        /// <param name="fire">The event callback.</param>
        public Sink(Action<object> fire)
        {
            _fire = fire;
        }

        /// <summary>Passes the event to the graph callback.</summary>
        public void Handle() => _fire(null);
    }
}
