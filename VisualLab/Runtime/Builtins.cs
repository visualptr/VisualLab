using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using LabApi.Features.Wrappers;
using MapGeneration;
using MEC;
using PlayerRoles;
using PlayerStatsSystem;
using UnityEngine;
using VisualLab.Bindings;
using VisualLab.Graphs;
using VisualLab.Runtime.Nodes;
using Random = System.Random;

namespace VisualLab.Runtime;

/// <summary>Creates implementations for built-in nodes.</summary>
public static class Builtins
{
    private const int MaxRepeat = 10000;

    private static readonly Random Rng = new ();

    private static readonly Dictionary<string, Func<NodeImplementation>> Table = new (StringComparer.Ordinal)
    {
        ["flow.branch"] = () => new ExecNode((rt, n, f) => rt.Follow(n, rt.In<bool>(n, "condition", f) ? "true" : "false", f)),
        ["flow.chance"] = () => new ExecNode((rt, n, f) => rt.Follow(n, Rng.NextDouble() * 100 < rt.Num(n, "percent", f) ? "success" : "fail", f)),
        ["flow.sequence"] = () => new ExecNode((rt, n, f) =>
        {
            rt.Follow(n, "then0", f);
            rt.Follow(n, "then1", f);
            rt.Follow(n, "then2", f);
        }),
        ["flow.delay"] = () => new ExecNode(Delay),
        ["flow.repeat"] = () => new ExecNode(Repeat),
        ["flow.while"] = () => new ExecNode(While),
        ["flow.stop"] = () => new ExecNode((rt, n, f) => f.Stopped = true),

        ["math.add"] = () => Math2((a, b) => a + b),
        ["math.subtract"] = () => Math2((a, b) => a - b),
        ["math.multiply"] = () => Math2((a, b) => a * b),
        ["math.divide"] = () => Math2((a, b) => b == 0 ? 0 : a / b),
        ["math.modulo"] = () => Math2((a, b) => b == 0 ? 0 : a % b),
        ["math.min"] = () => Math2(Math.Min),
        ["math.max"] = () => Math2(Math.Max),
        ["math.abs"] = () => Pure((rt, n, f) => Math.Abs(rt.Num(n, "value", f))),
        ["math.greater"] = () => Compare((a, b) => a > b),
        ["math.less"] = () => Compare((a, b) => a < b),
        ["math.greaterOrEqual"] = () => Compare((a, b) => a >= b),
        ["math.lessOrEqual"] = () => Compare((a, b) => a <= b),
        ["math.clamp"] = () => Pure((rt, n, f) => Mathf.Clamp(rt.Num(n, "value", f), rt.Num(n, "min", f), rt.Num(n, "max", f))),
        ["math.randomNumber"] = () => Pure((rt, n, f) =>
        {
            float min = rt.Num(n, "min", f);
            return min + ((float)Rng.NextDouble() * (rt.Num(n, "max", f) - min));
        }),
        ["math.randomInteger"] = () => Pure((rt, n, f) =>
        {
            int min = rt.In<int>(n, "min", f);
            int max = rt.In<int>(n, "max", f);
            return max <= min ? min : Rng.Next(min, max);
        }),
        ["math.round"] = () => Pure((rt, n, f) => (int)Math.Round(rt.Num(n, "value", f), MidpointRounding.AwayFromZero)),
        ["math.distance"] = () => Pure((rt, n, f) => Vector3.Distance(rt.In<Vector3>(n, "a", f), rt.In<Vector3>(n, "b", f))),
        ["math.makeVector"] = () => Pure((rt, n, f) => new Vector3(rt.Num(n, "x", f), rt.Num(n, "y", f), rt.Num(n, "z", f))),
        ["math.breakVector"] = () => new PureNode(
            (rt, n, pin, f) =>
            {
                Vector3 vector = rt.In<Vector3>(n, "value", f);
                return pin == "x" ? vector.x : pin == "y" ? vector.y : vector.z;
            },
            typeof(float)),

        ["logic.and"] = () => Bool2((a, b) => a && b),
        ["logic.or"] = () => Bool2((a, b) => a || b),
        ["logic.not"] = () => Pure((rt, n, f) => !rt.In<bool>(n, "a", f)),
        ["logic.equals"] = () => Pure((rt, n, f) => Values.Same(rt.Raw(n, "a", f), rt.Raw(n, "b", f))),
        ["logic.notEquals"] = () => Pure((rt, n, f) => !Values.Same(rt.Raw(n, "a", f), rt.Raw(n, "b", f))),
        ["logic.isValid"] = () => Pure((rt, n, f) => Values.IsValid(rt.Raw(n, "value", f))),
        ["logic.roleIs"] = () => Is<RoleTypeId>(),
        ["logic.teamIs"] = () => Is<Team>(),
        ["logic.itemIs"] = () => Is<ItemType>(),

        ["text.join"] = () => Pure((rt, n, f) => Text(rt, n, "a", f) + Text(rt, n, "b", f) + Text(rt, n, "c", f)),
        ["text.format"] = () => Pure((rt, n, f) => Text(rt, n, "template", f)
            .Replace("{0}", Text(rt, n, "a", f))
            .Replace("{1}", Text(rt, n, "b", f))
            .Replace("{2}", Text(rt, n, "c", f))),
        ["text.contains"] = () => Pure((rt, n, f) => Text(rt, n, "text", f).IndexOf(Text(rt, n, "search", f), StringComparison.OrdinalIgnoreCase) >= 0),
        ["text.toUpper"] = () => Pure((rt, n, f) => Text(rt, n, "text", f).ToUpperInvariant()),
        ["text.toLower"] = () => Pure((rt, n, f) => Text(rt, n, "text", f).ToLowerInvariant()),
        ["text.length"] = () => Pure((rt, n, f) => Text(rt, n, "text", f).Length),
        ["text.color"] = () => Pure((rt, n, f) =>
            $"<color=#{ColorUtility.ToHtmlStringRGBA(rt.In<Color>(n, "color", f)).ToLowerInvariant()}>{Text(rt, n, "text", f)}</color>"),

        ["value.string"] = () => Literal(typeof(string)),
        ["value.float"] = () => Literal(typeof(float)),
        ["value.int"] = () => Literal(typeof(int)),
        ["value.bool"] = () => Literal(typeof(bool)),
        ["value.vector3"] = () => Literal(typeof(Vector3)),
        ["value.color"] = () => Literal(typeof(Color)),
        ["value.RoleTypeId"] = () => Literal(typeof(RoleTypeId)),
        ["value.Team"] = () => Literal(typeof(Team)),
        ["value.ItemType"] = () => Literal(typeof(ItemType)),
        ["value.FacilityZone"] = () => Literal(typeof(FacilityZone)),

        ["convert.toText"] = () => Pure((rt, n, f) => Values.ToText(rt.Raw(n, "value", f))),
        ["convert.intToFloat"] = () => Pure((rt, n, f) => (float)rt.In<int>(n, "value", f)),
        ["convert.floatToInt"] = () => Pure((rt, n, f) => (int)Math.Truncate(rt.Num(n, "value", f))),

        ["event.interval"] = () => new IntervalNode(),
        ["helper.alivePlayers"] = () => Pure((rt, n, f) => Ready.Where(player => player.IsAlive).ToList()),
        ["helper.playersWithRole"] = () => Pure((rt, n, f) =>
        {
            RoleTypeId role = rt.In<RoleTypeId>(n, "role", f);
            return Ready.Where(player => player.Role == role).ToList();
        }),
        ["helper.playersInTeam"] = () => Pure((rt, n, f) =>
        {
            Team team = rt.In<Team>(n, "team", f);
            return Ready.Where(player => player.Team == team).ToList();
        }),
        ["helper.damageAmount"] = () => Pure((rt, n, f) => rt.Raw(n, "handler", f) is StandardDamageHandler handler ? handler.Damage : 0f),

        ["list.forEach"] = () => new ExecNode(ForEach),
        ["list.count"] = () => Pure((rt, n, f) => List(rt, n, f).Count()),
        ["list.random"] = () => Pure((rt, n, f) =>
        {
            List<object> list = List(rt, n, f).ToList();
            return list.Count == 0 ? null : list[Rng.Next(list.Count)];
        }),
        ["list.first"] = () => Pure((rt, n, f) => List(rt, n, f).FirstOrDefault()),
        ["list.get"] = () => Pure((rt, n, f) =>
        {
            int index = rt.In<int>(n, "index", f);
            return index < 0 ? null : List(rt, n, f).ElementAtOrDefault(index);
        }),
        ["list.isEmpty"] = () => Pure((rt, n, f) => !List(rt, n, f).Any()),
        ["list.contains"] = () => Pure((rt, n, f) =>
        {
            object value = rt.Raw(n, "value", f);
            return List(rt, n, f).Any(item => Values.Same(item, value));
        }),
    };

