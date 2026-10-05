CD rip tools (optional fallback)
================================

Music Library Manager v5 primary CD engine on Windows is native CDDA:

  IOCTL_CDROM_READ_TOC  +  IOCTL_CDROM_RAW_READ (TRACK_MODE_TYPE.CDDA)

No Exact Audio Copy install is required. The published win-x64 app talks to the
optical drive through Windows SPTI.

Optional redistributable helpers (if you drop them here they are detected):

  cdparanoia.exe
  cdda2wav.exe
  freaccmd.exe

Search paths at runtime (same pattern as ffmpeg / fpcalc):

  <app>\cdrip\
  <app>\tools\cdrip\
  PATH

Limitations of the native engine
--------------------------------
- Windows only (win-x64 build on Windows 10/11).
- Requires a real (or Windows-visible) CD-ROM class drive with digital audio
  extraction. Some USB bridges are flaky; sector retries + silence fill apply.
- Copy-protected / nonstandard "audio" discs may fail or produce silence.
- Multi-session CD-Extra: audio tracks only; DiscID lead-out adjusted by 11400
  frames per MusicBrainz rules.
- Data-only discs: clear error / no audio tracks.

Bundling note
-------------
We do not ship a third-party CD rip EXE by default (licensing / size). Native
IOCTL is redistributable with the app. Place a helper here only if you need a
fallback on unusual hardware.
