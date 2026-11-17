using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VisualLab.Bindings;
using VisualLab.Serialization;

namespace VisualLab.Graphs;

/// <summary>Checks graph wiring and pin types.</summary>
public static class GraphValidation
{
    private const int MaxNodes = 10000;
    private const int MaxEdges = 50000;
    private const int MaxPureDepth = 256;

    private static readonly Dictionary<string, JsonObject> Definitions = new ();
    private static readonly JsonObject Types;

    static GraphValidation()
    {
        using Stream stream = typeof(GraphValidation).Assembly.GetManifestResourceStream("VisualLab.bindings.json");
        using StreamReader reader = new (stream);
        JsonObject manifest = Json.ParseObject(reader.ReadToEnd());
        Types = manifest.Obj("types");
        foreach (JsonObject definition in manifest.Arr("definitions").Cast<JsonObject>())
            Definitions[definition.Str("type")] = definition;
    }

    /// <summary>Reads enum choices from the running game.</summary>
    /// <returns>The enum names and their choices.</returns>
    public static JsonObject EnumValues()
    {
        JsonObject result = new ();
        foreach (KeyValuePair<string, object> pair in Types)
        {
            if (pair.Value is not JsonObject entry || entry.Str("kind") != "enum")
                continue;

            Type type = TypeResolver.Find(pair.Key);
            if (type != null && type.IsEnum)
                result[pair.Key] = Enum.GetNames(type);
        }

        return result;
    }

