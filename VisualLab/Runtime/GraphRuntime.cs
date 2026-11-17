using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using LabApi.Events.Arguments.Interfaces;
using MEC;
using PlayerStatsSystem;
using VisualLab.Bindings;
using VisualLab.Graphs;
using VisualLab.Logging;
using VisualLab.Runtime.Nodes;
using VisualLab.Serialization;
using VisualLab.Storage;

namespace VisualLab.Runtime;

/// <summary>Runs a graph and handles its events.</summary>
public sealed class GraphRuntime
{
    private const int MaxSteps = 100000;
    private const int MaxDepth = 256;
    private const int MaxFaults = 200;
    private const double MaxBusyMilliseconds = 5000;
    private const double WindowMilliseconds = 10000;
    private const double TraceGapMilliseconds = 200;

    private readonly HashSet<CoroutineHandle> _coroutines = new ();
    private readonly HashSet<string> _warnedOnce = new (StringComparer.Ordinal);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _windowStart;
    private double _windowBusy;
    private int _windowFaults;
    private double _lastTrace = double.MinValue;
    private int _nesting;

    /// <summary>Initializes a new instance of the <see cref="GraphRuntime"/> class.</summary>
    /// <param name="pluginId">The graph plugin ID.</param>
    /// <param name="graph">The graph to run.</param>
    public GraphRuntime(string pluginId, GraphFile graph)
    {
        PluginId = pluginId;
        Graph = graph;
        Compile();
    }

    /// <summary>Gets the graph plugin ID.</summary>
    public string PluginId { get; }

    /// <summary>Gets the graph data.</summary>
    public GraphFile Graph { get; }

    /// <summary>Gets the compiled runtime nodes.</summary>
    public Dictionary<string, RuntimeNode> Nodes { get; } = new (StringComparer.Ordinal);

    /// <summary>Gets the current variable values.</summary>
    public Dictionary<string, object> Variables { get; } = new (StringComparer.Ordinal);

    /// <summary>Gets the graph errors.</summary>
    public List<string> Errors { get; } = new ();

    /// <summary>Gets the graph warnings.</summary>
    public List<string> Warnings { get; } = new ();

    /// <summary>Gets a value indicating whether the graph is running.</summary>
    public bool Running { get; private set; }

    /// <summary>Gets the graph name.</summary>
    public string Name => Graph.Name;

    /// <summary>Gets or sets the values this graph keeps on disk.</summary>
    public SavedData Data { get; set; }

    /// <summary>Gets or sets a value indicating whether chains report what they ran.</summary>
    public bool Tracing { get; set; }

    /// <summary>Gets or sets the callback that receives dashboard pushes.</summary>
    public Action<JsonObject> Pushed { get; set; }

    /// <summary>Gets or sets the callback that runs when the graph pauses itself.</summary>
    public Action<string> Paused { get; set; }

    /// <summary>Gets the number of chains started.</summary>
    public long Runs { get; private set; }

    /// <summary>Gets the number of runtime errors.</summary>
    public long Faults { get; private set; }

    /// <summary>Gets the total time spent running chains in milliseconds.</summary>
    public double TotalMilliseconds { get; private set; }

    /// <summary>Gets the longest chain time in milliseconds.</summary>
    public double MaxMilliseconds { get; private set; }

    /// <summary>Gets the default value for a CLR type.</summary>
    /// <param name="type">The requested type.</param>
    /// <returns>The default value, or null.</returns>
    public static object Default(Type type) => type != null && type.IsValueType && type != typeof(void) ? Activator.CreateInstance(type) : null;

    /// <summary>Removes reflection wrappers from an exception.</summary>
    /// <param name="exception">The exception to unwrap.</param>
    /// <returns>The underlying exception.</returns>
    public static Exception Unwrap(Exception exception)
    {
        while (exception is TargetInvocationException && exception.InnerException != null)
            exception = exception.InnerException;

        return exception;
    }

    /// <summary>Starts the graph if it has no compile errors.</summary>
    public void Start()
    {
        if (Running || Errors.Count > 0)
            return;

        Running = true;
        _windowStart = _clock.Elapsed.TotalMilliseconds;
        _windowBusy = 0;
        _windowFaults = 0;
        LoadSavedVariables();
        foreach (RuntimeNode node in Nodes.Values)
        {
            try
            {
                node.Implementation.Start(this, node);
            }
            catch (Exception e)
            {
                string message = "couldn't start: " + Unwrap(e).Message;
                Errors.Add(message);
                Log(LogLevel.Error, message, node.Id);
            }
        }
    }

