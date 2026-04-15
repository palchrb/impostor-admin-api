# Impostor Admin API Plugin

A drop-in [Impostor](https://github.com/Impostor/Impostor) plugin that exposes a
small HTTP admin API on top of the running Among Us private server. It lets you
inspect the live state of the server (games, players, clients), moderate it
(disconnect clients, kick players, ban IPs), tail the in-game chat, and
subscribe to real-time events via Server-Sent Events (SSE).

The plugin is intentionally self-contained: no external database, no extra
runtime, no ASP.NET Core pipeline. It uses .NET's built-in `HttpListener`, a
JSON-on-disk ban list and an in-memory chat ring buffer.

> **Plugin id:** `me.vibb.impostor.adminapi`
> **Target framework:** `net8.0`
> **Default endpoint:** `http://0.0.0.0:8081/`

---

## Table of contents

- [Features](#features)
- [Installation](#installation)
  - [Download a pre-built DLL](#download-a-pre-built-dll)
  - [Build from source](#build-from-source)
  - [Drop the plugin into your Impostor server](#drop-the-plugin-into-your-impostor-server)
- [Configuration](#configuration)
  - [Example `config.json`](#example-configjson)
  - [Docker / network considerations](#docker--network-considerations)
- [Authentication](#authentication)
- [API reference](#api-reference)
  - [`GET /admin/stats`](#get-adminstats)
  - [`GET /admin/games`](#get-admingames)
  - [`GET /admin/games/{code}`](#get-admingamescode)
  - [`GET /admin/clients`](#get-adminclients)
  - [`POST /admin/clients/{clientId}/disconnect`](#post-adminclientsclientiddisconnect)
  - [`POST /admin/games/{code}/players/{clientId}/kick`](#post-admingamescodeplayersclientidkick)
  - [`GET /admin/bans`](#get-adminbans)
  - [`POST /admin/bans`](#post-adminbans)
  - [`DELETE /admin/bans/{ip}`](#delete-adminbansip)
  - [`GET /admin/chat/recent`](#get-adminchatrecent)
  - [`GET /admin/chat/{code}`](#get-adminchatcode)
  - [`GET /admin/events` (Server-Sent Events)](#get-adminevents-server-sent-events)
- [Event types](#event-types)
- [Persistence](#persistence)
- [Security notes](#security-notes)
- [Development and releases](#development-and-releases)

---

## Features

- **Stats**: uptime, active games, connected clients, public/private split.
- **Games**: list every active game, drill into one to see all players, host,
  game options, IPs.
- **Clients**: list every connected client (in-game or in the lobby browser)
  with platform, game version, language, ping and IP.
- **Moderation**:
  - Disconnect a single client by id.
  - Kick a single player from a specific game.
  - Maintain a persistent IP ban list. Joins from banned IPs are rejected, and
    currently connected clients on a newly banned IP are kicked.
- **Chat log**: in-memory ring buffer of recent chat messages, queryable
  globally or per game.
- **Live event stream**: SSE feed with lobby lifecycle events
  (`game.created`, `game.starting`, `game.started`, `game.ended`,
  `game.destroyed`, `game.privacyChanged`, `game.hostChanged`,
  `game.optionsChanged`), player events (`player.joined`, `player.left`,
  `player.joining.rejected`, `player.murder`, `player.exiled`,
  `player.voted`), meeting events (`meeting.called`, `meeting.started`,
  `meeting.ended`) and `chat` messages. Configurable heartbeat keeps the
  stream alive through reverse proxies.

---

## Installation

### Download a pre-built DLL

Releases are cut by pushing a `v*` tag. The CI workflow builds the plugin and
attaches `Impostor.Plugins.AdminApi.dll` to the matching GitHub release, so you
can grab a stable URL per version:

```sh
wget https://github.com/palchrb/impostor-admin-api/releases/download/v1.0.0/Impostor.Plugins.AdminApi.dll
```

The latest release page is always at
[`/releases/latest`](https://github.com/palchrb/impostor-admin-api/releases/latest).

If you just want the current `main` build without cutting a tag, every push to
`main` runs the workflow and uploads `Impostor.Plugins.AdminApi.dll` as a build
artifact; download it from the run page on the
[Actions tab](https://github.com/palchrb/impostor-admin-api/actions).

### Build from source

Requires the .NET 8 SDK.

```sh
git clone https://github.com/palchrb/impostor-admin-api.git
cd impostor-admin-api
dotnet publish -c Release -o ./publish src/Impostor.Plugins.AdminApi/Impostor.Plugins.AdminApi.csproj
```

The compiled plugin is at `publish/Impostor.Plugins.AdminApi.dll`.

> The project references [`Impostor.Api`](https://www.nuget.org/packages/Impostor.Api)
> from NuGet (default: `1.10.4`). If you target a different Impostor server
> version, edit the `<PackageReference>` in
> `src/Impostor.Plugins.AdminApi/Impostor.Plugins.AdminApi.csproj`.

### Drop the plugin into your Impostor server

Copy `Impostor.Plugins.AdminApi.dll` into your server's `plugins/` folder and
restart Impostor. On startup you should see a log line such as:

```
info: Impostor.Plugins.AdminApi.AdminApiPlugin[0]
      Admin API plugin enabled.
info: Impostor.Plugins.AdminApi.Services.AdminApiHost[0]
      Admin API listening on http://+:8081/
```

For Docker users this typically means mounting your plugin folder, e.g.:

```sh
docker run -d \
  -p 22023:22023/udp \
  -p 127.0.0.1:8081:8081 \
  -v $(pwd)/plugins:/app/plugins \
  -v $(pwd)/libraries:/app/libraries \
  -v $(pwd)/config.json:/app/config.json \
  ghcr.io/impostor/impostor:latest
```

Note the `127.0.0.1:8081:8081` mapping: the API listens on all interfaces inside
the container, but Docker only exposes it on the host's loopback. See
[Docker / network considerations](#docker--network-considerations).

---

## Configuration

The plugin reads its options from the standard Impostor `config.json`, under an
`AdminApi` section. All fields are optional and have sensible defaults.

| Field                 | Type    | Default                          | Description |
|-----------------------|---------|----------------------------------|-------------|
| `Enabled`             | bool    | `true`                           | Master switch. When false the listener never starts. |
| `ListenIp`            | string  | `"0.0.0.0"`                      | Bind address. `0.0.0.0` listens on all interfaces (use the Docker port mapping to limit exposure); `127.0.0.1` restricts to loopback. |
| `ListenPort`          | int     | `8081`                           | TCP port for the HTTP listener. |
| `ApiKey`              | string  | `""`                             | If non-empty, every request must include `X-Admin-Key: <key>`. Empty disables auth - only safe behind a firewall or 127.0.0.1 port mapping. |
| `BanListPath`         | string  | `"libraries/adminapi/bans.json"` | Where to persist the ban list. Set to `""` to keep bans in memory only (cleared on restart). The directory is created automatically. |
| `ChatLogBufferSize`   | int     | `1000`                           | Number of recent chat messages kept in the in-memory ring buffer. |
| `SseHeartbeatSeconds` | int     | `15`                             | How often to emit a `:heartbeat` comment on idle `/admin/events` connections so reverse proxies don't time them out. Set to `0` to disable. |

### Example `config.json`

```json
{
  "Server": {
    "PublicIp": "127.0.0.1",
    "ListenIp": "0.0.0.0",
    "ListenPort": 22023
  },
  "AdminApi": {
    "Enabled": true,
    "ListenIp": "0.0.0.0",
    "ListenPort": 8081,
    "ApiKey": "change-me-to-a-long-random-string",
    "BanListPath": "libraries/adminapi/bans.json",
    "ChatLogBufferSize": 1000,
    "SseHeartbeatSeconds": 15
  }
}
```

### Docker / network considerations

`HttpListener` cannot bind to `0.0.0.0` directly - internally the plugin
translates `ListenIp: "0.0.0.0"` into the wildcard prefix `http://+:8081/`. In
practice this means:

- For host-only access: set `ListenIp: "127.0.0.1"`.
- For Docker: keep `ListenIp: "0.0.0.0"` and control exposure through the port
  mapping, e.g. `-p 127.0.0.1:8081:8081`.
- For LAN access: bind to `0.0.0.0` and **always set an `ApiKey`** plus a
  firewall rule.

---

## Authentication

If `AdminApi.ApiKey` is set, every request must include the matching key in the
`X-Admin-Key` request header. Otherwise the API returns `401 Unauthorized`:

```sh
curl -H "X-Admin-Key: change-me-to-a-long-random-string" \
     http://localhost:8081/admin/stats
```

If `ApiKey` is empty, requests are accepted without authentication. Only use
this mode when access is already restricted at the network layer (loopback bind,
Docker port mapping to `127.0.0.1`, firewall, VPN, etc.).

---

## API reference

All responses are `application/json; charset=utf-8` (except SSE). Property names
use `camelCase`. Errors look like:

```json
{ "error": "Game not found" }
```

### `GET /admin/stats`

Server-wide aggregate counters and uptime.

**Response 200**

```json
{
  "startedAt": "2025-04-14T08:21:14.1234567Z",
  "uptimeSeconds": 4512.4,
  "activeGames": 3,
  "connectedClients": 14,
  "publicGames": 1,
  "privateGames": 2,
  "totalPlayers": 14
}
```

---

### `GET /admin/games`

List every game currently active on the server.

**Response 200**

```json
[
  {
    "code": "ABCDEF",
    "gameId": 1234567,
    "hostName": "host_player",
    "displayName": null,
    "playerCount": 5,
    "maxPlayers": 10,
    "state": "NotStarted",
    "isPublic": true,
    "numImpostors": 2,
    "mapId": 0,
    "languageKeywords": 1,
    "gameMode": "Classic",
    "hostIp": "203.0.113.42",
    "hostClientId": 42
  }
]
```

`state` mirrors `GameStates`: `NotStarted | Started | Ended | Destroyed`.

---

### `GET /admin/games/{code}`

Detail view of a single game, including all players. `{code}` may be the
6-letter or 4-letter game code (case-insensitive) or the integer `gameId`.

**Response 200**

```json
{
  "summary": { "code": "ABCDEF", "...": "..." },
  "players": [
    {
      "clientId": 42,
      "name": "host_player",
      "ip": "203.0.113.42",
      "port": 51324,
      "platform": "StandaloneSteamPC",
      "platformName": "host_player",
      "gameVersion": 50552500,
      "language": "English",
      "chatMode": "FreeChatOrQuickChat",
      "pingMs": 38.2,
      "isHost": true,
      "limbo": "NotLimbo",
      "roleType": "Crewmate",
      "isDead": false,
      "playerId": 0
    }
  ]
}
```

**Errors**: `400 Invalid game code`, `404 Game not found`.

---

### `GET /admin/clients`

List every connected client, in-lobby or in the lobby browser. Useful for
finding the client id of a player you want to disconnect.

**Response 200**

```json
[
  {
    "id": 42,
    "name": "host_player",
    "ip": "203.0.113.42",
    "port": 51324,
    "platform": "StandaloneSteamPC",
    "platformName": "host_player",
    "gameVersion": 50552500,
    "language": "English",
    "chatMode": "FreeChatOrQuickChat",
    "pingMs": 38.2,
    "gameCode": "ABCDEF",
    "inGame": true
  }
]
```

---

### `POST /admin/clients/{clientId}/disconnect`

Disconnect a client from the server entirely (any game they were in
included). Body is ignored.

**Response 200**

```json
{ "ok": true, "clientId": 42 }
```

**Errors**: `404 Client not found`.

> The plugin sends the HTTP response *before* tearing down the in-game
> connection, so the caller always gets confirmation even if the disconnect
> takes a moment to propagate.

---

### `POST /admin/games/{code}/players/{clientId}/kick`

Kick a single player from a specific game. The player stays connected to the
server and may join other games. Body is ignored.

**Response 200**

```json
{ "ok": true, "code": "ABCDEF", "clientId": 42 }
```

**Errors**: `400 Invalid game code`, `404 Game not found`,
`404 Player not found in game`.

---

### `GET /admin/bans`

Return the current ban list.

**Response 200**

```json
[
  {
    "ip": "203.0.113.42",
    "reason": "spamming",
    "createdAt": "2025-04-14T09:00:00Z"
  }
]
```

---

### `POST /admin/bans`

Add a new IP ban. Currently connected clients on the banned IP are
disconnected with `DisconnectReason.Banned`, and any future join attempts from
that IP are rejected with `GameJoinError.Banned`.

**Request body**

```json
{ "ip": "203.0.113.42", "reason": "spamming" }
```

`reason` is optional.

**Response 201** (newly added) **/ 200** (already present)

```json
{ "ok": true, "added": true, "ip": "203.0.113.42" }
```

**Errors**: `400 Invalid JSON body`, `400 Missing 'ip' field`,
`400 Invalid IP address`.

---

### `DELETE /admin/bans/{ip}`

Remove a ban. The IP in the path should be URL-encoded if it contains special
characters (raw IPv4/IPv6 do not).

**Response 200**

```json
{ "ok": true, "ip": "203.0.113.42" }
```

**Response 404** (when the IP wasn't in the list)

```json
{ "ok": false, "ip": "203.0.113.42" }
```

---

### `GET /admin/chat/recent`

Return up to `?limit=N` most recent chat messages (default 100) across every
game.

**Response 200**

```json
[
  {
    "timestamp": "2025-04-14T09:01:13Z",
    "gameCode": "ABCDEF",
    "clientId": 42,
    "playerName": "host_player",
    "ip": "203.0.113.42",
    "message": "lets go"
  }
]
```

---

### `GET /admin/chat/{code}`

Same as `/admin/chat/recent` but filtered to a specific game. `{code}` is the
game code (case-insensitive). Supports `?limit=N`.

---

### `GET /admin/events` (Server-Sent Events)

Open a long-lived `text/event-stream` connection to receive server events in
real time. The first event is always a `hello` event with the subscription id;
afterwards events are pushed as they happen.

```sh
curl -N -H "X-Admin-Key: ..." http://localhost:8081/admin/events
```

```
event: hello
data: {"subscriptionId":"a3f1..."}

event: client.connected
data: {"type":"client.connected","timestamp":"2025-04-14T09:00:00Z","data":{"clientId":42,"name":"host_player","ip":"203.0.113.42","platform":"StandaloneSteamPC","version":50552500}}

event: chat
data: {"type":"chat","timestamp":"2025-04-14T09:01:13Z","data":{"timestamp":"...","gameCode":"ABCDEF","clientId":42,"playerName":"host_player","ip":"203.0.113.42","message":"lets go"}}
```

The internal channel is bounded to 100 events per subscriber with
`DropOldest` semantics, so a slow consumer will lose old events rather than
back-pressure the publisher.

**Heartbeats.** When there is no traffic for `SseHeartbeatSeconds` (default
15 s), the server writes an SSE comment line:

```
: heartbeat

```

Comment lines (those starting with `:`) are [ignored by spec-compliant SSE
clients](https://developer.mozilla.org/en-US/docs/Web/API/Server-sent_events/Using_server-sent_events#event_stream_format)
like the browser `EventSource`, but they keep the TCP connection warm so
reverse proxies (nginx, Cloudflare, Traefik) don't time out idle streams, and
they let the server detect dead clients on write. Set `SseHeartbeatSeconds`
to `0` to disable.

---

## Event types

| `type`                    | When it fires                                                                 |
|---------------------------|-------------------------------------------------------------------------------|
| `hello`                   | Once, on subscription start.                                                  |
| `client.connected`        | A new client connected to the server.                                         |
| `game.created`            | A lobby was created.                                                          |
| `game.destroyed`          | A game ended and was disposed.                                                |
| `game.starting`           | A lobby entered countdown / pre-start phase.                                  |
| `game.started`            | The host pressed start and the round is running.                              |
| `game.ended`              | The game finished (with `reason`).                                            |
| `game.privacyChanged`     | Host toggled public/private.                                                  |
| `game.hostChanged`        | Host migrated to another player (`previousHost*`, `newHost*`).                |
| `game.optionsChanged`     | Game settings changed. `changedBy` is `Host` or `Api`; payload includes the new options snapshot. |
| `player.joined`           | A player joined a game.                                                       |
| `player.left`             | A player left a game.                                                         |
| `player.joining.rejected` | A join attempt was blocked (currently only for banned IPs). Includes `code`, `clientId`, `name`, `ip`, `reason`. |
| `chat`                    | A chat message was sent (also added to the chat log).                         |
| `player.murder`           | A player was murdered (with killer/victim names).                             |
| `meeting.called`          | A player called a meeting. `isEmergency` = true for the emergency button; otherwise `reportedBodyName` is set. |
| `meeting.started`         | A meeting started.                                                            |
| `meeting.ended`           | A meeting ended (with `exiledName` and `isTie`).                              |
| `player.exiled`           | A player was voted out during a meeting.                                      |
| `player.voted`            | A player cast a vote. `voteType`: `Skipped`, `Missed`, `Dead`, `Player` or `HasNotVoted`; `votedForName` is set only when `voteType=Player`. |

---

## Persistence

- **Bans** are persisted as JSON to `BanListPath` (default
  `libraries/adminapi/bans.json`) on every add/remove. The parent directory is
  created automatically. Set `BanListPath` to `""` to keep bans purely
  in-memory.
- **Chat log** is in-memory only and capped at `ChatLogBufferSize`. It is lost
  on restart by design.
- **Stats counters** (`uptimeSeconds`, etc.) reset on restart.

---

## Security notes

- `HttpListener` only speaks plain HTTP. Do **not** expose this directly to the
  internet. Put it behind a reverse proxy with TLS (nginx, Caddy, Traefik) or
  bind to loopback.
- Always set a strong `ApiKey` if the port is reachable from anything other
  than `127.0.0.1`.
- The API can disconnect any player and ban any IP - treat the API key like a
  root password.
- The chat log contains user-visible text; treat it accordingly under your
  data-protection policy.

---

## Development and releases

Local build commands:

```sh
# Restore + build
dotnet build src/Impostor.Plugins.AdminApi/Impostor.Plugins.AdminApi.csproj

# Publish a deployable single DLL
dotnet publish -c Release -o ./publish \
  src/Impostor.Plugins.AdminApi/Impostor.Plugins.AdminApi.csproj
```

CI lives in [`.github/workflows/build.yml`](.github/workflows/build.yml) and
runs in two modes:

- **Push to `main`**: builds the plugin and uploads
  `Impostor.Plugins.AdminApi.dll` as a workflow artifact. Nothing is published
  to Releases.
- **Push of a `v*` tag** (e.g. `v1.0.0`): builds the plugin, then creates the
  matching GitHub release (or updates it) and attaches the DLL as a release
  asset.

Typical release flow:

```sh
# Make sure main is green, then cut a release tag
git tag v1.0.0
git push origin v1.0.0
```

The workflow picks up the tag, builds, and uploads
`Impostor.Plugins.AdminApi.dll` to the `v1.0.0` release.

Pull requests welcome.
