using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using VisualLab.Serialization;

namespace VisualLab.Bindings;

/// <summary>Finds LabAPI members for generated nodes.</summary>
public static class Catalog
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance;

    private static readonly Dictionary<string, Binding> Cache = new (StringComparer.Ordinal);

    private static Dictionary<string, JsonObject> _raw;

    /// <summary>Gets the source commit of the embedded catalog.</summary>
    public static string CatalogCommit { get; private set; } = string.Empty;

    /// <summary>Gets the number of catalog nodes.</summary>
    public static int Count
    {
        get
        {
            if (_raw == null)
                Load();

            return _raw.Count;
        }
    }

    /// <summary>Checks whether the catalog contains a node type.</summary>
    /// <param name="nodeType">The catalog node type.</param>
    /// <returns>Whether the node type exists.</returns>
    public static bool Exists(string nodeType)
    {
        if (_raw == null)
            Load();

        return _raw.ContainsKey(nodeType);
    }

    /// <summary>Finds the LabAPI binding for a node type.</summary>
    /// <param name="nodeType">The catalog node type.</param>
    /// <returns>The binding, or null if unknown.</returns>
    public static Binding Get(string nodeType)
    {
        if (Cache.TryGetValue(nodeType, out Binding binding))
            return binding;

        if (_raw == null)
            Load();

        if (!_raw.TryGetValue(nodeType, out JsonObject json))
            return null;

        binding = Resolve(nodeType, json);
        Cache[nodeType] = binding;
        return binding;
    }

    private static void Load()
    {
        using Stream stream = typeof(Catalog).Assembly.GetManifestResourceStream("VisualLab.bindings.json");
        using StreamReader reader = new (stream);
        JsonObject document = Json.ParseObject(reader.ReadToEnd());
        CatalogCommit = document.Obj("source")?.Str("commit") ?? string.Empty;
        _raw = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, object> node in document.Obj("nodes"))
            _raw[node.Key] = (JsonObject)node.Value;
    }

    private static Binding Resolve(string nodeType, JsonObject json)
    {
        Binding binding = new ()
        {
            NodeType = nodeType,
            Kind = json.Str("kind"),
            Static = json.Bool("static"),
            Extension = json.Bool("extension"),
            Access = json.Str("access"),
            Pure = json.Str("node") == "pure",
        };

        foreach (JsonObject parameter in (json.Arr("parameters") ?? new List<object>()).Cast<JsonObject>())
        {
            binding.Parameters.Add(new ParameterBinding
            {
                Name = parameter.Str("name"),
                Type = parameter.Str("type"),
                Optional = parameter.Bool("optional"),
                Out = parameter.Bool("out"),
                Params = parameter.Bool("params"),
            });
        }

        try
        {
            switch (binding.Kind)
            {
                case "event":
                    ResolveEvent(binding, json);
                    break;
                case "property":
                    ResolveProperty(binding, json);
                    break;
                case "method":
                    ResolveMethod(binding, json);
                    break;
                default:
                    binding.Error = $"unknown binding kind \"{binding.Kind}\"";
                    break;
            }
        }
        catch (Exception e)
        {
            binding.Error = e.Message;
        }

        return binding;
    }

    private static void ResolveEvent(Binding binding, JsonObject json)
    {
        Type handler = TypeResolver.Find("LabApi.Events.Handlers." + json.Str("handler"));
        if (handler == null)
        {
            binding.Error = $"LabAPI has no event handler class {json.Str("handler")}";
            return;
        }

        binding.Event = handler.GetEvent(json.Str("event"), BindingFlags.Public | BindingFlags.Static);
        if (binding.Event == null)
        {
            binding.Error = $"this LabAPI version has no event {json.Str("handler")}.{json.Str("event")}";
            return;
        }

        Type handlerType = binding.Event.EventHandlerType;
        binding.ArgsType = handlerType.IsGenericType ? handlerType.GetGenericArguments()[0] : null;
    }

    private static Type Owner(Binding binding, JsonObject json)
    {
        string ns = json.Str("ns");
        string name = json.Str("type");
        Type type = TypeResolver.Find(string.IsNullOrEmpty(ns) ? name : ns + "." + name) ?? TypeResolver.Find(name);
        if (type == null)
            binding.Error = $"this LabAPI version has no type {ns}.{name}";

        return type;
    }

    private static void ResolveProperty(Binding binding, JsonObject json)
    {
        Type owner = Owner(binding, json);
        if (owner == null)
            return;

        string member = json.Str("member");
        BindingFlags flags = (binding.Static ? BindingFlags.Static : BindingFlags.Instance) | BindingFlags.Public | BindingFlags.FlattenHierarchy;

        binding.Property = owner.GetProperties(flags)
            .Where(property => property.Name == member && property.GetIndexParameters().Length == 0)
            .OrderBy(property => Depth(owner, property.DeclaringType))
            .FirstOrDefault();

        if (binding.Property == null)
        {
            binding.Field = owner.GetField(member, flags);
            if (binding.Field == null)
            {
                binding.Error = $"this LabAPI version has no {owner.Name}.{member}";
                return;
            }
        }

        if (binding.Access == "set" && binding.Property != null && binding.Property.GetSetMethod() == null)
            binding.Error = $"{owner.Name}.{member} can't be set in this LabAPI version";
    }

    private static int Depth(Type from, Type declaring)
    {
        int depth = 0;
        for (Type type = from; type != null && type != declaring; type = type.BaseType)
            depth++;

        return depth;
    }

    private static void ResolveMethod(Binding binding, JsonObject json)
    {
        Type owner = Owner(binding, json);
        if (owner == null)
            return;

        string member = json.Str("member");
        List<ParameterBinding> wanted = binding.Parameters;
        List<MethodInfo> candidates = owner.GetMethods(Any | BindingFlags.FlattenHierarchy)
            .Where(method => method.Name == member && method.IsStatic == binding.Static && !method.IsGenericMethodDefinition && method.GetParameters().Length == wanted.Count)
            .ToList();

        bool NamesMatch(MethodInfo method) => method.GetParameters().Select(p => p.Name).SequenceEqual(wanted.Select(p => p.Name));

        bool TypesMatch(MethodInfo method) => method.GetParameters()
            .Select(p => TypeResolver.NormaliseCatalogName(TypeResolver.CatalogName(p.ParameterType)))
            .SequenceEqual(wanted.Select(p => TypeResolver.NormaliseCatalogName(p.Type)));

        binding.Method = candidates.FirstOrDefault(method => NamesMatch(method) && TypesMatch(method))
            ?? candidates.FirstOrDefault(TypesMatch)
            ?? (candidates.Count(NamesMatch) == 1 ? candidates.First(NamesMatch) : null);

        if (binding.Method == null)
        {
            binding.Error = $"this LabAPI version has no {owner.Name}.{member}({string.Join(", ", wanted.Select(p => p.Type))})";
            return;
        }

        binding.MethodParams = binding.Method.GetParameters();
    }
}
