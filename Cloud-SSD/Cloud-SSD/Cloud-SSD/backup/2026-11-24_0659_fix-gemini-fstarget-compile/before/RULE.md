# RULE.md — Central AI Coordination, Project Rules & Backup System

> **Mandatory Notice for All AI Agents:**  
> This file is the primary rulebook for every AI coding agent working in this repository (Antigravity, Codex, Claude Code, Gemini, and future agents).  
> **You MUST read and adhere strictly to these rules before reading, modifying, or creating any files.**  
> No AI agent may ignore, bypass, or override these rules.

---

## 1. Project Overview & Locations

* **Workspace Root:** `C:\Users\Roshan\Downloads\Cloud-Drive`
* **Git Repository Root:** `C:\Users\Roshan\Downloads\Cloud-Drive\Cloud-Drive` (Branches: `main` = aaj ka branch, aur `feature/multi-device` = dono zinda hain; push hamesha dono par hota hai)
* **Active Codebase & Solution:** `C:\Users\Roshan\Downloads\Cloud-Drive\Cloud-Drive\Cloud-SSD\Cloud-SSD\Cloud-SSD`
* **Solution File:** `CloudSSD.slnx` (.NET 10 WPF + Windows Service + WinFsp + rclone)
* **Mobile App:** `mobile-android/` (Android APK + WebView JS)
* **Central Documentation Directory:** `docs/` (Inside the active codebase directory)
* **Backup Tool:** `tools\backup\backup.ps1`

---

## 2. Mandatory Pre-Work Inspection (Read Before Touch)

Before modifying any file, every AI agent must execute this sequence:

1. **Read `RULE.md`** (this file).
2. **Inspect Active Task Lock:** Check `docs/CURRENT_TASK.json` and `docs/CHALU_KAAM.md`. If another agent has an active incomplete task, stop and reconcile.
3. **Read Work History:** Read your tool's history file (`docs/antigravity.md`, `docs/codex.md`, `docs/CLAUDE.md`, or `docs/GEMINI.md`) and recent entries from other agents to understand context.
4. **Inspect Git Status:** Run `git status --short` from Git root (`Cloud-Drive`). Never overwrite uncommitted or in-progress work without understanding why it exists.
5. **Inspect Target Files & Existing Tests:** Understand the existing implementation, dependencies, and reasons behind existing code before planning any changes.
6. **Check .NET Runtime:** The .NET 10 SDK is located at:
   ```powershell
   $dn = if (Get-Command dotnet -ErrorAction SilentlyContinue) { (Get-Command dotnet).Source } else { "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" }
   ```

---

## 3. Multi-Agent Concurrency & Conflict Prevention

Multiple AI agents (Antigravity, Claude, Codex, Gemini) collaborate on this project:

1. **State Lock (`docs/CURRENT_TASK.json`):**
   - Whenever an AI starts a task, it must record the active task status:
     ```json
     {
       "active": true,
       "taskId": "TASK-XXX",
       "agent": "Antigravity",
       "startTime": "2026-11-14T20:00:00",
       "targetFiles": ["src/CloudSSD.Desktop/MainWindow.xaml.cs"],
       "status": "IN_PROGRESS"
     }
     ```
   - On completion, `active` is set to `false` and status is set to `COMPLETED` / `APPROVED`.
2. **Human-Readable Live Handoff (`docs/CHALU_KAAM.md`):**
   - Keep the "Abhi ka haal" section updated after each significant milestone.
3. **Conflict Detection:**
   - If a target file has been modified by another agent or has an uncompleted backup in `backup/`, do NOT overwrite it blindly. Re-read the file, resolve conflicts, or wait.

---

## 4. Documentation & AI Tool Work Histories (`docs/`)

All AI work histories are organized in `docs/`:
* `docs/antigravity.md` — Antigravity (Google DeepMind) task log
* `docs/codex.md` — OpenAI Codex / ChatGPT task log
* `docs/CLAUDE.md` — Anthropic Claude Code task log
* `docs/GEMINI.md` — Google Gemini task log
* `docs/CHALU_KAAM.md` — Live task state & handover across sessions

