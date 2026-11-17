using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommandSystem;
using LabApi.Features.Console;
using LabApi.Features.Wrappers;
using LabApi.Loader;
using MEC;
using VisualLab.Graphs;
using VisualLab.Relay;
using VisualLab.Serialization;
using VisualLab.Storage;

namespace VisualLab;

/// <summary>The plugin entry point.</summary>
public sealed class Plugin : LabApi.Loader.Features.Plugins.Plugin<Config>
{
    private const int MaxSessions = 8;
    private const int MaxActionsPerFrame = 100;

    private readonly ConcurrentQueue<Action> _mainThread = new ();
    private readonly List<RelayConnection> _sessions = new ();
    private CancellationTokenSource _lifetime;
    private PluginStore _store;
    private CoroutineHandle _pump;
    private bool _opening;
    private bool _keepingAwake;

    /// <summary>Gets the active plugin instance.</summary>
    public static Plugin Instance { get; private set; }

    /// <summary>Gets the name.</summary>
    public override string Name => "VisualLab";

    /// <summary>Gets the plugin description.</summary>
    public override string Description => "Visual LabAPI graph plugins and encrypted dashboard.";

    /// <summary>Gets the author.</summary>
    public override string Author => "VisualLab";

    /// <summary>Gets the plugin version.</summary>
    public override Version Version => new (0, 2, 0);

    /// <summary>Gets the minimum supported LabAPI version.</summary>
    public override Version RequiredApiVersion => new (1, 1, 4);

    /// <summary>Starts the plugin.</summary>
    public override void Enable()
    {
        Instance = this;
        _lifetime = new CancellationTokenSource();
        _store = new PluginStore(PluginsFolder());
        _pump = Timing.RunCoroutine(Pump());
        Logger.Info("[VisualLab] Ready. Run visuallab dashboard to open the dashboard.");
    }

    /// <summary>Stops the plugin and closes dashboard sessions.</summary>
    public override void Disable()
    {
        _lifetime?.Cancel();
        Timing.KillCoroutines(_pump);
        foreach (RelayConnection session in _sessions)
            session.Dispose();

        _sessions.Clear();
        KeepAwake(false);
        _store?.Dispose();
        Instance = null;
    }

    /// <summary>Opens a dashboard session for the command sender.</summary>
    /// <param name="sender">The command sender.</param>
    public void OpenDashboard(ICommandSender sender)
    {
        if (_opening)
        {
            sender.Respond("A dashboard session is being created. Please wait.", false);
            return;
        }

        if (_sessions.Count >= MaxSessions)
        {
            sender.Respond("Eight dashboard sessions are already open. Wait for one to expire.", false);
            return;
        }

        _opening = true;
        JsonObject info = ServerInfo(sender);
        CancellationToken cancellation = _lifetime.Token;
        _ = Task.Run(() => CreateSession(sender, info, cancellation));
    }

    private static JsonObject ServerInfo(ICommandSender sender)
    {
        return new JsonObject
        {
            ["name"] = Server.ServerListName,
            ["labapi"] = typeof(Player).Assembly.GetName().Version.ToString(),
            ["plugin"] = Instance.Version.ToString(),
            ["formats"] = new[] { "visuallab/1" },
            ["user"] = sender.LogName,
            ["enums"] = GraphValidation.EnumValues(),
        };
    }

    private string PluginsFolder()
    {
        string folder = Config.PluginsFolder;
        return Path.IsPathRooted(folder) ? folder : Path.Combine(ConfigurationLoader.GetConfigDirectory(this).FullName, folder);
    }

    private async Task CreateSession(ICommandSender sender, JsonObject info, CancellationToken cancellation)
    {
        try
        {
            RelayConnection session = await RelayConnection.Create(Config, _store, _mainThread, info, cancellation).ConfigureAwait(false);
            _mainThread.Enqueue(() => StartSession(sender, session, cancellation));
        }
        catch (Exception e)
        {
            _mainThread.Enqueue(() =>
            {
                _opening = false;
                sender.Respond("Could not open VisualLab dashboard: " + e.Message, false);
            });
        }
    }

    private void StartSession(ICommandSender sender, RelayConnection session, CancellationToken cancellation)
    {
        _opening = false;
        if (cancellation.IsCancellationRequested)
        {
            session.Dispose();
            return;
        }

        _sessions.Add(session);
        session.Start();
        sender.Respond("VisualLab dashboard: " + session.Link, true);
    }

    private void KeepAwake(bool wanted)
    {
        if (wanted == _keepingAwake)
            return;

        if (wanted && Server.PauseIdleMode)
            return;

        _keepingAwake = wanted;
        Server.PauseIdleMode = wanted;
    }

    private IEnumerator<float> Pump()
    {
        while (!_lifetime.IsCancellationRequested)
        {
            for (int i = 0; i < MaxActionsPerFrame && _mainThread.TryDequeue(out Action action); i++)
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    Logger.Error("[VisualLab] " + e);
                }
            }

            _sessions.RemoveAll(session => session.Closed);
            KeepAwake(_sessions.Exists(session => session.HasClients));
            try
            {
                _store.Tick();
            }
            catch (Exception e)
            {
                Logger.Error("[VisualLab] " + e);
            }

            yield return Timing.WaitForOneFrame;
        }
    }
}