    private static IEnumerable<Player> Ready => Player.ReadyList;

    /// <summary>Checks whether a node type is built in.</summary>
    /// <param name="type">The requested type.</param>
    /// <returns>Whether the node type is built in.</returns>
    public static bool IsBuiltin(string type) => Table.ContainsKey(type) || type == "cast" || type == "variable.get" || type == "variable.set";

    /// <summary>Creates the implementation for a graph node.</summary>
    /// <param name="runtime">The graph runtime.</param>
    /// <param name="graphNode">The graph node.</param>
    /// <param name="why">The reason when the node is unsupported.</param>
    /// <returns>The implementation, or null if unsupported.</returns>
    public static NodeImplementation Create(GraphRuntime runtime, GraphNode graphNode, out string why)
    {
        why = null;
        if (Table.TryGetValue(graphNode.Type, out Func<NodeImplementation> make))
            return make();

        switch (graphNode.Type)
        {
            case "cast":
                return Cast(graphNode, out why);
            case "variable.get":
            case "variable.set":
                return Variable(runtime, graphNode, out why);
        }

        Binding binding = Catalog.Get(graphNode.Type);
        if (binding == null)
        {
            why = $"unknown node type \"{graphNode.Type}\" (made with a newer editor?)";
            return null;
        }

        if (binding.Error != null)
        {
            why = binding.Error;
            return null;
        }

        switch (binding.Kind)
        {
            case "event":
                return new EventNode(binding);
            case "property":
                return new PropertyNode(binding);
            case "method":
                return new MethodNode(binding);
        }

        why = $"unknown binding kind {binding.Kind}";
        return null;
    }