### Required Log Schema for Every Completed Task:
```markdown
### [Task ID] Task Title
- **Date & Time:** YYYY-MM-DD HH:mm
- **Agent & Model:** <AgentName> (<Model>)
- **Prompt:** <Original User Prompt>
- **Files Inspected:** <List of files>
- **Files Created / Modified:** <List of files>
- **Backup Folder:** `backup/<folder_name>`
- **Validation Result:** APPROVED / FAILED_VALIDATION (X tests passed, 0 warnings)
- **Summary of Changes:** <What was changed and why>
- **Next Steps / Remaining Work:** <If any>
```

---

## 5. Automatic Versioned Backup System (`tools/backup/backup.ps1`)

**CRITICAL RULE:** Never modify, move, or delete any project file before creating a backup.

### 5.1 Starting a Backup (Pre-Edit Snapshot)
Before touching any code or files:
```powershell
powershell -ExecutionPolicy Bypass -File tools\backup\backup.ps1 start -Name "<task-slug>" -Agent "<AgentName>" -TaskId "<TaskID>"
```
* Creates `backup/<timestamp>_<task-slug>/before/` containing complete source copies of affected files before modification.
* Preserves original relative folder hierarchy and file extensions.
* Excludes build artifacts (`bin`, `obj`, `dist`, `.git`, `.work`, `releases`, and files > 2MB) to prevent disk bloat.
* Updates `docs/CURRENT_TASK.json` with active lock.

### 5.2 Completing a Backup & Calculating Diffs
After files are modified:
```powershell
powershell -ExecutionPolicy Bypass -File tools\backup\backup.ps1 complete -Folder "<timestamp>_<task-slug>" [-Files "file1,file2"]
```
* Compares SHA256 hashes against `before/`.
* Stores final copies of all `NEW` and `CHANGED` files in `complete/files/`.
* Writes `complete/CHANGED-FILES.txt` and updates task status.

### 5.3 Immutability
* Historical backups are permanent.
* **NEVER** overwrite, rename, or delete existing folders inside `backup/`.

---

## 6. Automated Validation & Version Approval

A task is never complete until validated with actual execution.

