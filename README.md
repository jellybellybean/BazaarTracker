# BazaarTracker

A small Windows app that checks on its own whether any open public lobbies have appeared
on The Bazaar (PTR) server and notifies you about it.
No need to launch the game and refresh the list by hand anymore.

## Download

Ready-to-run build (nothing to install): **[BazaarTracker-portable.zip from the latest release](https://github.com/jellybellybean/BazaarTracker/releases/latest)**.
Unzip it, start Steam, run `BazaarTracker.exe`.

## What it shows

- the list of public lobbies: name, host, taken/max slots, code, time left until it closes;
- how many players are currently in the matchmaking queue (`/queue`);
- a popup notification and a sound when a new lobby appears;
- a notification when the matchmaking queue has more players than at the previous check
  (can be turned off in settings: "Show a notification when players appear in the queue");
- a request log (how long a refresh took, what errors occurred).

### Tray icon

The icon is drawn by the app and shows the current state without opening the window:

| Look | Meaning |
| --- | --- |
| green dot with a number, pulsing | there are open lobbies with free slots (the number is how many) |
| amber dot with a number, pulsing | no lobbies, but there are players in the queue (the number is how many) |
| dark dot "0" | lobbies and queue are empty |
| red "!" | not signed in or no connection to the server |
| gray dot | connecting |

"+" means more than nine. The hover tooltip shows lobbies and queue as text.
The animation can be turned off in settings.

### Popup window

Instead of a system Windows notification, the app shows its own small window near the tray
(in the corner next to the taskbar). It doesn't steal focus, stays for about 10 seconds
(it won't disappear while the mouse is over it) and fades out. Clicking it copies the lobby code
to the clipboard and, if the game isn't running, launches it through Steam
(`steam://rungameid/1617400`, using the branch selected in Steam, i.e. PTR). If the game is already
running or launching is disabled in settings, a click only copies the code (and without a code it
opens the app window); "×" closes the popup. It is visible over the game in windowed and borderless
modes, but not over exclusive fullscreen. You can switch back to a regular Windows notification in settings.

Double-clicking a row copies the lobby code to the clipboard so you can paste it into the game.

## Requirements

- Windows 10/11 x64.
- Steam running, with an account that owns The Bazaar (AppID 1617400).
- Either build: `dist\` (requires the .NET 10 Desktop Runtime) or `dist-portable\`
  (self-contained, nothing to install).

The game itself may be closed: authorization goes through Steam, not through the game client.

## Another account and sharing with a friend

The account is not hard-coded: on every start the app signs in with whichever Steam account is
currently running on the computer. To give the program to a friend:

1. Copy the whole `dist-portable\` folder (`BazaarTracker.exe`, `steam_api64.dll`, `steam_appid.txt`),
   or send the ready-made `BazaarTracker-portable.zip` from the
   [releases page](https://github.com/jellybellybean/BazaarTracker/releases/latest).
2. The friend runs the exe on their machine. On first launch the app asks Steam for a ticket
   and signs in under their account.

Things worth knowing:

- tokens are stored only in the Windows user profile (`%APPDATA%\BazaarTracker\session.json`);
  there are no credentials next to the exe, so they don't travel when you copy the folder;
- the saved session is bound to a SteamID64. If you sign in to Steam under another account,
  the app notices and renews the session automatically;
- you can switch manually via the tray icon menu → "Switch account";
- the friend's account must own The Bazaar, otherwise Steam won't issue a ticket
  (the app will show a sign-in error rather than silently working under someone else's account).

## Building

```powershell
# from the repository root
dotnet build .\src\BazaarTracker\BazaarTracker.csproj -c Release

# ready-made set in dist\ (requires .NET 10 installed)
.\build.ps1

# self-contained build in dist-portable\ (nothing to install)
.\build.ps1 -SelfContained
```

Run: `dist\BazaarTracker.exe` (or `src\BazaarTracker\bin\Release\net10.0-windows\BazaarTracker.exe`).

## How it works

The Bazaar client talks to two HTTP APIs:

| What | Address |
| --- | --- |
| account, sign-in | `https://temponet-core-ptr-app-srvce.azurewebsites.net` |
| lobbies, queue | `https://tournament-ptr-app.orangestone-41b95ee4.westus2.azurecontainerapps.io` |

Flow:

1. The app initializes Steamworks with the game's AppID and asks Steam for a web API ticket
   (`ISteamUser::GetAuthTicketForWebApi`), exactly as the game itself does.
2. `POST /api/auth/login/silent` with the ticket → JWT (`accessToken`) and `refreshToken`.
3. `GET /api/PlayerProfiles/me` → `accountId` and nickname (needed for the `aid`/`uid` headers).
4. Every N seconds: `GET /lobbies?limit=50` and `GET /queue` with an `Authorization: Bearer …` header.
5. When the access token expires or the server answers `401`, it is renewed via
   `POST /api/auth/refreshtokens`. A new Steam sign-in happens only if there is no refresh token
   or the server explicitly rejects it (HTTP `400`/`401`).

### Being gentle with the backend

- **Refresh token first.** A `401` never triggers a Steam sign-in directly; the refresh token is tried first.
- **Tokens are never dropped on hiccups.** Timeouts, network errors, `429` and `5xx` keep the saved tokens
  and simply retry later. Only `400`/`401` from the refresh endpoint discards the refresh token.
- **`403` stops everything.** No refresh, no re-sign-in, no automatic retries: the error is shown and
  polling is paused until you press Refresh.
- **Exponential backoff with jitter** after every failed poll: the delay doubles (check interval × 2, 4, 8, …),
  is randomized within its upper half, never goes below the normal interval and is capped at 5 minutes.
  A success returns to the normal interval. A `Retry-After` header (seconds or HTTP date) is honoured
  and is never undercut (limited to 1 hour).
- A problem on your own computer (e.g. Steam is not running) does not count as a server failure and is
  retried at the normal interval.

The account password is never asked for or stored: `%APPDATA%\BazaarTracker\session.json`
holds only the tokens issued by the server, and `settings.json` holds the polling interval,
API addresses and notification flags. Both files can be deleted at any time; the app will
simply ask Steam for a ticket again.

## Settings

The "Settings" button:

- check interval (5–300 seconds, default 15);
- notifications and sound;
- minimize to tray instead of closing;
- start with Windows and/or start minimized to the tray;
- API addresses (in case Tempo changes the domains or you need a prod server).

Hotkeys: F5 — check now, Esc — minimize to tray.
The `--tray` flag hides the window to the tray right at startup (used by autostart).

## Diagnostics

```powershell
.\BazaarTracker.exe --selftest
```

Checks sign-in, makes one lobby request and prints the result to the console. Handy if it
"finds nothing". The app's logs are shown in the "Log" window.

```powershell
.\BazaarTracker.exe --selftest-switch
```

The same, plus an account switch check (session reset and sign-in through Steam again).

```powershell
.\BazaarTracker.exe --selftest-http
.\BazaarTracker.exe --selftest-backoff
```

Offline checks that need neither Steam nor the real server: the first runs the sign-in / retry rules
(401, 403, 429, 5xx, timeouts, `Retry-After`) against a fake local HTTP server, the second checks the
backoff delays. They use a temporary data folder and never touch your saved session.

## Layout

```
src/BazaarTracker/     source code (WinForms, .NET 10)
vendor/                com.rlabrecque.steamworks.net.dll and steam_api64.dll from the installed game (not in the repo)
dist/                  built BazaarTracker.exe (steam_api64.dll and steam_appid.txt must sit next to it)
dist-portable/         the same, with the .NET runtime bundled, so it can be passed to a friend
build.ps1              builds dist\ (or dist-portable\ with -SelfContained)
```

The files in `vendor/` come from the The Bazaar client (`TheBazaar_Data/Managed` and
`TheBazaar_Data/Plugins/x86_64`). They are not included in this repository; copy them from your own
game install into `vendor/` before building. After a major game update you can copy them again from the same place.

## Limitations

- This is not an automatic lobby join: the app only watches the list.
- The API addresses are hard-coded as of client `1.0.13122-ptr`; if they change, fix them in settings.
- The server decides which lobbies to show; private lobbies don't appear in the list.