    /// <summary>Stops graph nodes and pending coroutines.</summary>
    public void Stop()
    {
        if (!Running)
            return;

        Running = false;
        foreach (RuntimeNode node in Nodes.Values)
        {
            try
            {
                node.Implementation.Stop(this, node);
            }
            catch (Exception e)
            {
                Log(LogLevel.Warn, "couldn't stop cleanly: " + Unwrap(e).Message, node.Id);
            }
        }

        foreach (CoroutineHandle handle in _coroutines)
            Timing.KillCoroutines(handle);

        _coroutines.Clear();
    }

    /// <summary>Tracks a coroutine so it stops with the graph.</summary>
    /// <param name="handle">The coroutine handle.</param>
    public void Track(CoroutineHandle handle) => _coroutines.Add(handle);

    /// <summary>Stops tracking a coroutine.</summary>
    /// <param name="handle">The coroutine handle.</param>
    public void Untrack(CoroutineHandle handle) => _coroutines.Remove(handle);

    /// <summary>Runs an event chain and applies its outcome inputs.</summary>
    /// <param name="eventNode">The event node.</param>
    /// <param name="args">The event arguments, if any.</param>
    public void Fire(RuntimeNode eventNode, object args)
    {
        if (!Running)
            return;

        ExecutionFrame frame = new () { EventNode = eventNode, EventArgs = args };
        Measure(eventNode, frame, () =>
        {
            Follow(eventNode, "then", frame);
            if (args != null && eventNode.Implementation.WritesOutcomes)
                WriteOutcomes(eventNode, frame);
        });
    }

    /// <summary>Runs a local function with a separate parameter scope and shared execution budget.</summary>
    /// <param name="call">The call node.</param>
    /// <param name="parent">The calling frame.</param>
    public void CallFunction(RuntimeNode call, ExecutionFrame parent)
    {
        if (!Nodes.TryGetValue(call.Graph.FunctionId, out RuntimeNode entry) || entry.Graph.Type != "event.function")
            throw new InvalidOperationException("The local function does not exist.");

        if (parent.FunctionDepth >= 32)
            throw new InvalidOperationException("Function recursion limit exceeded.");

        Dictionary<string, object> arguments = new (StringComparer.Ordinal);
        foreach (GraphParameter parameter in entry.Graph.Parameters)
            arguments[parameter.Id] = Values.Coerce(Raw(call, parameter.Id, parent), TypeResolver.ForPin(parameter.Type));

        long faults = Faults;
        ExecutionFrame child = new () { EventNode = entry, EventArgs = arguments, Depth = parent.Depth, FunctionDepth = parent.FunctionDepth + 1, Steps = parent.Steps, Trace = parent.Trace };
        child.Trace?.Ran(entry.Id);
        Follow(entry, "then", child);
        parent.Steps = child.Steps;
        if (parent.Steps >= MaxSteps || Faults != faults)
            parent.Stopped = true;
    }

    /// <summary>Continues a chain that was waiting.</summary>
    /// <param name="node">The node that waited.</param>
    /// <param name="pin">The exec output to follow.</param>
    /// <param name="frame">The saved execution frame.</param>
    public void Resume(RuntimeNode node, string pin, ExecutionFrame frame)
    {
        if (!Running)
            return;

        Measure(frame.EventNode ?? node, frame, () => Follow(node, pin, frame));
    }

    /// <summary>Saves a persistent variable after it changed.</summary>
    /// <param name="variable">The variable definition.</param>
    /// <param name="value">The new value.</param>
    public void VariableChanged(GraphVariable variable, object value)
    {
        if (variable.Persistent && Data != null)
            Data.SetVariable(variable.Id, Values.ToLiteral(value));
    }

    /// <summary>Counts a runtime error and pauses the graph when it keeps failing.</summary>
    public void Fault()
    {
        Faults++;
        _windowFaults++;
    }

