using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CustomPlayerEffects;
using Interactables.Interobjects.DoorUtils;
using LabApi.Features.Wrappers;
using MapGeneration;
using MEC;
using PlayerRoles;
using PlayerStatsSystem;
using UnityEngine;
using VisualLab.Bindings;
using VisualLab.Graphs;
using VisualLab.Logging;
using VisualLab.Runtime.Nodes;
using VisualLab.Runtime.Payloads;
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
        ["text.startsWith"] = () => Pure((rt, n, f) => Text(rt, n, "text", f).StartsWith(Text(rt, n, "search", f), StringComparison.OrdinalIgnoreCase)),
        ["text.endsWith"] = () => Pure((rt, n, f) => Text(rt, n, "text", f).EndsWith(Text(rt, n, "search", f), StringComparison.OrdinalIgnoreCase)),
        ["text.substring"] = () => Pure((rt, n, f) =>
        {
            string text = Text(rt, n, "text", f);
            int start = Math.Min(Math.Max(0, rt.In<int>(n, "start", f)), text.Length);
            int length = rt.In<int>(n, "length", f);
            return length <= 0 || start + length > text.Length ? text.Substring(start) : text.Substring(start, length);
        }),
        ["text.replace"] = () => Pure((rt, n, f) =>
        {
            string search = Text(rt, n, "search", f);
            return search.Length == 0 ? Text(rt, n, "text", f) : Text(rt, n, "text", f).Replace(search, Text(rt, n, "replacement", f));
        }),
        ["text.trim"] = () => Pure((rt, n, f) => Text(rt, n, "text", f).Trim()),
        ["text.split"] = () => Pure((rt, n, f) =>
        {
            string separator = Text(rt, n, "separator", f);
            return Text(rt, n, "text", f).Split(new[] { separator.Length == 0 ? " " : separator }, StringSplitOptions.RemoveEmptyEntries).ToList();
        }),
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

        ["convert.textToNumber"] = () => new PureNode(
            (rt, n, pin, f) =>
            {
                bool ok = float.TryParse(Text(rt, n, "text", f).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float number);
                return pin == "ok" ? ok : ok ? number : 0f;
            }),

        ["event.interval"] = () => new IntervalNode(),
        ["event.command"] = () => new CommandNode(),
        ["event.function"] = () => new FunctionNode(),
        ["function.call"] = () => new ExecNode((rt, n, f) =>
        {
            rt.CallFunction(n, f);
            rt.Follow(n, "then", f);
        }),
        ["event.signal"] = () => new SignalNode(),
        ["event.setting.slider"] = () => new SettingNode(),
        ["event.setting.toggle"] = () => new SettingNode(),
        ["event.setting.dropdown"] = () => new SettingNode(),
        ["event.setting.text"] = () => new SettingNode(),
        ["setting.number"] = () => Pure((rt, n, f) => Keybinds.Read(rt, Text(rt, n, "name", f), rt.Raw(n, "player", f) as Player)?.Number ?? 0f),
        ["setting.enabled"] = () => Pure((rt, n, f) => Keybinds.Read(rt, Text(rt, n, "name", f), rt.Raw(n, "player", f) as Player)?.Enabled ?? false),
        ["setting.index"] = () => Pure((rt, n, f) => Keybinds.Read(rt, Text(rt, n, "name", f), rt.Raw(n, "player", f) as Player)?.Index ?? 0),
        ["setting.text"] = () => Pure((rt, n, f) => Keybinds.Read(rt, Text(rt, n, "name", f), rt.Raw(n, "player", f) as Player)?.Text ?? string.Empty),
        ["event.keybind"] = () => new KeybindNode(),
        ["command.reply"] = () => new ExecNode((rt, n, f) =>
        {
            if (f.EventArgs is CommandCall call)
                call.Reply(Text(rt, n, "message", f), rt.In<bool>(n, "success", f));
            else
                rt.LogOnce("reply", LogLevel.Warn, "reply only works in a chain started by a command", n.Id);

            rt.Follow(n, "then", f);
        }),
        ["signal.send"] = () => new ExecNode((rt, n, f) =>
        {
            string name = Text(rt, n, "name", f).Trim();
            if (name.Length > 0)
                SignalBus.Send(name, new SignalCall { Text = Text(rt, n, "text", f), Number = rt.Num(n, "number", f), Player = rt.Raw(n, "player", f) as Player });

            rt.Follow(n, "then", f);
        }),

        ["save.getNumber"] = () => Pure((rt, n, f) => Saved(rt, n, f) is double number ? (float)number : rt.Num(n, "fallback", f)),
        ["save.getText"] = () => Pure((rt, n, f) => Saved(rt, n, f) is object saved ? Values.ToText(saved) : Text(rt, n, "fallback", f)),
        ["save.setNumber"] = () => new ExecNode((rt, n, f) =>
        {
            Save(rt, n, f, (double)rt.Num(n, "value", f));
            rt.Follow(n, "then", f);
        }),
        ["save.setText"] = () => new ExecNode((rt, n, f) =>
        {
            Save(rt, n, f, Text(rt, n, "value", f));
            rt.Follow(n, "then", f);
        }),
        ["save.addNumber"] = () => new ExecNode((rt, n, f) =>
        {
            double total = (Saved(rt, n, f) is double number ? number : 0) + rt.Num(n, "amount", f);
            Save(rt, n, f, total);
            rt.SetOutput(n, "value", f, (float)total);
            rt.Follow(n, "then", f);
        }),
        ["save.delete"] = () => new ExecNode((rt, n, f) =>
        {
            Save(rt, n, f, null);
            rt.Follow(n, "then", f);
        }),

        ["logic.select"] = () => Pure((rt, n, f) =>
        {
            object picked = rt.Raw(n, rt.In<bool>(n, "condition", f) ? "a" : "b", f);
            return picked is Unset ? null : picked;
        }),
        ["logic.hasFlag"] = () => Pure((rt, n, f) =>
        {
            object value = rt.Raw(n, "value", f);
            if (value == null || value is Unset || !value.GetType().IsEnum)
                return false;

            long flag = Convert.ToInt64(Values.ToEnum(rt.Raw(n, "flag", f), value.GetType()));
            return flag != 0 && (Convert.ToInt64(value) & flag) == flag;
        }),
        ["helper.findPlayer"] = () => Pure((rt, n, f) => FindPlayer(Text(rt, n, "text", f).Trim())),
        ["helper.hasKeycardFor"] = () => Pure((rt, n, f) =>
        {
            if (rt.Raw(n, "player", f) is not Player player || rt.Raw(n, "door", f) is not Door door || !Values.IsValid(player) || door.Base == null)
                return false;

            DoorPermissionsPolicy policy = door.Base.PermissionsPolicy;
            return policy.RequiredPermissions == DoorPermissionFlags.None
                || player.Items.Any(item => item?.Base is IDoorPermissionProvider provider && policy.CheckPermissions(provider.GetPermissions(door.Base)));
        }),
        ["effect.isActive"] = () => Pure((rt, n, f) => Effect(rt, n, f)?.IsEnabled ?? false),
        ["effect.intensity"] = () => Pure((rt, n, f) => (int)(Effect(rt, n, f)?.Intensity ?? 0)),
        ["effect.timeLeft"] = () => Pure((rt, n, f) => Effect(rt, n, f) is StatusEffectBase effect && effect.IsEnabled ? effect.TimeLeft : 0f),
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
        ["list.add"] = () => Pure((rt, n, f) =>
        {
            List<object> list = List(rt, n, f).ToList();
            list.Add(rt.Raw(n, "value", f));
            return list;
        }),
        ["list.remove"] = () => Pure((rt, n, f) =>
        {
            object value = rt.Raw(n, "value", f);
            List<object> list = List(rt, n, f).ToList();
            int index = list.FindIndex(item => Values.Same(item, value));
            if (index >= 0)
                list.RemoveAt(index);

            return list;
        }),
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

    private static StatusEffectBase Effect(GraphRuntime runtime, RuntimeNode node, ExecutionFrame frame)
    {
        if (runtime.Raw(node, "player", frame) is not Player player || !Values.IsValid(player))
            return null;

        return player.TryGetEffect(Text(runtime, node, "effect", frame).Trim(), out StatusEffectBase effect) ? effect : null;
    }

    private static Player FindPlayer(string text)
    {
        if (text.Length == 0)
            return null;

        List<Player> ready = Ready.ToList();
        if (int.TryParse(text, out int id) && ready.FirstOrDefault(player => player.PlayerId == id) is Player byId)
            return byId;

        return ready.FirstOrDefault(player => string.Equals(player.UserId, text, StringComparison.OrdinalIgnoreCase))
            ?? ready.FirstOrDefault(player => string.Equals(player.Nickname, text, StringComparison.OrdinalIgnoreCase))
            ?? ready.FirstOrDefault(player => player.Nickname != null && player.Nickname.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static string Owner(GraphRuntime runtime, RuntimeNode node, ExecutionFrame frame)
    {
        object player = runtime.Raw(node, "player", frame);
        if (player is Unset || player == null)
            return null;

        if (player is not Player owner || !Values.IsValid(owner) || string.IsNullOrEmpty(owner.UserId))
            throw new InvalidOperationException("the player is empty, so their saved value can't be used");

        return owner.UserId;
    }

    private static string Key(GraphRuntime runtime, RuntimeNode node, ExecutionFrame frame)
    {
        string key = Text(runtime, node, "key", frame).Trim();
        if (key.Length == 0 || key.Length > 200)
            throw new InvalidOperationException("a saved value needs a key of 1 to 200 characters");

        return key;
    }

    private static object Saved(GraphRuntime runtime, RuntimeNode node, ExecutionFrame frame) =>
        runtime.Data?.Get(Key(runtime, node, frame), Owner(runtime, node, frame));

    private static void Save(GraphRuntime runtime, RuntimeNode node, ExecutionFrame frame, object value)
    {
        if (runtime.Data == null)
            throw new InvalidOperationException("saved values aren't available here");

        runtime.Data.Set(Key(runtime, node, frame), Owner(runtime, node, frame), value);
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

            runtime.Resume(node, "completed", continuation);
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
            rt.VariableChanged(variable, value);
            rt.SetOutput(n, "value", f, value);
            rt.Follow(n, "then", f);
        });
    }
}
