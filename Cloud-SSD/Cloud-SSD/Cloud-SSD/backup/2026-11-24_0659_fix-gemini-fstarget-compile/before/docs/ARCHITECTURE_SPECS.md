# 🏛️ SYSTEM ARCHITECTURE & PROTOCOLS (Cloud-SSD)

> **Overview:** Consolidated technical architecture, system layers, IPC design, and Game-Aware streaming protocols.  
> **Source Documents Consolidated:** ARCHITECTURE.md, GAME_AWARE_PROTOCOL.md.

---

# 📑 TABLE OF CONTENTS
- [1. System Architecture](#1-system-architecture)
- [2. Game Aware Streaming Protocol](#2-game-aware-protocol)

---

# 1. System Architecture
# Cloud-SSD — Architecture

Cloud-SSD runs games on the user's own PC from files stored in cloud storage. It is built on proven components —
**rclone** (cloud backends + VFS sparse cache) and **WinFsp** (Windows user-mode filesystem) — and adds orchestration,
telemetry, prediction, game management and a desktop UI. No kernel driver and no second cache layer.

```
 ┌────────────────────────── CloudSSD.Desktop (WPF, MVVM) ─────────────────────────┐   cpx.exe (CLI)
 │ Dashboard · Cloud Drives · Game Library · Accelerator · Smart Cache · Perf · ...   │   benchmarks
 └───────────────────────────────────────┬────────────────────────────────────────────┘        │
                                         ▼                                                     ▼
 ┌──────────────────────────────── CloudSSD.App: AppHost ──────────────────────────────────────┐
 │ settings · rclone discovery · mounts ⇄ engine wiring · metrics (1 Hz) · disk guard · games     │
 └──────┬──────────────┬────────────────┬───────────────────┬──────────────────┬──────────────────┘
        ▼              ▼                ▼                   ▼                  ▼
  Mount (rclone)   Cache            Streaming            Games              Providers / Diagnostics
  RcloneMountSvc   CacheInspector   StreamingEngine      Library (SQLite)   provider descriptors
  LogTailer+Parser DiskSafetyGuard  predictors           Scanner + PE       redaction, DPAPI,
  RcClient         CacheIndex       PrefetchQueue/Sched  Compatibility      dependency checks,
  GoogleDrive conn CacheMaintenance ProfileStore(SQLite) Launcher (job obj) diagnostic ZIP
                                    GameAware            Hybrid / FullLocal
                                    Simulator            Preparer
        │                                  ▲ AccessEvents (offset, size, hit/miss, wait)
        ▼                                  │
  rclone mount (child process) ── -vv log file ──► LogTailer ──► RcloneLogParser
        │  --vfs-cache-mode full, --buffer-size 0, --rc on 127.0.0.1 (random user/pass via env)
        ▼
  WinFsp drive X:  ⇄  sparse VFS cache on NTFS  ⇄  Google Drive / any rclone backend
```

## Projects

| Project | Responsibility |
|---|---|
| `CloudSSD.Core` | Settings (atomic JSON), paths, units (Mbps vs MB/s), models, metric history, disk P/Invoke (sparse physical size, free space, filesystem), read-attribution registry |
| `CloudSSD.Diagnostics` | Logging (file + in-memory, redacted), `Redactor`, DPAPI `SecretStore`, WinFsp/dependency checks, sanitized diagnostic ZIP |
| `CloudSSD.Mount` | rclone discovery + capability detection (parses `mount --help`/`help flags`), argument-list runner, rc client, private config + encryption, Google Drive OAuth connector (rc `config/create` state machine), mount flag builder, drive letters, `RcloneMountService` (start/ready/health/crash-restart/graceful unmount/adoption), rotation-safe `LogTailer`, `RcloneLogParser` |
| `CloudSSD.Cache` | `CacheInspector` (reads rclone `vfsMeta` range lists + `GetCompressedFileSizeW`), `DiskSafetyGuard` (budget vs. reserve, overrun margin, pressure), `CacheMaintenance` (marker-guarded clearing), `CacheIndex` |
| `CloudSSD.Streaming` | Prediction engine: predictors, accounting, `PrefetchQueue`, `NetworkGovernor`, `SchedulingPolicy`, `PrefetchScheduler`, `MountReadExecutor`, `ProfileStore`, `StreamingEngine`, Game-Aware mode, deterministic simulator + synthetic traces |
| `CloudSSD.Games` | Library, scanner, PE inspector, dependency resolver, compatibility analyzer, Hybrid builder, Full Local installer, launcher (job objects, NTSTATUS decoding, event log), network probe, Optimize & Prepare, pin keeper |
| `CloudSSD.Providers` | Provider descriptors and support levels (Google Drive supported; others via rclone untested; TeraBox unsupported) |
| `CloudSSD.App` | `AppHost` application service and `MetricsCollector` |
| `CloudSSD.Desktop` | WPF UI (MVVM with CommunityToolkit), theme, `Sparkline` chart, dialogs |
| `CloudSSD.Cli` | `cpx` CLI; also rclone's `--password-command` helper for DPAPI-protected config encryption |
| `CloudSSD.SimCloud` | Throttled local HTTP file server for tests/benchmarks (bandwidth, latency, failures, drops, offline) |

## Key data flows

### Mount
1. `AppHost.MountAsync` → `DiskSafetyGuard.Decide` turns the budget into a safe `--vfs-cache-max-size`
   (current cache + free − reserve, minus a 10 % overrun margin, split across drives).
2. `MountFlagBuilder` emits only flags the installed rclone supports; dangerous extra flags (`*command*`, `rc*`,
   `config`, cache/log paths) are refused.
3. rclone starts with a random loopback rc port; rc user/password and the config password are passed **only via
   environment variables**. Std streams are redirected and drained (a kept-alive mount must not pin parent handles).
4. Ready = drive letter visible **and** rc responds. State is persisted (pid, start time, port, DPAPI-protected rc
   password) so a restarted app re-attaches to running mounts instead of starting duplicates.
5. Health check every 5 s; unexpected exit → auto-restart with exponential backoff (max 5 in 10 min); resume from
   sleep re-verifies mounts.
6. Unmount: wait for pending uploads (≤ 60 s) and for read activity to settle (rclone persists range indexes only
   after handles close + 5 s), then `core/quit`, then kill as a last resort.

### Telemetry (no admin, no injection)
rclone `-vv` with microsecond timestamps logs, per read, `_readAt: size, off`, `vfs cache: looking for range … present
true|false`, and `>_readAt: n, err`. The parser pairs them into `AccessEvent`s with offset, length, rclone's own
cache-hit verdict and the wait time; lookup lines arrive *before* the data and give a live "game is waiting on the
network" signal. Prefetch reads are separated from game reads with a range + time-window registry. ETW was evaluated
and rejected: file I/O tracing needs administrator rights and gives no VFS cache verdict.

### Prediction and prefetch (Universal Mode)
- **Blocks** of 1 MiB relative to the game folder are the unit of learning.
- **Predictors** (deterministic, explainable): `StartupPredictor` (blocks read in a *majority* of recorded launches,
  ordered by median first-access time, Laplace-smoothed confidence), `MarkovPredictor` (block→block transitions,
  confidence = count/(occurrences+1)), `SequentialPredictor` (run detection, depth = ~1 s of measured throughput).
- **Accounting** tracks useful vs. wasted prefetch per predictor; predictors below 25 % accuracy after 64 samples are
  disabled automatically (persisted per game).
- **Queue**: P1 > P2 > P3 then confidence then FIFO; dedupe, in-place upgrade, cancellation by owner/tag, TTL for
  speculative items, session byte budget, and a `Dropped` notification for every unfetched exit.
- **Policy**: P0 (game reads) always wins — while the game waits on the network only P1 runs with one stream; P2/P3
  resume after 750 ms and are rate-limited to the background share while the game is loading; nothing runs under
  disk pressure. Concurrency follows the bandwidth-delay product measured by the `NetworkGovernor`.
- **Executor** reads blocks through the mounted drive, so rclone's own sparse cache stores them.

### Optimize & Prepare
Scan folder → capacity check (budget, reserve) → real throughput probe with `rclone cat` (bypasses the cache) →
profile → compatibility + missing DLLs → essentials (or Hybrid build / Full Local check) → startup prefetch within
the available budget → validation, re-checking coverage against **rclone's on-disk range index** after its
handle-caching delay. Readiness is reported honestly; with no profile it says the startup data is unknown.

### Execution modes
- **Cloud Direct**: launch from `X:\…`.
- **Hybrid Optimized**: local folder mirroring the tree; executables, DLLs, configs and files ≤ 2 MiB copied
  (stamp-checked, user-modified local copies are kept), large files become symbolic links into the drive.
- **Full Local**: `rclone copy` (checksummed, resumable) after explicit approval.
The launcher tracks the process tree with a job object (no kill-on-close), decodes NTSTATUS exit codes, collects
Application event-log entries, writes a launch diagnostics JSON, and marks a mode **Verified** only after ≥ 60 s.

### Game-Aware Mode
Optional, per title. A current-user-only named pipe receives JSON telemetry from an authorized plugin; a region map
associates blocks with regions only after repeated, dominant co-occurrence; region entry schedules P1 (current) and
P2 (likely next, heading-weighted) work, all cancellable by tag. See `docs/GAME_AWARE_PROTOCOL.md`.

## Persistence

| Data | Location |
|---|---|
| Settings | `%LOCALAPPDATA%\CloudPlayX\settings.json` (atomic replace; corrupt files are kept aside) |
| Library, profiles, sessions, transitions, predictor stats | `cloudplayx.db` (SQLite, WAL) |
| Cloud logins | `rclone.conf` (rclone-encrypted) + `rclone.conf.key` (DPAPI) |
| Running mounts | `mounts.state.json` (rc password DPAPI-protected) |
| Cache | `<cache root>\m-<drive id>\{vfs,vfsMeta}` + `.cloudplayx-cache` marker |
| Logs / launch diagnostics | `logs\`, `diagnostics\` |

## Security model

Least privilege (`asInvoker`, never admin); rc bound to 127.0.0.1 with random credentials; secrets only in env vars,
DPAPI or HTTP bodies to localhost; redaction on every log line and export; single-instance and data-folder locks
prevent concurrent cache ownership; cache deletion only inside the configured root and only in marked folders;
cloud data is never deleted by cache management; no DRM/anti-cheat interaction; dependencies are checked, not
silently installed; rclone is pinned and checksum-verified.


---

# 2. Game Aware Streaming Protocol
# Game-Aware Mode — telemetry protocol

Game-Aware Mode lets an **authorized** integration (a game's official modding API, a plugin, or the game's own
telemetry) tell Cloud-SSD where the player is, so data for the current and nearby world regions can be cached
before it is needed. Without such an integration Cloud-SSD uses Universal Mode only and never pretends to know the
player's location.

**Status:** the receiving side (pipe server, region learning, prediction, scheduling, cancellation) is implemented
and unit-tested with synthetic telemetry. **No game plugin exists yet**; nothing has been validated with a real game.

## Transport

- Windows named pipe `\\.\pipe\cloudplayx-telemetry-<gameId>` (the game ID is shown in Game settings).
- Created with `PipeOptions.CurrentUserOnly`: only processes of the same Windows user can connect.
- The pipe exists only while the game is running from Cloud-SSD with Game-Aware enabled for that title.
- One client at a time; reconnecting is allowed.

## Messages

UTF-8 JSON, one object per line (max 4096 bytes per line). Unknown fields are ignored; malformed lines are counted
and dropped.

```json
{"type":"position","x":1520.5,"y":12.0,"z":-340.25,"region":"downtown"}
{"type":"region","region":"harbor"}
{"type":"fasttravel","region":"airport"}
{"type":"loading","state":"begin"}
```

| type | meaning |
|---|---|
| `position` | Player position (world units) plus the game's own region identifier. Send at 1–10 Hz. |
| `region` | Region change without coordinates. |
| `fasttravel` | Teleport/fast travel to a region (prefetch for neighbours is cancelled). |
| `loading` | Optional loading-screen markers (currently logged only). |

Region identifiers must come from the game (zone names, cell IDs, map chunks). Cloud-SSD does not infer regions
from coordinates on its own.

## How the data is used

1. **Learning (validated, never guessed):** while the player is in region R, every block the game reads is counted
   for R. A block is associated with R only after it was read during **≥ 2 separate visits** to R and **≥ 70 %** of
   its reads happened in R. Shared data (UI, common textures, global archives) is therefore never attributed to a
   single region, and no assumption is made about archive layout.
2. **Region graph:** transitions between regions are counted; movement heading toward a region's observed centroid
   raises its probability.
3. **Prediction:** on entering R, R's validated blocks are scheduled at P1; regions reached from R with probability
   ≥ 0.3 are scheduled at P2. All region prefetches are tagged and cancelled when the player moves elsewhere; fast
   travel cancels neighbour predictions and prioritizes the destination.
4. All normal safeguards apply: game reads (P0) always win, prefetch respects the cache budget, disk reserve and
   bandwidth share, and accuracy is tracked per predictor ("region").

## Rules for plugin authors

- Use only the game's supported modding/plugin interfaces. Do not inject into games protected by anti-cheat, and do
  not modify executables or bypass DRM. Cloud-SSD will not help circumvent such protections.
- Send coarse data only (position and region). No personal data.

