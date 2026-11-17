using System;
using CommandSystem;
using LabApi.Features.Permissions;

namespace VisualLab.Commands;

/// <summary>The "visuallab dashboard" command.</summary>
[CommandHandler(typeof(RemoteAdminCommandHandler))]
[CommandHandler(typeof(GameConsoleCommandHandler))]
public sealed class DashboardCommand : ICommand
{
    private const string Permission = "visuallab.dashboard";

    /// <summary>Gets the command name.</summary>
    public string Command => "visuallab";

    /// <summary>Gets the alternate command names.</summary>
    public string[] Aliases => new[] { "vl" };

    /// <summary>Gets the dashboard command description.</summary>
    public string Description => "Open the VisualLab dashboard (visuallab.dashboard permission).";

    /// <summary>Handles the dashboard command.</summary>
    /// <param name="arguments">The command arguments.</param>
    /// <param name="sender">The command sender.</param>
    /// <param name="response">The command response.</param>
    /// <returns>Whether the command succeeded.</returns>
    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (sender is not ServerConsoleSender && !sender.HasPermission(Permission))
        {
            response = "Missing permission: " + Permission;
            return false;
        }

        if (arguments.Count != 1 || !string.Equals(arguments.Array[arguments.Offset], "dashboard", StringComparison.OrdinalIgnoreCase))
        {
            response = "Usage: visuallab dashboard";
            return false;
        }

        if (Plugin.Instance == null)
        {
            response = "VisualLab is disabled.";
            return false;
        }

        Plugin.Instance.OpenDashboard(sender);
        response = "Creating dashboard session…";
        return true;
    }
}
