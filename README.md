# Sweeft

> *Sweep the cruft.* — a **Jeffersoft** tool

[![CI](https://github.com/jeffaristi92/Sweeft/actions/workflows/ci.yml/badge.svg)](https://github.com/jeffaristi92/Sweeft/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/)

Cross-platform tool (.NET 8) that recursively scans a folder to **free up disk space**.
It detects:

1. **Regenerable folders** — build artifacts, dependencies and caches
   (`node_modules`, `bin`, `obj`, `.vs`, `target`, `.gradle`, `__pycache__`,
   `dist`, `build`, Python virtual environments, etc.).
2. **Old, heavy files** — files above a minimum size and not modified for more
   than N days, so **you decide** what to delete.

It shows a report sorted by size (largest first) and, after your confirmation,
deletes the chosen items by **sending them to the Recycle Bin** (recoverable by
default) or permanently.

## Install

### Package managers (recommended)

```bash
# macOS / Linux — Homebrew
brew install jeffaristi92/tap/sweeft

# Windows — Scoop
scoop bucket add sweeft https://github.com/jeffaristi92/scoop-bucket
scoop install sweeft

# Any OS with the .NET SDK — global tool
dotnet tool install -g Sweeft
```

> **Windows & antivirus.** The native binaries (direct download and Scoop) are
> **not code-signed yet**, so some antivirus engines (e.g. McAfee) may raise a
> false-positive and quarantine `sweeft.exe`. Until code signing is in place, on
> Windows the most reliable install is **`dotnet tool install -g Sweeft`** — it
> runs as managed code through the signed `dotnet` host and isn't flagged.
> (Code signing via SignPath/Azure Trusted Signing is on the roadmap.)

### Direct download

Grab a **single native binary** (no .NET runtime required) from the
[latest release](https://github.com/jeffaristi92/Sweeft/releases/latest):

| Platform            | Asset                              |
|---------------------|------------------------------------|
| Windows (x64)       | `sweeft-<version>-win-x64.zip`     |
| Linux (x64)         | `sweeft-<version>-linux-x64.tar.gz`|
| macOS (Apple silicon)| `sweeft-<version>-osx-arm64.tar.gz`|
| macOS (Intel)       | `sweeft-<version>-osx-x64.tar.gz`  |

Unpack and run `sweeft` (add it to your `PATH` to use it anywhere):

```bash
# Linux / macOS
tar -xzf sweeft-*.tar.gz && chmod +x sweeft && ./sweeft --version
```

The binaries are built with **NativeAOT** — they start instantly and need no
installed runtime. From source, `dotnet run --project src/Sweeft.Console` works too.

## Architecture

The logic lives in `Core` and is reused by both the console and the GUI:

| Project          | Responsibility                                           |
|------------------|----------------------------------------------------------|
| `Sweeft.Core`    | Scan engine, models and deletion. No UI dependency.      |
| `Sweeft.Console` | Command-line interface (report + confirmation).          |
| `Sweeft.Gui`     | WPF graphical interface (MVVM): type and item selection. |

Key Core pieces: `FolderScanner` (scan + repo tracking), `KnownPatterns` (folder
catalog with toggleable categories), `GitService` (Git repo state), `Cleaner`
(safe deletion to the Recycle Bin via the native Windows API),
`ScanOptions` / `ScanResult` / `Finding` (models).

## Graphical interface (WPF)

```bash
dotnet run --project src/Sweeft.Gui
```

Flow: pick the folder, choose **which types to detect** (e.g. turn off `.vs`),
tune the large/old file filter, and click **Scan**. Results appear in a grid with
checkboxes; you select exactly what to clean and confirm.

GUI features:

- **Fully parameterizable**: thresholds, types to detect, folders to exclude and
  delete mode. You can **add custom types** (name + description) from the panel.
- **Stale-project filter**: optionally clean only regenerable folders of projects
  you haven't touched in a while (e.g. `90d`), so active work is never flagged.
- **Type selection before scanning**: every known folder (`node_modules`, `bin`,
  `obj`, `.vs`, …) can be toggled. Some ambiguous ones (`env`, `vendor`,
  `packages`) are off by default.
- **Git repository detection**: each item shows whether it is inside a repo and
  whether that repo has **uncommitted changes** (⚠ orange). Those rows are
  highlighted and **not** pre-selected, to avoid deleting work in progress by
  mistake.
- **Cautious pre-selection**: only regenerable folders in clean repos or outside
  repos are checked. Large files and items in "dirty" repos stay unchecked.
- **Deletion progress**: a determinate bar (X/N) and per-item status while cleaning.
- **Remembered preferences**: saves the configuration on scan and on close (and
  with the «Save configuration» button).

## Build

```bash
dotnet build Sweeft.slnx -c Release
```

## CLI usage

```bash
sweeft <path> [options]
```

Examples:

```bash
# Report + delete confirmation (to Recycle Bin)
dotnet run --project src/Sweeft.Console -- C:\Projects

# Report only, delete nothing
dotnet run --project src/Sweeft.Console -- C:\Projects --report-only

# Tune the "old and large file" thresholds
dotnet run --project src/Sweeft.Console -- C:\Projects --min-size 500MB --min-age 365
```

### CLI options

**Scan**

| Option                | Description                                                        |
|-----------------------|--------------------------------------------------------------------|
| `-p, --path <path>`   | Root folder (or first positional argument).                        |
| `-s, --min-size <val>`| Minimum file size (e.g. `100MB`, `1.5GB`).                         |
| `-a, --min-age <days>`| Minimum file age in days.                                          |
| `--only-folders`      | Analyze folders only; skip files.                                  |
| `--with-files`        | Force file analysis.                                               |
| `--stale <window>`    | Only clean regenerable folders of projects idle ≥ this long (e.g. `90d`, `6mo`). |
| `-g, --global`        | Scan **global** package-manager caches (npm, NuGet, pip, Gradle, Cargo, Go…) instead of a folder. |
| `-t, --types <list>`  | Detect **only** these types. E.g. `node_modules,bin,obj`.          |
| `-x, --exclude <list>`| Folders to skip entirely during traversal.                         |
| `--custom <spec>`     | Add a custom type: `name\|Category\|Description` (repeatable).      |
| `--git` / `--no-git`  | Enable/disable Git repository state detection.                     |

**Output**

| Option                | Description                                                        |
|-----------------------|--------------------------------------------------------------------|
| `--list-types`        | List the catalog of detectable types and exit.                     |
| `--json`              | Print the result as JSON (for scripting/`jq`); does not delete.     |
| `--report-only`       | Only show the report; never delete.                                |

**Deletion**

| Option                | Description                                                        |
|-----------------------|--------------------------------------------------------------------|
| `-y, --yes`           | Do not ask; select **everything** for deletion.                    |
| `--recycle`           | Send to the Recycle Bin (recoverable).                             |
| `--permanent`         | Permanent, irreversible deletion.                                  |
| `--force`             | Required to combine `--yes` with `--permanent` (safety guard).     |

**Configuration (shared with the GUI)**

| Option                | Description                                                        |
|-----------------------|--------------------------------------------------------------------|
| `--save-config`       | Save the parameters used as the defaults.                          |
| `--config <path>`     | Use a specific configuration file.                                 |
| `--no-config`         | Ignore the saved configuration.                                    |
| `-h, --help`          | Help.                                                              |
| `-v, --version`       | Show the version and exit.                                         |

### Persistent configuration

Both the GUI and the CLI read and write the same configuration at:

```
%APPDATA%\Sweeft\config.json
```

It remembers: last folder, thresholds, enabled types, **custom types**, excluded
folders and the delete mode. The GUI saves it automatically on scan and on close;
the CLI does so with `--save-config`.

Advanced CLI examples:

```bash
# See all types and which are enabled
dotnet run --project src/Sweeft.Console -- C:\Projects --list-types

# Only certain types, no Git, JSON output to process with jq
dotnet run --project src/Sweeft.Console -- C:\Projects --types node_modules,bin,obj --no-git --json

# Add a custom type and save it as a preference
dotnet run --project src/Sweeft.Console -- C:\Projects --custom "logs|Other|Old logs" --save-config

# Unattended deletion to the Recycle Bin
dotnet run --project src/Sweeft.Console -- C:\Projects -y --recycle
```

## Safety

- By default it sends items to the **Recycle Bin** (recoverable).
- Unattended permanent deletion (`--yes --permanent`) is refused unless `--force`
  is also given.
- It does not descend into detected regenerable folders (reported as a block).
- It ignores symbolic links and junctions (reparse points) to avoid cycles and
  double counting.
- Access errors are tolerated: they are logged as warnings and the scan continues.

## License

Released under the [MIT License](LICENSE) © 2026 Yeferson Guarin (Jeffersoft).

### Hardening for untrusted repositories

Because the scanner runs `git` inside repositories it discovers (which may be
attacker-controlled), `GitService`:

- resolves the **absolute path** of `git` from `PATH` and never runs a bare name
  (prevents picking up a malicious `git` from the current directory);
- runs `git status` with `-c core.fsmonitor=`, ignores system config
  (`GIT_CONFIG_NOSYSTEM`), disables credential prompts and optional locks — so a
  hostile repo's config cannot execute commands during the scan;
- drains both stdout and stderr to avoid a process deadlock on chatty output.
