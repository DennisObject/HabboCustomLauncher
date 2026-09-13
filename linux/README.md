# Linux desktop and G-Earth helpers

These are optional wrappers for Linux desktops. The shipped binary still
writes a CRLF `.desktop` file on every start.

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
hidden and only handles `habbo:` URLs. The path unit rewrites that file when
the app overwrites it with CRLF.

## Existing-window focus

`habbo-launcher` focuses an already-open launcher window (Hyprland / Omarchy
`hl.dsp.*` first, then generic `hyprctl`). If the process is alive with no
window, it restarts it.

## G-Earth memory access

Build `gearth-client-access.so` from the G-Earth tree (`tools/linux`) and
install it to `~/.local/lib/gearth-client-access.so`. The wrapper preloads it
so the AIR `Habbo` process can grant `/proc/<pid>/mem` to G-Earth.
