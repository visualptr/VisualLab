using System;
using System.Collections.Generic;
using System.Linq;
using VisualLab.Runtime;
using VisualLab.Serialization;

namespace VisualLab.Graphs;

/// <summary>A parsed `visuallab/1` graph (see docs/FORMAT.md).</summary>
public sealed class GraphFile
{
    /// <summary>The supported graph format identifier.</summary>
    public const string Format = "visuallab/1";

    /// <summary>Gets or sets the graph metadata.</summary>
    public JsonObject Meta { get; set; } = new ();

    /// <summary>Gets or sets the graph nodes.</summary>
    public List<GraphNode> Nodes { get; set; } = new ();

    /// <summary>Gets or sets the graph wires.</summary>
    public List<GraphEdge> Edges { get; set; } = new ();

    /// <summary>Gets or sets the graph variables.</summary>
    public List<GraphVariable> Variables { get; set; } = new ();

    /// <summary>Gets the graph name.</summary>
    public string Name => Meta.Str("name") ?? "Untitled";

    /// <summary>Parses and validates a graph file.</summary>
    /// <param name="json">The graph JSON object.</param>
    /// <returns>The validated graph.</returns>
    public static GraphFile Parse(JsonObject json)
    {
        if (json == null)
            throw new FormatException("not a JSON object");

        string format = json.Str("format");
        if (format != Format)
            throw new FormatException(format == null ? "missing \"format\"" : $"format \"{format}\" isn't supported (this server reads {Format})");

        if (json.Arr("nodes") == null || json.Arr("edges") == null)
            throw new FormatException("Missing nodes or edges.");

        GraphFile graph = new () { Meta = json.Obj("meta") ?? new JsonObject() };
        foreach (JsonObject variable in (json.Arr("variables") ?? new List<object>()).OfType<JsonObject>())
        {
            graph.Variables.Add(new GraphVariable
            {
                Id = variable.Str("id"),
                Name = variable.Str("name"),
                Type = variable.Str("type"),
                Persistent = variable.Bool("persistent"),
                Default = variable.TryGetValue("default", out object fallback) ? fallback : null,
            });
        }

        foreach (JsonObject node in json.Arr("nodes").OfType<JsonObject>())
        {
            if (node.TryGetValue("parameters", out object parameters) && (parameters is not List<object> list || list.Any(parameter => parameter is not JsonObject)))
                throw new FormatException("Invalid function parameters.");

            graph.Nodes.Add(new GraphNode
            {
                Id = node.Str("id"),
                Type = node.Str("type"),
                FunctionId = node.Str("function"),
                Parameters = (node.Arr("parameters") ?? new List<object>()).OfType<JsonObject>().Select(parameter => new GraphParameter { Id = parameter.Str("id"), Name = parameter.Str("name"), Type = parameter.Str("type") }).ToList(),
                VarId = node.Str("variable"),
                TypeArg = node.Str("typeArg"),
                Values = node.Obj("values") ?? new JsonObject(),
            });
        }

        foreach (JsonObject edge in json.Arr("edges").OfType<JsonObject>())
        {
            JsonObject from = edge.Obj("from");
            JsonObject to = edge.Obj("to");
            if (from == null || to == null)
                throw new FormatException("an edge is missing \"from\" or \"to\"");

            graph.Edges.Add(new GraphEdge { FromNode = from.Str("node"), FromPin = from.Str("pin"), ToNode = to.Str("node"), ToPin = to.Str("pin") });
        }

        if (graph.Nodes.Any(node => string.IsNullOrEmpty(node.Id) || string.IsNullOrEmpty(node.Type)))
            throw new FormatException("a node has no id or type");

        IGrouping<string, GraphNode> duplicate = graph.Nodes.GroupBy(node => node.Id).FirstOrDefault(group => group.Count() > 1);
        if (duplicate != null)
            throw new FormatException($"two nodes share the id \"{duplicate.Key}\"");

        GraphValidation.Check(graph);
        return graph;
    }
}
