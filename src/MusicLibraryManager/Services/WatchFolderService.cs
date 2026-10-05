using System.Collections.Concurrent;
using MusicLibraryManager.Models;

namespace MusicLibraryManager.Services;

/// <summary>
/// Watches a folder (and one level of subfolders) for new album audio / CUE files,
/// waits for settle delay, then runs AlbumSplitter with current settings.
/// </summary>
public sealed class WatchFolderService : IDisposable
{
    private readonly Action<string> _log;
    private readonly Func<AppSettings> _getSettings;
    private readonly Func<OutputFormat> _getFormat;
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private readonly ConcurrentDictionary<string, DateTime> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<string> _readyQueue = new();
    private readonly HashSet<string> _processedRecently = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private bool _isRunning;
    private bool _isProcessing;
    private DateTime _startedUtc;

    public bool IsRunning => _isRunning;
    public string? WatchedPath { get; private set; }

    public event Action? StateChanged;

    public WatchFolderService(
        Action<string> log,
        Func<AppSettings> getSettings,
        Func<OutputFormat> getFormat)
    {
        _log = log;
        _getSettings = getSettings;
        _getFormat = getFormat;
    }

    public void Start(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            throw new DirectoryNotFoundException($"Watch folder not found: {folder}");

        Stop();

        WatchedPath = Path.GetFullPath(folder);
        _loopCts = new CancellationTokenSource();
        _startedUtc = DateTime.UtcNow;
        _isRunning = true;

        try
        {
            _watcher = new FileSystemWatcher(WatchedPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                Filter = "*.*"
            };
            _watcher.Created += OnFsEvent;
            _watcher.Changed += OnFsEvent;
            _watcher.Renamed += OnRenamed;
            _watcher.Error += (_, e) => _log($"Watch folder error: {e.GetException().Message}");
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            _log($"FileSystemWatcher unavailable ({ex.Message}); using poll-only mode.");
            _watcher = null;
        }

        _loopTask = Task.Run(() => LoopAsync(_loopCts.Token));
        _log($"Watch folder STARTED: {WatchedPath} (settle {_getSettings().WatchSettleDelaySeconds}s, one-level subfolders)");
        StateChanged?.Invoke();
    }

    public void Stop()
    {
        if (!_isRunning && _watcher == null && _loopCts == null) return;

        _isRunning = false;
        try { if (_watcher != null) _watcher.EnableRaisingEvents = false; } catch { /* ignore */ }
        _watcher?.Dispose();
        _watcher = null;

        try { _loopCts?.Cancel(); } catch { /* ignore */ }
        _loopCts?.Dispose();
        _loopCts = null;
        _loopTask = null;

        _pending.Clear();
        while (_readyQueue.TryDequeue(out _)) { }

        if (!string.IsNullOrEmpty(WatchedPath))
            _log($"Watch folder STOPPED: {WatchedPath}");
        WatchedPath = null;
        StateChanged?.Invoke();
    }

    private void OnRenamed(object sender, RenamedEventArgs e) => ConsiderPath(e.FullPath);

    private void OnFsEvent(object sender, FileSystemEventArgs e) => ConsiderPath(e.FullPath);

    private void ConsiderPath(string path)
    {
        if (!_isRunning || string.IsNullOrWhiteSpace(path)) return;
        if (!IsInWatchScope(path)) return;

        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (!AlbumSplitter.AudioExtensions.Contains(ext) && ext != ".cue")
            return;

        // De-dupe rapid events — mark pending with "last seen" time
        _pending[path] = DateTime.UtcNow;
    }

