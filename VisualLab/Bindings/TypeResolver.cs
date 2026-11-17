using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace VisualLab.Bindings;

/// <summary>Finds game and LabAPI types by catalog name.</summary>
public static class TypeResolver
{
    private static readonly string[] AssemblyOrder = { "LabApi", "Assembly-CSharp", "Assembly-CSharp-firstpass", "CommandSystem.Core", "Mirror", "UnityEngine.CoreModule", "mscorlib", "System", "System.Core" };

    private static readonly string[] CatalogPrefixes = { "params ", "this ", "out ", "ref ", "in " };

    private static readonly Dictionary<Type, string> Aliases = new ()
    {
        [typeof(string)] = "string",
        [typeof(int)] = "int",
        [typeof(float)] = "float",
        [typeof(bool)] = "bool",
        [typeof(double)] = "double",
        [typeof(long)] = "long",
        [typeof(ulong)] = "ulong",
        [typeof(uint)] = "uint",
        [typeof(short)] = "short",
        [typeof(ushort)] = "ushort",
        [typeof(byte)] = "byte",
        [typeof(sbyte)] = "sbyte",
        [typeof(char)] = "char",
        [typeof(object)] = "object",
        [typeof(decimal)] = "decimal",
    };

    private static Dictionary<string, Type> _byFull;
    private static Dictionary<string, Type> _byShort;

    /// <summary>Finds a game type by full or short name.</summary>
    /// <param name="name">The type name.</param>
    /// <returns>The type, or null if unknown.</returns>
    public static Type Find(string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;

        if (_byFull == null)
            Build();

        name = name.Trim();
        if (_byFull.TryGetValue(name, out Type type))
            return type;

        if (_byShort.TryGetValue(name, out type))
            return type;

        int dot = name.LastIndexOf('.');
        return dot > 0 && _byShort.TryGetValue(name.Substring(dot + 1), out type) ? type : null;
    }

    /// <summary>Finds the CLR type for a pin.</summary>
    /// <param name="pinType">The catalog pin type.</param>
    /// <returns>The CLR type, or object if unknown.</returns>
    public static Type ForPin(string pinType)
    {
        switch (pinType)
        {
            case "string":
                return typeof(string);
            case "int":
                return typeof(int);
            case "float":
                return typeof(float);
            case "bool":
                return typeof(bool);
            case "vector3":
                return typeof(Vector3);
            case "vector2":
                return typeof(Vector2);
            case "quaternion":
                return typeof(Quaternion);
            case "color":
                return typeof(Color);
            case null:
            case "any":
            case "T":
            case "exec":
                return typeof(object);
        }

        if (pinType.EndsWith("[]"))
            return typeof(List<>).MakeGenericType(ForPin(pinType.Substring(0, pinType.Length - 2)));

        return Find(pinType) ?? typeof(object);
    }

    /// <summary>Formats a CLR type for the catalog.</summary>
    /// <param name="type">The requested type.</param>
    /// <returns>The catalog type name.</returns>
    public static string CatalogName(Type type)
    {
        if (type.IsByRef)
            type = type.GetElementType();

        if (Aliases.TryGetValue(type, out string alias))
            return alias;

        if (type.IsArray)
            return CatalogName(type.GetElementType()) + "[]";

        Type underlying = Nullable.GetUnderlyingType(type);
        if (underlying != null)
            return CatalogName(underlying) + "?";

        if (type.IsGenericType)
        {
            string name = type.Name.Substring(0, type.Name.IndexOf('`'));
            return name + "<" + string.Join(", ", type.GetGenericArguments().Select(CatalogName)) + ">";
        }

        return type.Name;
    }

    /// <summary>Normalizes a catalog type name for comparison.</summary>
    /// <param name="name">The type name.</param>
    /// <returns>The normalized name.</returns>
    public static string NormaliseCatalogName(string name)
    {
        name = name.Trim();
        foreach (string prefix in CatalogPrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal))
                name = name.Substring(prefix.Length);
        }

        StringBuilder result = new ();
        StringBuilder word = new ();
        foreach (char c in name + "\0")
        {
            if (char.IsLetterOrDigit(c) || c == '_' || c == '.')
            {
                word.Append(c);
                continue;
            }

            string segment = word.ToString();
            int dot = segment.LastIndexOf('.');
            result.Append(dot >= 0 ? segment.Substring(dot + 1) : segment);
            word.Clear();
            if (c != '\0')
                result.Append(c);
        }

        return result.ToString().Replace(" ", string.Empty);
    }

    private static void Build()
    {
        _byFull = new Dictionary<string, Type>(StringComparer.Ordinal);
        _byShort = new Dictionary<string, Type>(StringComparer.Ordinal);
        IOrderedEnumerable<Assembly> assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => Array.IndexOf(AssemblyOrder, assembly.GetName().Name) >= 0)
            .OrderBy(assembly => Array.IndexOf(AssemblyOrder, assembly.GetName().Name));

        foreach (Assembly assembly in assemblies)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = e.Types.Where(type => type != null).ToArray();
            }

            foreach (Type type in types.OrderBy(type => type.Namespace == "LabApi.Features.Wrappers" ? 0 : 1))
            {
                if (type.IsGenericTypeDefinition || type.FullName == null)
                    continue;

                string full = type.FullName.Replace('+', '.');
                if (!_byFull.ContainsKey(full))
                    _byFull[full] = type;

                string shortName = ShortName(type);
                if (!_byShort.ContainsKey(shortName))
                    _byShort[shortName] = type;

                if (type.IsNested && !_byShort.ContainsKey(type.Name))
                    _byShort[type.Name] = type;
            }
        }
    }

    private static string ShortName(Type type) => type.IsNested ? ShortName(type.DeclaringType) + "." + type.Name : type.Name;
}
