# VisualLab

Build SCP: Secret Laboratory server plugins as node graphs, without writing code. 

https://visuallab-sl.pages.dev.

## Install

From the server's LocalAdmin console:

```
p install visualptr/VisualLab
```

Run `p confirm` first if LocalAdmin asks for it, then restart the server.

To install by hand, download `VisualLab.dll` from the latest release into the server's LabAPI plugin folder and restart:

- Windows: `%AppData%\SCP Secret Laboratory\LabAPI\plugins\<port>\`
- Linux: `~/.config/SCP Secret Laboratory/LabAPI/plugins/<port>/`

## Use

Run `visuallab dashboard` from the server console or Remote Admin. Remote Admin requires the `visuallab.dashboard` permission. The command prints a private dashboard link. Up to eight sessions can be open. Sessions expire at the earlier of the configured TTL and relay TTL.
