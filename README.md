# HabboCustomLauncher

<img width="482" alt="image" src="https://github.com/user-attachments/assets/86a47002-fe6b-4a37-8b47-b0189526ac05" />
<br><br>

| OS                  | Classic client | App shortcut | App protocol |
|---------------------|----------------|--------------|--------------|
| Windows (7+)        | ✅              | ✅            | ✅            |
| Linux (glibc 2.33+) | ✅              | ✅            | ✅            |
| OSX (10.12+)        | ✅              | ✅            | ✅ (needs write access to /Applications) |

Linux desktop wrappers (CRLF `.desktop` rewrite, existing-window focus, optional G-Earth preload) live in [`linux/`](linux/README.md).

## Install

Download the build for your system from this fork's releases (they include the custom hotel domain setting):

https://github.com/DennisObject/HabboCustomLauncher/releases/latest

| System | File |
|---|---|
| Windows | `HabboCustomLauncher.exe` |
| macOS (Apple Silicon) | `HabboCustomLauncher_OSX_arm64` |
| macOS (Intel) | `HabboCustomLauncher_OSX_x64` |
| Linux x64 / ARM64 | `HabboCustomLauncher_Linux_x64` / `HabboCustomLauncher_Linux_arm64` |

- **Windows:** run `HabboCustomLauncher.exe`. If SmartScreen warns about an unknown publisher, choose *More info* → *Run anyway*.
- **macOS:** the build is not signed. In Terminal, in the folder you downloaded it to:

  ```sh
  chmod +x HabboCustomLauncher_OSX_arm64
  xattr -d com.apple.quarantine HabboCustomLauncher_OSX_arm64
  ./HabboCustomLauncher_OSX_arm64
  ```

  (use `_OSX_x64` on an Intel Mac).
- **Linux:** `chmod +x HabboCustomLauncher_Linux_x64 && ./HabboCustomLauncher_Linux_x64`.

The launcher downloads the official Habbo AIR client itself on first start.

### Play on a custom hotel (for example plusemu.dev)

1. Open **Settings** and set **Hotel domain** to the hotel's domain, e.g. `plusemu.dev`.
2. Press **Open plusemu.dev**. The hotel's website opens in your browser. Sign in if needed.
3. Press **Open AIR client** on the website. The browser hands the launcher a ticket through a `habbo://` link, and the launcher fetches the hotel's client settings, prepares a copy of the client for that hotel and starts it. Your Habbo client folder is not changed.

If the hotel gives you a launcher token, paste it into **Hotel token** instead. The main button then starts the client directly.

Clear **Hotel domain** to go back to Habbo and its login codes.

## Custom hotel domain

Settings has a **Hotel domain** field. Leave it empty, or set a `habbo.*` domain, and the launcher works as before: it waits for a Habbo login code and starts the client with `-server <hotel> -ticket <ticket>`.

Any other domain (for example `plusemu.dev`) switches the launcher to that hotel:

1. The login code step is skipped and the launch button is available right away.
2. The launcher reads `https://<domain>/api/client/air.json` (contract below).
3. It downloads the official AIR client (from `client.url`, or Habbo's `gamedata/clienturls` when the hotel does not set one) into `Habbo Launcher/downloads/hotels/<domain>/<version>`, so Habbo's own client folder is never touched.
4. Before each launch it writes `HabboAir.swf` from the untouched copy `HabboAir.habbo.swf`, with the hotel's endpoints and RSA key.
5. It gets an SSO ticket from the hotel and starts the client with `-server <environment> -ticket <ticket>`.

The patch is needed because the AIR client has no argument for an arbitrary server. `HabboAir.parseArguments` only reads `-server` (the environment id) and `-ticket`. The game host, ports and `url.prefix` for an environment come from the configuration embedded in the SWF (`connection.info.host.<env>`, `connection.info.port.<env>`, `url.prefix.<env>`, ...). The handshake checks the server's Diffie-Hellman values with an RSA public key (exponent `10001`) that is also embedded in the SWF.

**Hotel token** is optional. When set, it is sent as `Authorization: Bearer <token>` to the hotel's ticket endpoint.

### Hotel-side contract

`GET https://<domain>/api/client/air.json`:

```json
{
  "version": 1,
  "name": "PlusEMU",
  "environment": "en",
  "client": { "version": "30", "url": "https://<domain>/client/HabboWin.zip" },
  "game": { "host": "game.<domain>", "ports": "30000" },
  "urls": { "prefix": "https://<domain>", "webApi": "https://<domain>", "pocketApi": "https://<domain>" },
  "rsa": { "modulus": "<hex>" },
  "ticket": { "url": "/api/client/air-ticket" },
  "configuration": { "hotelview.banner.url": "https://<domain>/gamedata/supersecret" }
}
```

| Field | Required | Meaning |
|---|---|---|
| `environment` | no, default `en` | Environment id the client runs as. Must be one the client accepts: `en`, `pt`, `de`, `es`, `fi`, `fr`, `it`, `nl`, `tr`, `s2`. Its embedded endpoints are replaced. |
| `game.host`, `game.ports` | yes | Plain TCP game socket. `ports` is a comma-separated list the client tries in order. |
| `urls.prefix` | no, default `https://<domain>` | `url.prefix`. The client loads `${url.prefix}/gamedata/hashes` and the files it lists (external_variables, external_flash_texts, furnidata, productdata). |
| `urls.webApi`, `urls.pocketApi` | no, default `urls.prefix` | `web.api` and `pocket.api`. |
| `rsa.modulus` | yes for a working handshake | Hex RSA modulus of the hotel's key. The public exponent must be `65537` (`10001` hex), which the client uses. |
| `client.version`, `client.url` | no | AIR client package (the same zip as Habbo's `flash-windows`). Without it the launcher uses Habbo's current client. |
| `ticket.url` | no | Ticket endpoint, absolute or relative to the domain. |
| `configuration` | no | Extra `key: value` lines for the embedded client configuration. |

`POST <ticket.url>` (body `{}`, optional `Authorization: Bearer <hotel token>`) returns:

```json
{ "ticket": "<sso ticket>" }
```

Without a hotel token, the main button opens `https://<domain>/?launcher=air` in the system browser, where the player is already signed in. The website can then hand over a ticket from that session with a link to `habbo://hab?server=<domain>&token=<sso ticket>`. When the launcher is set to that domain, it starts the client with that ticket; links for other domains are ignored. A link copied to the clipboard still works as a fallback.

The launcher registers itself as the `habbo://` handler on every start:

- **Windows:** `HKCU\Software\Classes\habbo`.
- **Linux:** `~/.local/share/applications/HabboCustomLauncher.desktop` with `MimeType=x-scheme-handler/habbo;`, then `update-desktop-database` and `xdg-mime default HabboCustomLauncher.desktop x-scheme-handler/habbo`.
- **macOS:** the `/Applications/HabboCustomLauncher.app` shortcut bundle declares the `habbo` scheme and forwards the link to the launcher. The launcher rebuilds it when it is missing or points at another binary, registers it with `lsregister` and sets it as the default handler.

The site opens in the system browser rather than an embedded view: Avalonia has no built-in cross-platform web view, and the browser already holds the player's session.