### 6.1 Validation Command
```powershell
powershell -ExecutionPolicy Bypass -File tools\backup\backup.ps1 validate -Folder "<timestamp>_<task-slug>"
```
* Executes:
  1. `$dn build CloudSSD.slnx` (PC Desktop App & Services)
  2. `$dn test tests\CloudSSD.Tests` (C# Unit Tests)
  3. `node --test tests/mobile-js/*.test.js` (if mobile files changed)
* **Status Tags:**
  * `APPROVED`: All required builds and tests pass (0 errors, 0 warnings).
  * `FAILED_VALIDATION`: Any compilation, syntax, or test failure occurs.
  * `PARTIALLY_VALIDATED`: Some checks passed, but full environment check unavailable.
  * `UNVERIFIED`: Tests were not executed.

---

## 7. Restore & Rollback System

To revert changes back to any exact historical version:

```powershell
powershell -ExecutionPolicy Bypass -File tools\backup\backup.ps1 restore -Folder "<timestamp>_<task-slug>" [-Files "file1,file2"]
```
**Safety Protocol during Restore:**
1. Automatically takes a `before-restore` safety snapshot of current files.
2. Restores original files from `before/` over the project.
3. Deletes any `NEW` files created by that task.
4. Preserves all historical backups in `backup/`.
5. Logs the restoration event in `docs/RESTORE_LOG.md`.

---

## 8. Git & GitHub Upload Rules

1. **No Unrequested Pushes:** Never execute `git push` unless the user explicitly commands it in their prompt.
2. **Secret & Credential Protection:**
   - NEVER stage or push `client_secret*.json`, `installer\google-client.json`, `mobile-android\google-oauth.json`, or any API keys.
   - Run the staged secret scanner before committing:
     ```powershell
     node .work\scan-staged.js roshan rosha I2221
     ```
3. **Commit Integrity:** Check git status with `git status --short`. Only commit the files related to the specific task.

---

## 9. 18 Strict Rules — Must Never Violate

1. **Never modify an existing file before creating a complete pre-edit backup.**
2. **Never overwrite or delete any historical backup in `backup/`.**
3. **Never delete historical versions automatically.**
4. **Never modify unrelated project files outside your task scope.**
5. **Never touch or modify `src\CloudSSD.Desktop\App.xaml.cs` (Critical Core File).**
6. **Never reorganize the entire project or change folder structures unnecessarily.**
7. **Never create duplicate AI work-history files for the same tool.**
8. **Never ignore `RULE.md`.**
9. **Never ignore relevant previous AI work or overwrite another agent's active work.**
10. **Never claim a test passed without actually executing it.**
11. **Never mark an unverified version as `APPROVED`.**
12. **Never restore an older version without backing up the current state first.**
13. **Never push to GitHub without explicit user instruction.**
14. **Never commit secrets, tokens, credentials, or private keys to Git.**
15. **Never push files larger than 100MB to GitHub.**
16. **Never report an incomplete task as complete.**
17. **Never hide a backup failure or continue editing if a backup fails.**
18. **Never sacrifice existing project data or delete documentation.**

---

## 10. Speed Lock (the user's rule, 17 Nov 2026)

The user reported "it is fixed, then it is slow again by itself" several times, and asked for the speed to be **locked**
("isko lock ya kuch kar sakte hai, baad me late open hona shuru kar dega"). So speed is a requirement like a passing test:
**a change that makes any number below worse is not shipped**, whatever else it improves.

### 10.1 What makes the drive fast (do not switch off, replace or "simplify" without measuring)
| Piece | Where | What it gives |
|---|---|---|
| The user's own Google key (all accounts) | PC app → Cloud Drives; `cpx key` | Google no longer refuses requests (403). Never move an account back to rclone's shared key. |
| Fast drive (hybrid) | `src/CloudSSD.App/Hybrid/*`, `Settings.Hybrid.Enabled = true` on the user's PC | New files and folders are written to the PC first, uploaded behind. |
| Preparing opened folders | `src/CloudSSD.App/ArchiveWarmer.cs`, `ExplorerFolderWatcher.cs` | The files of a folder open in File Explorer are fetched ahead. |
| Mount flags | `src/CloudSSD.Mount/MountPlanning.cs` (`--vfs-cache-mode full`, chunk size 4Mi, limit `off`, `--dir-cache-time 72h`, `--vfs-refresh`, `--links`) | Each was measured; the reasons are in the comments. |
| Speed check | `src/CloudSSD.App/SpeedGuard.cs` | Measures at start, every 3 hours and on request; raises a note when something that was fine is not. Do not weaken its limits to make a problem "pass". |

### 10.2 The numbers (the user's PC, 9–10 MB/s line, measured 17 Nov 2026)
| Action | Before | Locked value |
|---|---|---|
| Install-like write, 853 files / 143 folders / 970 MB | 42 min (shared key), 13 min (own key, straight to the cloud drive) | **about 5 s** on the fast drive |
| First open of a file in a folder that is open in File Explorer | 1.7 – 15 s | **under 0.3 s** once the folder is prepared (a few seconds after it is opened) |
| Second open of a file | 5 ms | 5 – 40 ms |
| Opening a folder | 0.7 s first time | under 1 s first time, about 10 ms after |
| "Each Account" drive after the app starts | 40 – 84 s | about 5 s |
| Google refusals (403) | 1,290 in 26 min | 0 |

### 10.3 Before and after touching any of the pieces in 10.1
1. Read the last report: `%LOCALAPPDATA%\Cloud-SSD\speed-check.json` (or PC app → Cloud Drives → "Check the speed now").
2. After your change is installed, run the same check again and, when the change touches reading or writing, also
   `.work\speed\fast-live-test.ps1 -Fast <letter> -Cloud <letter>`, `.work\speed\warm-live-test.ps1 -Cloud <letter> -Fast <letter>`
   and `.work\speed\first-open.ps1 -Cloud <letter>`. Put both sets of numbers into your task log.
3. A check that says "problem", or a number worse than 10.2, means: undo the change (restore from your backup's `before/`) and say so.
   Judge by the measured times and by request counts, not by "it felt fine".
4. Never disconnect a cloud drive while a program runs from it or while uploads are unfinished (17 Nov 2026: both the
   program and rclone got stuck in Windows until a restart, and the drive came back on another letter).
5. Never restart the PC, and never switch the fast drive off, without the user's yes.
