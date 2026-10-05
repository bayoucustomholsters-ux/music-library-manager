# Music Library Manager v5.1.0 (Phase 5.1)

Windows desktop app to split large album audio files into individually tagged tracks using optional CUE sheets, cover art, plain-text track lists, **MusicBrainz / Cover Art Archive**, **AcoustID** fingerprinting, **Discogs** lookup, optional **ReplayGain**, **Watch Folder**, **Duplicate Finder**, **Fill missing covers**, **batch track convert**, plus a **CD Ripper** and **Apple Lossless (ALAC / M4A)** output.

Built with **.NET 8** and **Avalonia UI**. Cross-compiled for **win-x64**.

## Features

### Phase 1 (core)
- Drag-and-drop or browse album audio: MP3, FLAC, WAV, APE, ALAC/M4A, WMA, AIFF, OGG
- Optional CUE sheet, cover (JPG/PNG), plain-text track list
- **Split Album**: split by CUE, convert (**MP3** 320 CBR default; **FLAC**; **Apple Lossless ALAC** in `.m4a`; or keep original), tag Artist/Album/Year/Title/Track/Genre, embed cover
- Folder layout: `Artist\Album (Year)\01 - Song.ext`
- **Batch mode**: folder of albums, auto-find CUE next to audio, skip already processed, log window
- Progress bar, dark mode default, Settings (bitrate CBR/VBR, patterns, optional delete source)

### Phase 2
- **Same-folder-as-source output** (default **on**)
- **MusicBrainz lookup** + equal-duration split without CUE
- **Cover Art Archive** download
- Settings: Auto-lookup on load; **Preserve tags and cover art from source file** (default on)
- Rate-limited MusicBrainz client (~1 req/sec) with User-Agent `MusicLibraryManager/5.1.0`

### Phase 3 — music-focused
- **Watch Folder mode**: Settings path + settle delay (default ~7s). Start/Stop from the main UI; enabled state persists. Background `FileSystemWatcher` (+ light poll) for new audio/CUE in the watch folder **and one level of subfolders**. When a new album settles, auto-runs the existing AlbumSplitter pipeline with current format / PreserveSourceTags / UseSourceFolderAsOutput. Failures are logged; the UI keeps running.
- **Duplicate Finder**: separate window — scan a folder for likely duplicate tracks by normalized artist+album+title tags, similar file size + duration, optional filename similarity. Copy paths to the main log, open folder, or **quarantine extras** (keeps the first file) into a `DuplicatesQuarantine` subfolder after explicit confirm (no permanent delete by default).
- **Fill missing covers** (v5.1.0): toolbar (and Batch Mode) opens a window — pick an **artist folder** whose immediate children are album folders. Skips albums that already have `cover`/`folder`/`front`/`album` art (jpg/jpeg/png). For the rest, resolves Artist/Album from tags when possible (else folder names), searches MusicBrainz, auto-picks the best/first release, downloads Cover Art Archive front art as `cover.jpg`. Progress + cancel + summary (downloaded / skipped / no match / failed). Respects MusicBrainz ~1.1s rate limit.
- **Convert folder of tracks**: each audio file is its own job (already-split library). Convert FLAC/WAV/etc. → selected format while preserving source tags. Output same folder as source **or** mirror under the output root (respects UseSourceFolderAsOutput). Progress + log.