    /// <summary>Checks graph wiring and types; throws if invalid.</summary>
    /// <param name="graph">The graph to run.</param>
    public static void Check(GraphFile graph)
    {
        if (graph.Nodes.Count > MaxNodes || graph.Edges.Count > MaxEdges)
            throw new FormatException("Graph is too large.");

        if (graph.Variables.Any(variable => string.IsNullOrEmpty(variable.Id) || string.IsNullOrEmpty(variable.Type)) || graph.Variables.GroupBy(variable => variable.Id).Any(group => group.Count() > 1))
            throw new FormatException("Variables need unique ids and a type.");

        Dictionary<string, GraphNode> nodes = graph.Nodes.ToDictionary(node => node.Id);
        Dictionary<string, JsonObject> definitions = graph.Nodes.ToDictionary(node => node.Id, node => Definition(node, graph));
        Dictionary<string, GraphEdge> inputs = new ();
        HashSet<string> execOutputs = new ();
        Dictionary<string, List<GraphEdge>> exec = graph.Nodes.ToDictionary(node => node.Id, _ => new List<GraphEdge>());
        Dictionary<string, List<GraphEdge>> data = graph.Nodes.ToDictionary(node => node.Id, _ => new List<GraphEdge>());

        foreach (GraphEdge edge in graph.Edges)
        {
            if (edge.FromNode == null || edge.ToNode == null || !nodes.ContainsKey(edge.FromNode) || !nodes.ContainsKey(edge.ToNode))
                throw new FormatException("A wire references a missing node.");

            JsonObject source = definitions[edge.FromNode];
            JsonObject target = definitions[edge.ToNode];

            if (source == null || target == null)
                continue;

            JsonObject sourcePin = Pins(source, "outputs").FirstOrDefault(pin => pin.Str("id") == edge.FromPin);
            JsonObject targetPin = Pins(target, "inputs").FirstOrDefault(pin => pin.Str("id") == edge.ToPin);
            if (sourcePin == null || targetPin == null)
                throw new FormatException($"{edge.ToNode}: wire references a missing pin.");

            if (!Compatible(PinType(sourcePin, nodes[edge.FromNode]), PinType(targetPin, nodes[edge.ToNode])))
                throw new FormatException($"{edge.ToNode}.{edge.ToPin}: pin types do not match.");

            if (sourcePin.Str("type") == "exec")
            {
                if (!execOutputs.Add(edge.FromNode + "\n" + edge.FromPin))
                    throw new FormatException("Exec outputs can only have one wire.");

                exec[edge.FromNode].Add(edge);
            }
            else
            {
                string key = edge.ToNode + "\n" + edge.ToPin;
                if (inputs.ContainsKey(key))
                    throw new FormatException("Data inputs can only have one wire.");

                inputs[key] = edge;
                data[edge.FromNode].Add(edge);
            }
        }

        HashSet<string> Reach(IEnumerable<string> starts)
        {
            HashSet<string> result = new ();
            Stack<string> pending = new (starts);
            while (pending.Count > 0)
            {
                string id = pending.Pop();
                if (!result.Add(id))
                    continue;

                foreach (GraphEdge edge in exec[id])
                    pending.Push(edge.ToNode);
            }

            return result;
        }

        List<GraphNode> events = graph.Nodes.Where(node => definitions[node.Id]?.Str("kind") == "event").ToList();
        Dictionary<string, HashSet<string>> reached = events.ToDictionary(node => node.Id, node => Reach(new[] { node.Id }));

        foreach (GraphNode node in graph.Nodes)
        {
            JsonObject definition = definitions[node.Id];
            if (definition == null)
                continue;

            foreach (JsonObject pin in Pins(definition, "inputs"))
            {
                if (pin.Bool("required") && pin.Str("type") != "exec" && !inputs.ContainsKey(node.Id + "\n" + pin.Str("id")))
                    throw new FormatException($"{node.Id}.{pin.Str("id")}: required input is not wired.");
            }

            if (definition.Str("kind") == "event")
                CheckOutcomeDependencies(node, definition, definitions, inputs);

            HashSet<string> consumers = new ();
            Stack<string> queue = new (data[node.Id].Select(edge => edge.ToNode));
            HashSet<string> visited = new ();
            while (queue.Count > 0)
            {
                string id = queue.Pop();
                if (!visited.Add(id))
                    continue;

                if (definitions[id]?.Str("kind") != "pure")
                {
                    consumers.Add(id);
                }
                else
                {
                    foreach (GraphEdge edge in data[id])
                        queue.Push(edge.ToNode);
                }
            }

            if (definition.Str("kind") == "event")
            {
                foreach (string consumer in consumers.Where(id => id != node.Id))
                {
                    if (reached.Any(pair => pair.Key != node.Id && pair.Value.Contains(consumer)))
                        throw new FormatException($"{consumer}: event values cross event chains.");
                }
            }

            foreach (JsonObject pin in Pins(definition, "outputs").Where(pin => pin.Str("scope") != null))
            {
                if (!data[node.Id].Any(edge => edge.FromPin == pin.Str("id")))
                    continue;

                HashSet<string> inside = Reach(exec[node.Id].Where(edge => edge.FromPin == pin.Str("scope")).Select(edge => edge.ToNode));
                if (consumers.Any(id => !inside.Contains(id)))
                    throw new FormatException($"{node.Id}: loop output is used outside its body.");
            }
        }

        Dictionary<string, int> state = new ();

        void Visit(string id, int depth)
        {
            if (depth > MaxPureDepth)
                throw new FormatException("Pure dependency chain is too deep.");

            if (state.TryGetValue(id, out int current))
            {
                if (current == 1)
                    throw new FormatException("Pure data wires contain a cycle.");

                return;
            }

            state[id] = 1;
            foreach (GraphEdge edge in data[id])
            {
                if (definitions[edge.ToNode]?.Str("kind") == "pure")
                    Visit(edge.ToNode, depth + 1);
            }

            state[id] = 2;
        }

        foreach (GraphNode node in graph.Nodes.Where(node => definitions[node.Id]?.Str("kind") == "pure"))
            Visit(node.Id, 0);
    }

    private static IEnumerable<JsonObject> Pins(JsonObject definition, string direction) => (definition.Arr(direction) ?? new List<object>()).Cast<JsonObject>();

    private static JsonObject Pin(string id, string type) => new () { ["id"] = id, ["type"] = type };

