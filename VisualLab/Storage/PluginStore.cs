using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VisualLab.Graphs;
using VisualLab.Logging;
using VisualLab.Relay;
using VisualLab.Runtime;
using VisualLab.Serialization;

namespace VisualLab.Storage;

/// <summary>Loads, saves, and runs graph plugins.</summary>
public sealed class PluginStore : IDisposable
{
    private readonly string _folder;
    private readonly Dictionary<string, StoredPlugin> _entries = new ();

    /// <summary>Initializes a new instance of the <see cref="PluginStore"/> class.</summary>
    /// <param name="folder">The graph storage folder.</param>
    public PluginStore(string folder)
    {
        _folder = folder;
        Directory.CreateDirectory(folder);
        Load();
    }

    /// <summary>Occurs when a stored graph changes.</summary>
    public event Action<JsonObject> Changed;

    private static double Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private string IndexPath => Path.Combine(_folder, "index.json");

    /// <summary>Handles a dashboard request for stored graphs.</summary>
    /// <param name="request">The dashboard request.</param>
    /// <returns>The response fields.</returns>
    public JsonObject Handle(JsonObject request)
    {
        string id = request.Str("id");
        switch (request.Str("type"))
        {
            case "list":
                return new JsonObject { ["plugins"] = _entries.Values.Select(Summary).ToList() };
            case "get":
            {
                StoredPlugin found = Find(id);
                return new JsonObject { ["plugin"] = Summary(found), ["graph"] = found.Graph };
            }

            case "create":
            {
                id = "p_" + Guid.NewGuid().ToString("N");
                JsonObject graph = request.Obj("graph");
                Compile(id, graph);
                StoredPlugin entry = new () { Id = id, Graph = graph, Enabled = request.Bool("enabled"), Revision = 1, UpdatedAt = Now };
                WriteAtomic(GraphPath(id), Json.Write(graph, true));
                _entries[id] = entry;
                WriteIndex();
                Reload(entry);
                return Publish(entry);
            }

            case "save":
            {
                StoredPlugin entry = Find(id);
                if (request.Num("baseRevision", -1) != entry.Revision)
                    throw new ProtocolException("conflict", "Another dashboard saved this plugin.", Summary(entry));

                JsonObject graph = request.Obj("graph");
                Compile(id, graph);
                WriteAtomic(GraphPath(id), Json.Write(graph, true));
                entry.Graph = graph;
                entry.Revision++;
                entry.UpdatedAt = Now;
                WriteIndex();
                Reload(entry);
                return Publish(entry);
            }

            case "setEnabled":
            {
                StoredPlugin entry = Find(id);
                entry.Enabled = request.Bool("enabled");
                WriteIndex();
                Reload(entry);
                return Publish(entry);
            }

            case "delete":
            {
                StoredPlugin entry = Find(id);
                File.Delete(GraphPath(entry.Id));
                entry.Runtime?.Stop();
                _entries.Remove(entry.Id);
                WriteIndex();
                Changed?.Invoke(new JsonObject { ["event"] = "pluginDeleted", ["id"] = entry.Id });
                return new JsonObject();
            }

            default:
                throw new ProtocolException("invalid", "Unknown request.");
        }
    }

    /// <summary>Stops all stored graphs.</summary>
    public void Dispose()
    {
        foreach (StoredPlugin entry in _entries.Values)
            entry.Runtime?.Stop();
    }

    private static void WriteAtomic(string path, string content)
    {
        string temp = path + ".tmp";
        File.WriteAllText(temp, content);
        if (File.Exists(path))
            File.Replace(temp, path, null);
        else
            File.Move(temp, path);
    }

    private static GraphRuntime Compile(string id, JsonObject graph)
    {
        try
        {
            GraphRuntime runtime = new (id, GraphFile.Parse(graph));
            if (runtime.Errors.Count > 0)
                throw new FormatException(string.Join("; ", runtime.Errors));

            return runtime;
        }
        catch (Exception e)
        {
            throw new ProtocolException("invalid", e.Message);
        }
    }

    private string GraphPath(string id) => Path.Combine(_folder, id + ".vlab.json");

    private void Load()
    {
        JsonObject index = File.Exists(IndexPath) ? Json.ParseObject(File.ReadAllText(IndexPath)) : new JsonObject();
        foreach (string file in Directory.GetFiles(_folder, "*.vlab.json"))
        {
            string id = Path.GetFileName(file).Replace(".vlab.json", string.Empty);
            JsonObject state = index.Obj(id) ?? new JsonObject();
            StoredPlugin entry = new () { Id = id, Enabled = state.Bool("enabled"), Revision = state.Num("revision", 1), UpdatedAt = state.Num("updatedAt", Now) };
            _entries[id] = entry;
            try
            {
                entry.Graph = Json.ParseObject(File.ReadAllText(file));
                Reload(entry);
            }
            catch (Exception e)
            {
                entry.Error = e.Message;
                RuntimeLog.Write(id, id, LogLevel.Error, e.Message);
            }
        }
    }

    private void WriteIndex()
    {
        JsonObject index = new ();
        foreach (StoredPlugin entry in _entries.Values)
            index[entry.Id] = new JsonObject { ["enabled"] = entry.Enabled, ["revision"] = entry.Revision, ["updatedAt"] = entry.UpdatedAt };

        WriteAtomic(IndexPath, Json.Write(index, true));
    }

    private void Reload(StoredPlugin entry)
    {
        entry.Runtime?.Stop();
        entry.Runtime = null;
        entry.Error = null;
        try
        {
            entry.Runtime = Compile(entry.Id, entry.Graph);
            if (entry.Enabled)
                entry.Runtime.Start();

            if (entry.Runtime.Errors.Count > 0)
                throw new Exception(string.Join("; ", entry.Runtime.Errors));
        }
        catch (Exception e)
        {
            entry.Runtime?.Stop();
            entry.Error = e.Message;
        }
    }

    private StoredPlugin Find(string id)
    {
        if (id == null || !_entries.TryGetValue(id, out StoredPlugin entry))
            throw new ProtocolException("not_found", "Plugin not found.");

        return entry;
    }

    private JsonObject Summary(StoredPlugin entry)
    {
        JsonObject summary = new ()
        {
            ["id"] = entry.Id,
            ["meta"] = entry.Graph?.Obj("meta") ?? new JsonObject { ["name"] = entry.Id },
            ["enabled"] = entry.Enabled,
            ["revision"] = entry.Revision,
            ["updatedAt"] = entry.UpdatedAt,
            ["nodeCount"] = entry.Graph?.Arr("nodes")?.Count ?? 0,
            ["state"] = entry.Error != null ? "error" : entry.Enabled ? "running" : "disabled",
        };

        if (entry.Error != null)
            summary["message"] = entry.Error;
        else if (entry.Runtime?.Warnings.Count > 0)
            summary["message"] = string.Join("; ", entry.Runtime.Warnings);

        return summary;
    }

    private JsonObject Publish(StoredPlugin entry)
    {
        JsonObject summary = Summary(entry);
        Changed?.Invoke(new JsonObject { ["event"] = "pluginChanged", ["plugin"] = summary });
        return new JsonObject { ["plugin"] = summary };
    }
}
