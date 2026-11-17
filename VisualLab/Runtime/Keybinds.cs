using System;
using System.Collections.Generic;
using System.Linq;
using LabApi.Features.Wrappers;
using UnityEngine;
using UserSettings.ServerSpecific;
using VisualLab.Runtime.Payloads;

namespace VisualLab.Runtime;

/// <summary>Shares graph key bindings through the server-specific settings menu.</summary>
public static class Keybinds
{
    private const int HeaderId = 0x564C0000;

    private static readonly Dictionary<int, KeyValuePair<GraphRuntime, RuntimeNode>> Bound = new ();
    private static readonly Dictionary<int, ServerSpecificSettingBase> Settings = new ();
    private static readonly HashSet<ServerSpecificSettingBase> Published = new ();
    private static readonly SSGroupHeader Header = new (HeaderId, "VisualLab");
    private static bool _subscribed;

    /// <summary>Adds a key binding for a graph node.</summary>
    /// <param name="runtime">The graph runtime.</param>
    /// <param name="node">The runtime node.</param>
    /// <param name="label">The label shown to players.</param>
    /// <param name="key">The suggested key.</param>
    /// <param name="hint">The hint shown to players.</param>
    /// <returns>The setting ID.</returns>
    public static int Add(GraphRuntime runtime, RuntimeNode node, string label, KeyCode key, string hint)
    {
        int id = Id(runtime.PluginId + "\n" + node.Id);
        while (Settings.ContainsKey(id) || id == HeaderId)
            id++;

        Settings[id] = new SSKeybindSetting(id, label, key, true, true, string.IsNullOrEmpty(hint) ? null : hint);
        Bound[id] = new KeyValuePair<GraphRuntime, RuntimeNode>(runtime, node);
        if (!_subscribed)
        {
            ServerSpecificSettingsSync.ServerOnSettingValueReceived += Received;
            _subscribed = true;
        }

        Publish();
        return id;
    }

    /// <summary>Removes a key binding.</summary>
    /// <param name="id">The setting ID.</param>
    public static void Remove(int id)
    {
        if (!Settings.Remove(id))
            return;

        Bound.Remove(id);
        if (Bound.Count == 0 && _subscribed)
        {
            ServerSpecificSettingsSync.ServerOnSettingValueReceived -= Received;
            _subscribed = false;
        }

        Publish();
    }

    /// <summary>Adds a player-specific graph setting.</summary>
    /// <param name="runtime">The owning graph.</param>
    /// <param name="node">The event node.</param>
    /// <param name="factory">The setting factory.</param>
    /// <returns>The allocated setting identifier.</returns>
    public static int AddSetting(GraphRuntime runtime, RuntimeNode node, Func<int, ServerSpecificSettingBase> factory)
    {
        string name = node.Graph.Values.TryGetValue("name", out object value) ? Values.ToText(value).Trim() : string.Empty;
        if (name.Length == 0 || Bound.Values.Any(pair => pair.Key == runtime && pair.Value.Graph.Values.TryGetValue("name", out object other) && Values.ToText(other).Trim() == name))
            throw new ArgumentException("Setting names must be nonempty and unique within a graph.");

        int id = Id(runtime.PluginId + "\n" + node.Id);
        while (Settings.ContainsKey(id) || id == HeaderId)
            id++;

        Settings[id] = factory(id);
        Bound[id] = new KeyValuePair<GraphRuntime, RuntimeNode>(runtime, node);
        if (!_subscribed)
        {
            ServerSpecificSettingsSync.ServerOnSettingValueReceived += Received;
            _subscribed = true;
        }

        Publish();
        return id;
    }

    /// <summary>Reads a player's current setting value.</summary>
    /// <param name="runtime">The owning graph.</param>
    /// <param name="name">The setting name.</param>
    /// <param name="player">The player.</param>
    /// <returns>The value, or null if the setting does not exist.</returns>
    public static SettingChange Read(GraphRuntime runtime, string name, Player player)
    {
        if (player == null)
            return null;

        foreach (KeyValuePair<int, KeyValuePair<GraphRuntime, RuntimeNode>> pair in Bound)
        {
            if (pair.Value.Key != runtime || !pair.Value.Value.Graph.Values.TryGetValue("name", out object value) || Values.ToText(value).Trim() != name.Trim())
                continue;

            ServerSpecificSettingBase setting = ServerSpecificSettingsSync.TryGetSettingOfUser(player.ReferenceHub, pair.Key, out ServerSpecificSettingBase current) ? current : Settings[pair.Key];
            SettingChange change = Change(player, setting);
            if (ReferenceEquals(setting, Settings[pair.Key]) && setting is SSDropdownSetting dropdown)
                change.Index = dropdown.DefaultOptionIndex;

            return change;
        }

        return null;
    }

    private static SettingChange Change(Player player, ServerSpecificSettingBase setting) => new ()
    {
        Player = player,
        Number = setting is SSSliderSetting slider ? slider.SyncFloatValue : 0f,
        Enabled = setting is SSTwoButtonsSetting toggle && toggle.SyncIsB,
        Index = setting is SSDropdownSetting dropdown ? dropdown.SyncSelectionIndexValidated : 0,
        Text = setting is SSPlaintextSetting text ? text.SyncInputText ?? string.Empty : string.Empty,
    };

    private static int Id(string text)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char c in text)
                hash = (hash ^ c) * 16777619;

            return (int)(hash & 0x3FFFFFFF) | 0x40000000;
        }
    }

    private static void Publish()
    {
        List<ServerSpecificSettingBase> all = (ServerSpecificSettingsSync.DefinedSettings ?? Array.Empty<ServerSpecificSettingBase>())
            .Where(setting => !Published.Contains(setting))
            .ToList();

        Published.Clear();
        if (Settings.Count > 0)
        {
            all.Add(Header);
            Published.Add(Header);
            foreach (ServerSpecificSettingBase setting in Settings.Values.OrderBy(setting => setting.Label, StringComparer.OrdinalIgnoreCase))
            {
                all.Add(setting);
                Published.Add(setting);
            }
        }

        ServerSpecificSettingsSync.DefinedSettings = all.ToArray();
        ServerSpecificSettingsSync.SendToAll();
    }

    private static void Received(ReferenceHub hub, ServerSpecificSettingBase setting)
    {
        if (!Bound.TryGetValue(setting.SettingId, out KeyValuePair<GraphRuntime, RuntimeNode> target) || (setting is SSKeybindSetting keybind && !keybind.SyncIsPressed))
            return;

        Player player = Player.Get(hub);
        if (player != null)
            target.Key.Fire(target.Value, setting is SSKeybindSetting ? new KeyPress { Player = player } : Change(player, setting));
    }
}
