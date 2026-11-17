using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VisualLab.Serialization;

namespace VisualLab.Storage;

/// <summary>Values a graph keeps on disk between restarts.</summary>
public sealed class SavedData
{
    private const int MaxKeys = 100000;

    private readonly string _path;
    private readonly object _gate = new ();
    private readonly object _file = new ();
    private JsonObject _root;
    private bool _dirty;
    private bool _writing;
    private string _queued;
    private string _failure;

    /// <summary>Initializes a new instance of the <see cref="SavedData"/> class.</summary>
    /// <param name="path">The data file path.</param>
    public SavedData(string path)
    {
        _path = path;
    }

    private JsonObject Root
    {
        get
        {
            if (_root != null)
                return _root;

            try
            {
                _root = File.Exists(_path) ? Json.ParseObject(File.ReadAllText(_path)) : new JsonObject();
            }
            catch (Exception)
            {
                _root = new JsonObject();
            }

            return _root;
        }
    }

    /// <summary>Reads a saved value.</summary>
    /// <param name="key">The value name.</param>
    /// <param name="userId">The owning player, or null for a shared value.</param>
    /// <returns>The saved value, or null if missing.</returns>
    public object Get(string key, string userId) => Section(userId, false) is JsonObject section && section.TryGetValue(key, out object value) ? value : null;

    /// <summary>Saves or deletes a value.</summary>
    /// <param name="key">The value name.</param>
    /// <param name="userId">The owning player, or null for a shared value.</param>
    /// <param name="value">The value to save, or null to delete it.</param>
    public void Set(string key, string userId, object value)
    {
        if (value == null)
        {
            if (Section(userId, false) is JsonObject existing && existing.Remove(key))
                _dirty = true;

            return;
        }

        JsonObject section = Section(userId, true);
        if (!section.ContainsKey(key) && section.Count >= MaxKeys)
            throw new InvalidOperationException("Too many saved values.");

        section[key] = value;
        _dirty = true;
    }

    /// <summary>Reads a saved variable.</summary>
    /// <param name="id">The variable ID.</param>
    /// <param name="value">The saved literal.</param>
    /// <returns>Whether the variable was saved before.</returns>
    public bool TryGetVariable(string id, out object value)
    {
        value = null;
        return Root.Obj("variables") is JsonObject variables && variables.TryGetValue(id, out value);
    }

    /// <summary>Saves a variable.</summary>
    /// <param name="id">The variable ID.</param>
    /// <param name="value">The literal to save.</param>
    public void SetVariable(string id, object value)
    {
        if (Root.Obj("variables") is not JsonObject variables)
            Root["variables"] = variables = new JsonObject();

        variables[id] = value;
        _dirty = true;
    }

    /// <summary>Writes pending changes to disk, off the game thread unless told to wait.</summary>
    /// <param name="wait">Whether to finish writing before returning.</param>
    public void Flush(bool wait = false)
    {
        string failure = Interlocked.Exchange(ref _failure, null);
        if (_dirty)
        {
            _dirty = false;
            string json = Json.Write(Root);
            lock (_gate)
            {
                _queued = json;
                if (!wait && !_writing)
                {
                    _writing = true;
                    Task.Run(Drain);
                }
            }
        }

        if (wait)
        {
            Drain();
            lock (_file)
            {
            }
        }

        if (failure != null)
            throw new IOException(failure);
    }

    /// <summary>Deletes every saved value.</summary>
    public void Delete()
    {
        _root = new JsonObject();
        _dirty = false;
        lock (_gate)
            _queued = null;

        lock (_file)
        {
            if (File.Exists(_path))
                File.Delete(_path);
        }
    }

    private void Drain()
    {
        while (true)
        {
            string json;
            lock (_gate)
            {
                json = _queued;
                _queued = null;
                if (json == null)
                {
                    _writing = false;
                    return;
                }
            }

            try
            {
                lock (_file)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_path));
                    PluginStore.WriteAtomic(_path, json);
                }
            }
            catch (Exception e)
            {
                _failure = e.Message;
            }
        }
    }

    private JsonObject Section(string userId, bool create)
    {
        string name = userId == null ? "values" : "players";
        JsonObject section = Root.Obj(name);
        if (section == null)
        {
            if (!create)
                return null;

            Root[name] = section = new JsonObject();
        }

        if (userId == null)
            return section;

        JsonObject player = section.Obj(userId);
        if (player == null && create)
            section[userId] = player = new JsonObject();

        return player;
    }
}