    private bool IsInWatchScope(string path)
    {
        if (string.IsNullOrEmpty(WatchedPath)) return false;
        try
        {
            var full = Path.GetFullPath(path);
            var root = WatchedPath!;
            if (!full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(Path.GetDirectoryName(full), root, StringComparison.OrdinalIgnoreCase))
                return false;

            // Root file OR exactly one subdirectory deep
            var rel = Path.GetRelativePath(root, full);
            var parts = rel.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);
            return parts.Length <= 2; // file in root, or file in one subfolder
        }
        catch
        {
            return false;
        }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                PollExistingOnce();
                PromoteSettledFiles();
                await ProcessReadyAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log($"Watch loop warning: {ex.Message}");
            }

            try { await Task.Delay(1000, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void PollExistingOnce()
    {
        // Lightweight poll so we catch files even if watcher missed them
        if (string.IsNullOrEmpty(WatchedPath) || !Directory.Exists(WatchedPath)) return;
        try
        {
            foreach (var f in EnumerateWatchFiles(WatchedPath))
            {
                if (!_processedRecently.Contains(f) && !_pending.ContainsKey(f))
                {
                    // Only pick up files written after watch started (backup for missed watcher events).
                    var writeUtc = File.GetLastWriteTimeUtc(f);
                    if (writeUtc >= _startedUtc.AddSeconds(-2))
                        _pending[f] = DateTime.UtcNow;
                }
            }
        }
        catch { /* ignore poll errors */ }
    }

    private IEnumerable<string> EnumerateWatchFiles(string root)
    {
        foreach (var f in Directory.EnumerateFiles(root, "*.*"))
        {
            var ext = Path.GetExtension(f).ToLowerInvariant();
            if (AlbumSplitter.AudioExtensions.Contains(ext) || ext == ".cue")
                yield return f;
        }

        foreach (var sub in Directory.EnumerateDirectories(root))
        {
            string[] files;
            try { files = Directory.GetFiles(sub, "*.*"); }
            catch { continue; }
            foreach (var f in files)
            {
                var ext = Path.GetExtension(f).ToLowerInvariant();
                if (AlbumSplitter.AudioExtensions.Contains(ext) || ext == ".cue")
                    yield return f;
            }
        }
    }

    private void PromoteSettledFiles()
    {
        var settings = _getSettings();
        var settle = TimeSpan.FromSeconds(Math.Clamp(settings.WatchSettleDelaySeconds, 1, 120));
        var now = DateTime.UtcNow;

        foreach (var kv in _pending.ToArray())
        {
            var path = kv.Key;
            if (!File.Exists(path))
            {
                _pending.TryRemove(path, out _);
                continue;
            }

            if (!IsFileStable(path, settle))
            {
                _pending[path] = now; // still changing — reset settle clock
                continue;
            }

            // Stable long enough since last event?
            if (now - kv.Value < settle) continue;

            _pending.TryRemove(path, out _);
            var albumKey = ResolveAlbumKey(path);
            if (albumKey != null && !_processedRecently.Contains(albumKey))
                _readyQueue.Enqueue(albumKey);
        }
    }

    private static bool IsFileStable(string path, TimeSpan settle)
    {
        try
        {
            var fi1 = new FileInfo(path);
            var len1 = fi1.Length;
            var write1 = fi1.LastWriteTimeUtc;
            // Brief check: if last write is older than settle/2, treat as stable enough
            if (DateTime.UtcNow - write1 >= settle)
                return true;
            Thread.Sleep(200);
            fi1.Refresh();
            return fi1.Length == len1 && fi1.LastWriteTimeUtc == write1;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Map a cue/audio path to the primary album audio path to process.</summary>
    private string? ResolveAlbumKey(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".cue")
        {
            var dir = Path.GetDirectoryName(path)!;
            var stem = Path.GetFileNameWithoutExtension(path);
            var match = Directory.GetFiles(dir)
                .FirstOrDefault(f =>
                    AlbumSplitter.AudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())
                    && string.Equals(Path.GetFileNameWithoutExtension(f), stem, StringComparison.OrdinalIgnoreCase));
            if (match != null) return Path.GetFullPath(match);
            // Fall back to largest audio in folder
            var largest = Directory.GetFiles(dir)
                .Where(f => AlbumSplitter.AudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderByDescending(f => new FileInfo(f).Length)
                .FirstOrDefault();
            return largest != null ? Path.GetFullPath(largest) : null;
        }

        if (!AlbumSplitter.IsLikelyAlbumFile(path))
            return null; // skip already-split looking tracks

        return Path.GetFullPath(path);
    }

    private async Task ProcessReadyAsync(CancellationToken ct)
    {
        if (_isProcessing) return;
        if (!_readyQueue.TryDequeue(out var audioPath)) return;

        _isProcessing = true;
        try
        {
            if (_processedRecently.Contains(audioPath))
                return;

            _processedRecently.Add(audioPath);
            // Cap memory of recent set
            if (_processedRecently.Count > 500)
                _processedRecently.Clear();

            var settings = _getSettings();
            var format = _getFormat();

            _log($"Watch: auto-processing {audioPath}");
            var job = AlbumSplitter.DiscoverFromAudio(audioPath);
            if (job == null)
            {
                _log($"Watch: could not discover job for {audioPath}");
                return;
            }

            var outRoot = settings.UseSourceFolderAsOutput
                ? Path.GetDirectoryName(job.AudioPath) ?? settings.LastOutputFolder ?? WatchedPath!
                : (settings.LastOutputFolder ?? WatchedPath!);

            var splitter = new AlbumSplitter(settings, _log, null);
            await splitter.ProcessAlbumAsync(job, outRoot, format, ct).ConfigureAwait(false);
            _log($"Watch: finished ({job.Status}) — {Path.GetFileName(audioPath)}");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log($"Watch: album failed (continuing): {ex.Message}");
        }
        finally
        {
            _isProcessing = false;
        }
    }

    public void Dispose() => Stop();
}