    /// <summary>Runs the node connected to an execution output.</summary>
    /// <param name="node">The runtime node.</param>
    /// <param name="pin">The pin ID.</param>
    /// <param name="frame">The current execution frame.</param>
    public void Follow(RuntimeNode node, string pin, ExecutionFrame frame)
    {
        if (frame.Stopped || !Running)
            return;

        if (node.Successors.TryGetValue(pin, out RuntimeNode next))
            Run(next, frame);
    }

    /// <summary>Reads an input without converting its value.</summary>
    /// <param name="node">The runtime node.</param>
    /// <param name="pin">The pin ID.</param>
    /// <param name="frame">The current execution frame.</param>
    /// <returns>The wired value, literal, or Unset.Value.</returns>
    public object Raw(RuntimeNode node, string pin, ExecutionFrame frame)
    {
        if (node.Inputs.TryGetValue(pin, out NodeOutput source))
            return Output(source.Node, source.Pin, frame);

        return node.Graph.Values.TryGetValue(pin, out object literal) ? literal : Unset.Value;
    }

    /// <summary>Checks whether an input has a wire or literal.</summary>
    /// <param name="node">The runtime node.</param>
    /// <param name="pin">The pin ID.</param>
    /// <returns>Whether the input has a value source.</returns>
    public bool Has(RuntimeNode node, string pin) => node.Inputs.ContainsKey(pin) || node.Graph.Values.ContainsKey(pin);

    /// <summary>Reads a node output in the current chain.</summary>
    /// <param name="source">The source node.</param>
    /// <param name="pin">The pin ID.</param>
    /// <param name="frame">The current execution frame.</param>
    /// <returns>The output value.</returns>
    public object Output(RuntimeNode source, string pin, ExecutionFrame frame)
    {
        object value = Resolve(source, pin, frame);
        frame.Trace?.Read(source.Id, pin, value);
        return value;
    }

    /// <summary>Stores an output value for the current chain.</summary>
    /// <param name="node">The runtime node.</param>
    /// <param name="pin">The pin ID.</param>
    /// <param name="frame">The current execution frame.</param>
    /// <param name="value">The value to use.</param>
    public void SetOutput(RuntimeNode node, string pin, ExecutionFrame frame, object value) => frame.Outputs[ExecutionFrame.Key(node, pin)] = value;

    /// <summary>Reads an input and converts it to the requested type.</summary>
    /// <param name="node">The runtime node.</param>
    /// <param name="pin">The pin ID.</param>
    /// <param name="frame">The current execution frame.</param>
    /// <param name="type">The requested type.</param>
    /// <returns>The converted input value.</returns>
    public object In(RuntimeNode node, string pin, ExecutionFrame frame, Type type) => Values.Coerce(Raw(node, pin, frame), type);

    /// <summary>Reads an input and converts it to the requested type.</summary>
    /// <typeparam name="T">The requested value type.</typeparam>
    /// <param name="node">The runtime node.</param>
    /// <param name="pin">The pin ID.</param>
    /// <param name="frame">The current execution frame.</param>
    /// <returns>The converted input value.</returns>
    public T In<T>(RuntimeNode node, string pin, ExecutionFrame frame) => (T)Values.Coerce(Raw(node, pin, frame), typeof(T));

    /// <summary>Reads an input as a float.</summary>
    /// <param name="node">The runtime node.</param>
    /// <param name="pin">The pin ID.</param>
    /// <param name="frame">The current execution frame.</param>
    /// <returns>The numeric input value.</returns>
    public float Num(RuntimeNode node, string pin, ExecutionFrame frame) => In<float>(node, pin, frame);

    /// <summary>Logs a message for this graph.</summary>
    /// <param name="level">The message severity.</param>
    /// <param name="message">The message text.</param>
    /// <param name="nodeId">The related node ID, if any.</param>
    public void Log(LogLevel level, string message, string nodeId = null) => RuntimeLog.Write(PluginId, Name, level, message, nodeId);

    /// <summary>Logs a message once for the given node and key.</summary>
    /// <param name="key">The message deduplication key.</param>
    /// <param name="level">The message severity.</param>
    /// <param name="message">The message text.</param>
    /// <param name="nodeId">The related node ID, if any.</param>
    public void LogOnce(string key, LogLevel level, string message, string nodeId)
    {
        if (_warnedOnce.Add(nodeId + "\n" + key))
            Log(level, message, nodeId);
    }

