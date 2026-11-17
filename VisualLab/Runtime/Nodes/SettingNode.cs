using System;
using System.Linq;
using UserSettings.ServerSpecific;
using VisualLab.Runtime.Payloads;

namespace VisualLab.Runtime.Nodes;

/// <summary>Publishes a player-specific setting and receives its changes.</summary>
public sealed class SettingNode : PayloadNode
{
    private int? _id;

    /// <summary>Gets the setting payload type.</summary>
    protected override Type PayloadType => typeof(SettingChange);

    /// <summary>Publishes the setting.</summary>
    /// <param name="runtime">The graph runtime.</param>
    /// <param name="node">The event node.</param>
    public override void Start(GraphRuntime runtime, RuntimeNode node)
    {
        string label = Setting(node, "label", runtime.Name);
        string hint = Setting(node, "hint", string.Empty);
        _id = Keybinds.AddSetting(runtime, node, id =>
        {
            ServerSpecificSettingBase setting;
            switch (node.Graph.Type)
            {
                case "event.setting.slider":
                    float min = Setting(node, "min", 0f);
                    float max = Setting(node, "max", 100f);
                    if (float.IsNaN(min) || float.IsInfinity(min) || float.IsNaN(max) || float.IsInfinity(max) || min > max)
                        throw new ArgumentException("Slider bounds must be finite and ordered.");

                    setting = new SSSliderSetting(id, label, min, max, Setting(node, "default", 50f), hint: hint);
                    break;
                case "event.setting.toggle":
                    setting = new SSTwoButtonsSetting(id, label, "Off", "On", Setting(node, "default", false), hint);
                    break;
                case "event.setting.dropdown":
                    string[] options = node.Graph.Values.TryGetValue("options", out object value) ? (value as System.Collections.IEnumerable)?.Cast<object>().Select(Values.ToText).ToArray() ?? Array.Empty<string>() : new[] { "First", "Second" };
                    if (options.Length == 0 || options.Length > 256)
                        throw new ArgumentException("Dropdowns need 1 to 256 options.");

                    setting = new SSDropdownSetting(id, label, options, Math.Max(0, Math.Min(options.Length - 1, Setting(node, "default", 0))), hint: hint);
                    break;
                default:
                    setting = new SSPlaintextSetting(id, label, Setting(node, "placeholder", "..."), Math.Max(1, Math.Min(1000, Setting(node, "limit", 64))), hint: hint);
                    break;
            }

            setting.ApplyDefaultValues();
            return setting;
        });
    }

    /// <summary>Removes the setting.</summary>
    /// <param name="runtime">The graph runtime.</param>
    /// <param name="node">The event node.</param>
    public override void Stop(GraphRuntime runtime, RuntimeNode node)
    {
        if (_id.HasValue)
            Keybinds.Remove(_id.Value);

        _id = null;
    }
}