    private static PureNode Pure<T>(Func<GraphRuntime, RuntimeNode, ExecutionFrame, T> eval) => new ((rt, n, pin, f) => eval(rt, n, f), typeof(T));

    private static PureNode Math2(Func<float, float, float> op) => Pure((rt, n, f) => op(rt.Num(n, "a", f), rt.Num(n, "b", f)));

    private static PureNode Compare(Func<float, float, bool> op) => Pure((rt, n, f) => op(rt.Num(n, "a", f), rt.Num(n, "b", f)));

    private static PureNode Bool2(Func<bool, bool, bool> op) => Pure((rt, n, f) => op(rt.In<bool>(n, "a", f), rt.In<bool>(n, "b", f)));

    private static PureNode Is<T>() => Pure((rt, n, f) => Equals(rt.In<T>(n, "a", f), rt.In<T>(n, "b", f)));

    private static PureNode Literal(Type type) => new ((rt, n, pin, f) => rt.In(n, "value", f, type), type);

    private static string Text(GraphRuntime runtime, RuntimeNode node, string pin, ExecutionFrame frame) => Values.ToText(runtime.Raw(node, pin, frame));

    private static IEnumerable<object> List(GraphRuntime runtime, RuntimeNode node, ExecutionFrame frame)
    {
        object value = runtime.Raw(node, "list", frame);
        if (value is Unset || value == null)
            return Enumerable.Empty<object>();

        if (value is string || value is not IEnumerable sequence)
            throw new InvalidCastException("List isn't a list");

        return sequence.Cast<object>().ToList();
    }