    private object Resolve(RuntimeNode source, string pin, ExecutionFrame frame)
    {
        if (source.Implementation.Pure)
        {
            string key = ExecutionFrame.Key(source, pin);
            try
            {
                if (!frame.Evaluating.Add(key))
                    throw new InvalidOperationException("Cyclic data dependency.");

                if (frame.Evaluating.Count > MaxDepth)
                    throw new InvalidOperationException("Data dependency is too deep.");

                return source.Implementation.Eval(this, source, pin, frame);
            }
            catch (Exception e)
            {
                Fault();
                Log(LogLevel.Error, Unwrap(e).Message, source.Id);
                return Default(source.Implementation.OutputType(pin));
            }
            finally
            {
                frame.Evaluating.Remove(key);
            }
        }

        if (frame.Outputs.TryGetValue(ExecutionFrame.Key(source, pin), out object value))
            return value;

        if (frame.EventNode == source)
            return source.Implementation.Eval(this, source, pin, frame);

        return Default(source.Implementation.OutputType(pin));
    }

    private void LoadSavedVariables()
    {
        if (Data == null)
            return;

        foreach (GraphVariable variable in Graph.Variables)
        {
            if (variable.Id != null && variable.Persistent && Data.TryGetVariable(variable.Id, out object saved))
                Variables[variable.Id] = Values.Literal(saved, variable.Type);
        }
    }

