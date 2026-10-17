# Linux desktop and G-Earth helpers

These are optional wrappers for Linux desktops. The launcher itself writes
`~/.local/share/applications/HabboCustomLauncher.desktop` (LF line endings) and
sets it as the `x-scheme-handler/habbo` default on every start, so `habbo:`
links work without them. It keeps an existing handler entry for the same
binary, such as the hidden one `install-desktop` writes.

## Desktop entry

```sh
install -m 0755 linux/install-desktop ~/.local/share/HabboCustomLauncher/install-desktop
install -m 0755 linux/habbo-launcher ~/.local/bin/habbo-launcher
~/.local/share/HabboCustomLauncher/install-desktop
install -m 0644 linux/habbo-desktop-fix.service ~/.config/systemd/user/
install -m 0644 linux/habbo-desktop-fix.path ~/.config/systemd/user/
systemctl --user enable --now habbo-desktop-fix.path
```

`habbo.desktop` stays in the app launcher. `HabboCustomLauncher.desktop` is
hidden and only handles `habbo:` URLs. Older launcher builds overwrote that
file with CRLF line endings on every start; the path unit restores it for them.

## Existing-window focus

`habbo-launcher` focuses an already-open launcher window (Hyprland / Omarchy
`hl.dsp.*` first, then generic `hyprctl`). If the process is alive with no
window, it restarts it.

## G-Earth memory access

Build `gearth-client-access.so` from the G-Earth tree (`tools/linux`) and
install it to `~/.local/lib/gearth-client-access.so`. The wrapper preloads it
so the AIR `Habbo` process can grant `/proc/<pid>/mem` to G-Earth.
