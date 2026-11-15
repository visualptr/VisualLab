# VisualLab

Build SCP: Secret Laboratory server plugins as node graphs, without writing code. This repository holds the server plugin: one .NET Framework 4.8 DLL that runs the graphs through LabAPI. Graphs are edited at https://visuallab-sl.pages.dev.

## Install

From the server's LocalAdmin console:

```
p install visualptr/VisualLab
```

Run `p confirm` first if LocalAdmin asks for it, then restart the server.

To install by hand, download `VisualLab.dll` from the latest release into the server's LabAPI plugin folder and restart:

- Windows: `%AppData%\SCP Secret Laboratory\LabAPI\plugins\<port>\`
- Linux: `~/.config/SCP Secret Laboratory/LabAPI/plugins/<port>/`

Use `global` instead of the port to install for every server.

## Use

Run `visuallab dashboard` from the server console or Remote Admin. Remote Admin requires the `visuallab.dashboard` permission. The command prints a private dashboard link. Up to eight sessions can be open. Sessions expire at the earlier of the configured TTL and relay TTL.

## Configure

The first launch creates `LabAPI/configs/<port>/VisualLab/config.yml`:

```yaml
relay_url: https://visuallab-sl.pages.dev/relay
website_url: https://visuallab-sl.pages.dev
session_ttl_minutes: 720
plugins_folder: plugins
```

Restart after changing configuration. Relative plugin folders resolve under the VisualLab config directory.

## Graphs

Each graph is a `*.vlab.json` file. `index.json` stores enabled state, revision, and timestamp. Back up the whole plugins folder. Unindexed graphs start disabled.

Saves are atomic, check the base revision, and restart only the changed graph. The runtime checks wiring and types. Events run on the game thread; event outcomes apply before the event returns. Delayed chains run later. Unknown nodes are skipped with a warning.

Limits: 10,000 nodes, 50,000 wires, 256 nested calls, and 10,000 loop iterations. Runtime logs include plugin and node IDs. The dashboard receives the server's enum choices.

## Build

Install the .NET 8 SDK and the SCP:SL dedicated server, then:

```sh
dotnet build VisualLab -c Release
```

The build reads game DLLs from the default Steam dedicated server installation. Set `SL_MANAGED` to another server's `SCPSL_Data/Managed` folder if needed. The DLL is written to `VisualLab/bin/Release/net48/VisualLab.dll`.