    private void Measure(RuntimeNode eventNode, ExecutionFrame frame, Action run)
    {
        double started = _clock.Elapsed.TotalMilliseconds;
        bool outermost = _nesting == 0;
        if (Tracing && frame.Trace == null && started - _lastTrace >= TraceGapMilliseconds)
        {
            frame.Trace = new TraceRecord();
            frame.Trace.Ran(eventNode.Id);
            _lastTrace = started;
        }

        _nesting++;
        try
        {
            run();
        }
        finally
        {
            _nesting--;
            double elapsed = _clock.Elapsed.TotalMilliseconds - started;
            Runs++;
            if (outermost)
            {
                TotalMilliseconds += elapsed;
                _windowBusy += elapsed;
            }

            if (elapsed > MaxMilliseconds)
                MaxMilliseconds = elapsed;

            if (frame.Trace != null)
            {
                Pushed?.Invoke(frame.Trace.ToPush(PluginId, eventNode.Id, Math.Round(elapsed, 3), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
                frame.Trace = null;
            }

            if (outermost)
                CheckBreaker();
        }
    }

    private void CheckBreaker()
    {
        double now = _clock.Elapsed.TotalMilliseconds;
        string reason = _windowFaults > MaxFaults
            ? $"paused after {_windowFaults} errors in a few seconds; fix the errors and save to start it again"
            : _windowBusy > MaxBusyMilliseconds
                ? $"paused because it used {Math.Round(_windowBusy)} ms of server time in a few seconds; make it lighter and save to start it again"
                : null;

        if (reason != null && Running)
        {
            Log(LogLevel.Error, reason);
            Stop();
            Paused?.Invoke(reason);
            return;
        }

        if (now - _windowStart > WindowMilliseconds)
        {
            _windowStart = now;
            _windowBusy = 0;
            _windowFaults = 0;
        }
    }

    private void Compile()
    {
        foreach (GraphVariable variable in Graph.Variables)
        {
            if (variable.Id != null)
                Variables[variable.Id] = Values.Literal(variable.Default, variable.Type);
        }

        List<string> skipped = new ();
        foreach (GraphNode graphNode in Graph.Nodes)
        {
            NodeImplementation impl = Builtins.Create(this, graphNode, out string why);
            if (impl == null)
            {
                skipped.Add($"{graphNode.Id} ({why})");
                Log(LogLevel.Error, $"node skipped: {why}", graphNode.Id);
                continue;
            }

            Nodes[graphNode.Id] = new RuntimeNode { Graph = graphNode, Implementation = impl };
        }

        if (skipped.Count > 0)
            Warnings.Add($"{skipped.Count} node(s) can't run on this server: {string.Join("; ", skipped.Take(3))}{(skipped.Count > 3 ? "…" : string.Empty)}");

        HashSet<string> allIds = new (Graph.Nodes.Select(node => node.Id));
        foreach (GraphEdge edge in Graph.Edges)
        {
            if (!allIds.Contains(edge.FromNode) || !allIds.Contains(edge.ToNode))
            {
                Warnings.Add($"an edge points at a node that doesn't exist ({edge.FromNode} → {edge.ToNode})");
                continue;
            }

            if (!Nodes.TryGetValue(edge.FromNode, out RuntimeNode from) || !Nodes.TryGetValue(edge.ToNode, out RuntimeNode to))
                continue;

            if (edge.ToPin == "exec")
            {
                if (from.Successors.ContainsKey(edge.FromPin))
                    Warnings.Add($"exec output {edge.FromNode}.{edge.FromPin} has more than one wire; only the first is used");
                else
                    from.Successors[edge.FromPin] = to;
            }
            else
            {
                if (to.Inputs.ContainsKey(edge.ToPin))
                    Warnings.Add($"input {edge.ToNode}.{edge.ToPin} has more than one wire; only the first is used");
                else
                    to.Inputs[edge.ToPin] = new NodeOutput(from, edge.FromPin);
            }
        }

        string cycle = FindExecCycle();
        if (cycle != null)
            Errors.Add($"exec wires loop back on themselves at node {cycle}; use For Each or Repeat for loops");
    }

    private string FindExecCycle()
    {
        Dictionary<RuntimeNode, int> state = new ();

        string Visit(RuntimeNode node, int depth = 0)
        {
            if (depth > MaxDepth)
            {
                Errors.Add("Exec chain is too deep; split it with a delay.");
                return node.Id;
            }

            state[node] = 1;
            foreach (RuntimeNode next in node.Successors.Values)
            {
                state.TryGetValue(next, out int current);
                if (current == 1)
                    return next.Id;

                if (current == 0)
                {
                    string found = Visit(next, depth + 1);
                    if (found != null)
                        return found;
                }
            }

            state[node] = 2;
            return null;
        }

        foreach (RuntimeNode node in Nodes.Values)
        {
            if (state.ContainsKey(node))
                continue;

            string found = Visit(node);
            if (found != null)
                return found;
        }

        return null;
    }

    private void Run(RuntimeNode node, ExecutionFrame frame)
    {
        if (frame.Stopped || !Running)
            return;

        if (++frame.Steps > MaxSteps)
        {
            frame.Stopped = true;
            Log(LogLevel.Error, $"stopped after {MaxSteps} steps; is a loop running forever?", node.Id);
            return;
        }

        frame.Trace?.Ran(node.Id);
        try
        {
            if (++frame.Depth > MaxDepth)
                throw new InvalidOperationException("Exec chain is too deep; split it with a delay.");

            node.Implementation.Run(this, node, frame);
        }
        catch (Exception e)
        {
            Fault();
            if (!frame.Stopped)
                Log(LogLevel.Error, Unwrap(e).Message, node.Id);

            frame.Stopped = true;
        }
        finally
        {
            frame.Depth--;
        }
    }

    private void WriteOutcomes(RuntimeNode node, ExecutionFrame frame)
    {
        List<string> pins = node.Inputs.Keys.Concat(node.Graph.Values.Keys).Distinct().ToList();
        foreach (string pin in pins)
        {
            try
            {
                object value = Raw(node, pin, frame);
                if (value is Unset)
                    continue;

                switch (pin)
                {
                    case "IsAllowed":
                        if (frame.EventArgs is ICancellableEvent cancellable)
                            cancellable.IsAllowed = (bool)Values.Coerce(value, typeof(bool));

                        break;
                    case "Damage":
                        if (frame.EventArgs.GetType().GetProperty("DamageHandler")?.GetValue(frame.EventArgs) is StandardDamageHandler handler)
                            handler.Damage = (float)Values.Coerce(value, typeof(float));

                        break;
                    default:
                        PropertyInfo property = frame.EventArgs.GetType().GetProperty(pin);
                        if (property == null || property.GetSetMethod() == null)
                        {
                            LogOnce($"outcome:{pin}", LogLevel.Warn, $"this event has no settable {pin}", node.Id);
                            break;
                        }

                        property.SetValue(frame.EventArgs, Values.Coerce(value, property.PropertyType));
                        break;
                }
            }
            catch (Exception e)
            {
                Fault();
                Log(LogLevel.Error, $"couldn't set {pin}: {Unwrap(e).Message}", node.Id);
            }
        }
    }
}