    private static void Delay(GraphRuntime runtime, RuntimeNode node, ExecutionFrame frame)
    {
        float seconds = Math.Max(0f, runtime.Num(node, "seconds", frame));
        ExecutionFrame continuation = frame.Snapshot();
        CoroutineHandle handle = default;
        handle = Timing.CallDelayed(seconds, () =>
        {
            runtime.Untrack(handle);
            if (!runtime.Running)
                return;

            runtime.Follow(node, "completed", continuation);
        });

        runtime.Track(handle);
    }

    private static void Repeat(GraphRuntime runtime, RuntimeNode node, ExecutionFrame frame)
    {
        int count = runtime.In<int>(node, "count", frame);
        if (count > MaxRepeat)
            throw new InvalidOperationException("Repeat is limited to 10000 iterations per run.");

        for (int i = 0; i < count && !frame.Stopped; i++)
        {
            runtime.SetOutput(node, "index", frame, i);
            runtime.Follow(node, "body", frame);
        }

        runtime.Follow(node, "completed", frame);
    }

    private static void While(GraphRuntime runtime, RuntimeNode node, ExecutionFrame frame)
    {
        for (int i = 0; !frame.Stopped && runtime.In<bool>(node, "condition", frame); i++)
        {
            if (i >= MaxRepeat)
                throw new InvalidOperationException("While is limited to 10000 iterations per run.");

            runtime.SetOutput(node, "index", frame, i);
            runtime.Follow(node, "body", frame);
        }

        runtime.Follow(node, "completed", frame);
    }

    private static void ForEach(GraphRuntime runtime, RuntimeNode node, ExecutionFrame frame)
    {
        int index = 0;
        foreach (object item in List(runtime, node, frame))
        {
            if (frame.Stopped)
                break;

            runtime.SetOutput(node, "item", frame, item);
            runtime.SetOutput(node, "index", frame, index++);
            runtime.Follow(node, "body", frame);
        }

        runtime.Follow(node, "completed", frame);
    }

    private static NodeImplementation Cast(GraphNode graphNode, out string why)
    {
        why = null;
        Type type = TypeResolver.Find(graphNode.TypeArg);
        if (type == null)
        {
            why = graphNode.TypeArg == null ? "As Type has no type chosen" : $"unknown type {graphNode.TypeArg}";
            return null;
        }

        return new PureNode((rt, n, pin, f) =>
        {
            object value = rt.Raw(n, "value", f);
            bool ok = value != null && value is not Unset && type.IsInstanceOfType(value);
            return pin == "ok" ? ok : ok ? value : null;
        });
    }

    private static NodeImplementation Variable(GraphRuntime runtime, GraphNode graphNode, out string why)
    {
        why = null;
        GraphVariable variable = runtime.Graph.Variables.FirstOrDefault(v => v.Id == graphNode.VarId);
        if (variable == null)
        {
            why = "uses a variable that doesn't exist";
            return null;
        }

        Type type = TypeResolver.ForPin(variable.Type);
        if (graphNode.Type == "variable.get")
            return new PureNode((rt, n, pin, f) => rt.Variables[variable.Id], type);

        return new ExecNode((rt, n, f) =>
        {
            object value = rt.In(n, "value", f, type);
            rt.Variables[variable.Id] = value;
            rt.SetOutput(n, "value", f, value);
            rt.Follow(n, "then", f);
        });
    }
}