### Phase 4
- **AcoustID fingerprinting**: Chromaprint `fpcalc` (bundled win-x64 under `fpcalc\`) + your free AcoustID API key (Settings → https://acoustid.org/api). **Identify with AcoustID** fingerprints the current audio (first ~120s), looks up AcoustID, maps hits to MusicBrainz releases, reuses the existing release picker / metadata fill / cover download flow. Clear errors if key missing, fpcalc missing, or no match.
- **Discogs lookup**: optional personal access token + User-Agent in Settings. **Lookup on Discogs** searches by artist+album, shows candidates, on accept fills Artist/Album/Year/Genre and track titles when available. Polite ~1 req/sec rate limit.
- **ReplayGain** (optional, default **off**): when **Apply ReplayGain** is enabled, convert/split/rip runs an ffmpeg `loudnorm` scan pass and writes `REPLAYGAIN_TRACK_GAIN` / `REPLAYGAIN_TRACK_PEAK` via TagLib where supported (MP3/FLAC; **best-effort on M4A/ALAC** — warning logged if unsupported). Failures do not fail the job.

### Phase 5 / 5.1 (this release)
- **Fill missing covers** (5.1.0): batch Cover Art Archive download for album subfolders under an artist folder — see Phase 3 bullet.
- **CD Ripper** (Windows 11): separate window from the main toolbar.
  - Detect optical drives (CD-ROM class); clear message when none found.
  - Read TOC (track count / lengths); compute **MusicBrainz DiscID**; optional DiscID / TOC / name lookup to fill artist/album/year/titles; optional Cover Art Archive cover.
  - Rip selected tracks (or all) to **MP3**, **FLAC**, or **ALAC (.m4a)** via native Windows CDDA sector reads → temp WAV → bundled ffmpeg. Tags applied from MB/manual (CDs have no source tags — **PreserveSourceTags** does not apply).
  - Progress per track + log; cancelable async rip.
  - Output folder: **UseSourceFolderAsOutput does not apply to CDs** — uses `LastOutputFolder` / folder picker; defaults to Music library or `Documents\Music`.
- **Apple Lossless (ALAC)** as a first-class output format everywhere (main picker, Settings, convert, CD ripper): ffmpeg `-c:a alac` → `.m4a`, TagLib text tags + cover.

### Deferred — later
- Full **Media Suite** image/PDF tools (stub / not shipping in 5.1.0).

## How CD ripping works on Windows

1. **Drive list** — `DriveInfo` CD-ROM drives → `\\.\X:` device path.
2. **TOC** — `IOCTL_CDROM_READ_TOC` (MSF) → track offsets + lead-out. CD-Extra data tracks are skipped for DiscID (lead-out adjusted by 11400 frames).
3. **Rip** — `IOCTL_CDROM_RAW_READ` with `TRACK_MODE_TYPE.CDDA` (2352-byte sectors) in small chunks, written as PCM WAV, then encoded with the bundled ffmpeg.
4. **Optional helpers** — if you place `cdparanoia.exe` / `cdda2wav.exe` / `freaccmd.exe` under `cdrip\`, they are detected for future fallback (see `tools/cdrip/README.txt`). Native IOCTL is the default; **Exact Audio Copy is not required**.

### Limitations
- **Windows win-x64 only** for live TOC/rip (Linux CI uses fixtures only).
- USB optical drives vary; unreadable sectors are retried then filled with silence; high failure rate aborts with a clear error.
- **Copy-protected** / nonstandard discs may fail or produce silence.
- Virtual CD drivers work only if Windows reports them as `DriveType.CDRom`.
- No AccurateRip verification in 5.1.0.

## API keys & tools

| Feature | Requirement |
|--------|-------------|
| AcoustID identify | Free API key from https://acoustid.org/api. Bundled `fpcalc.exe`. |
| Discogs lookup | Personal access token + User-Agent. |
| MusicBrainz / CAA / DiscID | No key (polite User-Agent). |
| ReplayGain | Bundled ffmpeg; enable in Settings. |
| CD rip | Windows optical drive + bundled ffmpeg. |

## Windows 11 — Install & Run

### Setup installer
1. Prefer `dist/MusicLibraryManager-Setup-5.1.0.exe`, or reassemble from `dist/setup-parts-v5.1.0/` with `Join-Setup.cmd`.
2. Run the Setup exe (per-user install under `%LOCALAPPDATA%\MusicLibraryManager`).
3. Launch **Music Library Manager** from the Start Menu or Desktop shortcut.

### Portable zip
1. Download `MusicLibraryManager-win-x64.zip` from `dist/` (if published).
2. Unzip anywhere and run **`MusicLibraryManager.exe`**.
3. **FFmpeg**: `ffmpeg\ffmpeg.exe`. **fpcalc**: `fpcalc\fpcalc.exe`. Optional CD helpers: `cdrip\`. Override paths in Settings if needed.

### First CD rip
1. Open **CD Ripper** from the top bar.
2. Insert an audio CD → **Refresh** → select drive → **Read TOC**.
3. Optionally **Lookup DiscID** (or Lookup by name) and **Download cover**.
4. Choose MP3 / FLAC / ALAC and an output folder → **Rip selected**.

### First split / convert
1. Drop or browse a large album file (and optional `.cue` / cover).
2. Confirm **output folder** (auto-set to the album’s folder when the same-folder setting is on).
3. Optionally **Lookup metadata**, **Identify with AcoustID**, **Lookup on Discogs**, and/or **Download cover**.
4. Choose format (MP3 / FLAC / ALAC / keep original) → **Split Album**.
5. **Clear form** (bottom bar) clears the log and, when idle, resets single-album paths/metadata, batch/convert folder paths, progress, and MusicBrainz/Discogs selection caches. Watch folder path and Settings are left alone; while a job is running it only clears the log.

## Building from source (Linux or Windows)

```bash
# Requires .NET 8 SDK
dotnet restore
dotnet test
dotnet publish src/MusicLibraryManager/MusicLibraryManager.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=false \
  -o publish/win-x64
```

Preserve the existing `publish/win-x64/ffmpeg/`, `publish/win-x64/fpcalc/`, and `publish/win-x64/cdrip/` across republishes (publish may wipe the folder — copy them back afterward). Source copies live under `tools/fpcalc/` and `tools/cdrip/`.

NSIS installer (Linux `makensis` or Windows NSIS):

```bash
makensis installer/MusicLibraryManager.nsi
# → dist/MusicLibraryManager-Setup-5.1.0.exe
```

## License

- Application source: **MIT** (see `LICENSE`)
- Bundled FFmpeg binary: **LGPL/GPL** — see `docs/FFMPEG-LICENSE.txt`
- Bundled Chromaprint `fpcalc`: **MIT** — see `tools/fpcalc/README.txt`
- CD rip notes: `tools/cdrip/README.txt` / `docs/CDRIP-NOTES.txt`
- MusicBrainz / Cover Art Archive / AcoustID / Discogs data: their respective licenses/terms; this app is a polite API client only.

## Repository

https://github.com/bayoucustomholsters/music-library-manager