    private static JsonObject Definition(GraphNode node, GraphFile graph)
    {
        if (node.Type == "event.function" || node.Type == "function.call")
        {
            bool entry = node.Type == "event.function";
            GraphNode function = entry ? node : graph.Nodes.FirstOrDefault(candidate => candidate.Id == node.FunctionId && candidate.Type == "event.function");
            if (function == null)
                throw new FormatException($"{node.Id}: function does not exist.");

            if (function.Parameters.Count > 16 || function.Parameters.GroupBy(parameter => parameter.Id).Any(group => group.Count() > 1) || function.Parameters.Any(parameter => string.IsNullOrEmpty(parameter.Id) || string.IsNullOrWhiteSpace(parameter.Name) || new[] { "exec", "then", "name" }.Contains(parameter.Id) || !ValidParameterType(parameter.Type)))
                throw new FormatException($"{node.Id}: functions need up to 16 uniquely identified, named parameters with concrete types.");

            List<object> parameters = function.Parameters.Select(parameter => (object)Pin(parameter.Id, parameter.Type)).ToList();
            return new JsonObject
            {
                ["kind"] = entry ? "event" : "action",
                ["inputs"] = entry ? new List<object> { Pin("name", "string") } : new List<object> { Pin("exec", "exec") }.Concat(parameters).ToList(),
                ["outputs"] = entry ? new List<object> { Pin("then", "exec") }.Concat(parameters).ToList() : new List<object> { Pin("then", "exec") },
            };
        }

        if (!node.Type.StartsWith("variable."))
            return Definitions.TryGetValue(node.Type, out JsonObject definition) ? definition : null;

        if (node.Type != "variable.get" && node.Type != "variable.set")
            return null;

        GraphVariable variable = graph.Variables.FirstOrDefault(v => v.Id == node.VarId);
        if (variable == null)
            throw new FormatException($"{node.Id}: variable does not exist.");

        bool get = node.Type == "variable.get";
        return new JsonObject
        {
            ["kind"] = get ? "pure" : "action",
            ["inputs"] = get ? new List<object>() : new List<object> { Pin("exec", "exec"), Pin("value", variable.Type) },
            ["outputs"] = get ? new List<object> { Pin("value", variable.Type) } : new List<object> { Pin("then", "exec"), Pin("value", variable.Type) },
        };
    }

    private static bool ValidParameterType(string type)
    {
        if (string.IsNullOrEmpty(type))
            return false;

        string element = type.EndsWith("[]", StringComparison.Ordinal) ? type.Substring(0, type.Length - 2) : type;
        return element != "exec" && element != "any" && element != "T" && (Types.ContainsKey(element) || new[] { "string", "int", "float", "bool", "vector2", "vector3", "quaternion", "color" }.Contains(element));
    }

    private static string PinType(JsonObject pin, GraphNode node)
    {
        string type = pin.Str("type");
        if (type == "T")
            return node.TypeArg ?? "any";

        if (type == "T[]")
            return (node.TypeArg ?? "any") + "[]";

        return type;
    }

    private static bool Compatible(string from, string to)
    {
        if (from == to)
            return true;

        if (from == "exec" || to == "exec")
            return false;

        if (to == "any")
            return true;

        if (from.EndsWith("[]") || to.EndsWith("[]"))
            return from.EndsWith("[]") && to.EndsWith("[]") && Compatible(from.Substring(0, from.Length - 2), to.Substring(0, to.Length - 2));

        HashSet<string> seen = new ();
        while (from != null && seen.Add(from))
        {
            from = Types.Obj(from)?.Str("base");
            if (from == to)
                return true;
        }

        return false;
    }

    private static void CheckOutcomeDependencies(GraphNode node, JsonObject definition, Dictionary<string, JsonObject> definitions, Dictionary<string, GraphEdge> inputs)
    {
        Stack<string> pending = new (Pins(definition, "inputs")
            .Select(pin => inputs.TryGetValue(node.Id + "\n" + pin.Str("id"), out GraphEdge edge) ? edge.FromNode : null)
            .Where(id => id != null));

        HashSet<string> seen = new ();
        while (pending.Count > 0)
        {
            string id = pending.Pop();
            if (!seen.Add(id) || id == node.Id)
                continue;

            JsonObject dependency = definitions[id];
            if (dependency == null)
                continue;

            if (dependency.Str("kind") != "pure")
                throw new FormatException($"{node.Id}: event outcome reads another event or action.");

            foreach (JsonObject pin in Pins(dependency, "inputs"))
            {
                if (inputs.TryGetValue(id + "\n" + pin.Str("id"), out GraphEdge edge))
                    pending.Push(edge.FromNode);
            }
        }
    }
}
