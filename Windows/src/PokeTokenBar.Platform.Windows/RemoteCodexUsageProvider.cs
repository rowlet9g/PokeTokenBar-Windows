using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PokeTokenBar.Core;

namespace PokeTokenBar.Platform.Windows;

public sealed class RemoteCodexUsageProvider : IUsageProvider
{
    private readonly Func<IReadOnlyList<string>> _hosts;
    private readonly string _cacheRoot;
    private readonly IReadOnlyList<string> _localRoots;
    private readonly IRemoteCodexLogSynchronizer _synchronizer;
    private readonly CodexUsageReader _reader = new();

    public RemoteCodexUsageProvider(
        Func<IReadOnlyList<string>> hosts,
        string cacheRoot,
        IEnumerable<string> localRoots)
        : this(hosts, cacheRoot, localRoots, new RemoteCodexLogSynchronizer())
    {
    }

    internal RemoteCodexUsageProvider(
        Func<IReadOnlyList<string>> hosts,
        string cacheRoot,
        IEnumerable<string> localRoots,
        IRemoteCodexLogSynchronizer synchronizer)
    {
        _hosts = hosts;
        _cacheRoot = Path.GetFullPath(cacheRoot);
        _localRoots = localRoots.Select(Path.GetFullPath).ToArray();
        _synchronizer = synchronizer;
    }

    public string Id => "codex_remote";

    public string DisplayName => "Codex (원격)";

    public bool ReportsCost => false;

    public async Task<ProviderSnapshot?> FetchAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var hosts = _hosts();
        if (hosts.Count == 0)
        {
            return null;
        }

        var since = UsageAggregation.EnrichmentScanStart(now);
        await Task.WhenAll(hosts.Select(host => _synchronizer.SyncAsync(
            host,
            Path.Combine(_cacheRoot, CacheName(host)),
            since,
            cancellationToken))).ConfigureAwait(false);

        return await Task.Run(() =>
        {
            var remoteRoots = hosts.Select(host => Path.Combine(_cacheRoot, CacheName(host))).ToArray();
            var localIds = _reader.ReadEntries(_localRoots, since, cancellationToken)
                .Select(entry => entry.Id)
                .ToHashSet(StringComparer.Ordinal);
            var remote = _reader.ReadEntries(remoteRoots, since, cancellationToken)
                .Where(entry => !localIds.Contains(entry.Id))
                .ToArray();
            return CodexUsageProvider.BuildSnapshot(Id, DisplayName, remote, now, ReportsCost);
        }, cancellationToken).ConfigureAwait(false);
    }

    internal static string CacheName(string host) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(host.ToLowerInvariant())))[..16];
}

internal interface IRemoteCodexLogSynchronizer
{
    Task SyncAsync(string host, string cacheDirectory, DateTimeOffset since, CancellationToken cancellationToken);
}

internal sealed class RemoteCodexLogSynchronizer : IRemoteCodexLogSynchronizer
{
    private const string ManifestName = "manifest.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private const string CollectorScript = """
import base64,json,sys
from pathlib import Path

manifest=json.loads(base64.b64decode(sys.argv[1]).decode('utf-8'))
cutoff=int(sys.argv[2])
home=Path.home()
paths={}
for root in (home/'.codex/sessions',home/'.codex/archived_sessions'):
    if not root.is_dir(): continue
    for path in root.rglob('*.jsonl'):
        try:
            relative=str(path.relative_to(home))
            if path.stat().st_mtime>=cutoff or relative in manifest: paths[relative]=path
        except OSError: pass
for relative in manifest:
    path=home/relative
    if path.is_file(): paths[relative]=path
    else: print(json.dumps({'kind':'deleted','path':relative},separators=(',',':')))
for relative,path in sorted(paths.items()):
    try:
        size=path.stat().st_size
        old=int(manifest.get(relative,{}).get('offset',0))
        reset=old<0 or old>size
        start=0 if reset else old
        position=start
        records=[]
        with path.open('rb') as stream:
            stream.seek(start)
            while True:
                raw=stream.readline()
                if not raw or not raw.endswith(b'\n'): break
                position+=len(raw)
                try: item=json.loads(raw)
                except (UnicodeDecodeError,json.JSONDecodeError): continue
                kind=item.get('type')
                payload=item.get('payload') if isinstance(item.get('payload'),dict) else {}
                selected=None
                if kind=='session_meta':
                    keys=('id','session_id','forked_from_id','parent_thread_id','thread_source','source')
                    selected={'timestamp':item.get('timestamp'),'type':'session_meta','payload':{k:payload[k] for k in keys if k in payload}}
                elif kind=='event_msg' and payload.get('type')=='token_count' and isinstance(payload.get('info'),dict):
                    selected={'timestamp':item.get('timestamp'),'type':'event_msg','payload':{'type':'token_count','info':payload['info']}}
                elif isinstance(payload.get('model'),str):
                    selected={'timestamp':item.get('timestamp'),'type':kind,'payload':{'model':payload['model']}}
                if selected is not None: records.append(selected)
        for record in records:
            print(json.dumps({'kind':'record','path':relative,'record':record},separators=(',',':')))
        print(json.dumps({'kind':'file','path':relative,'offset':position,'reset':reset},separators=(',',':')))
    except OSError as error:
        print(json.dumps({'kind':'error','path':relative,'message':type(error).__name__},separators=(',',':')))
""";

    public async Task SyncAsync(
        string host,
        string cacheDirectory,
        DateTimeOffset since,
        CancellationToken cancellationToken)
    {
        if (!AppSettings.ParseRemoteCodexSshHosts(host).Contains(host, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"안전하지 않은 SSH 별칭입니다: {host}");
        }

        Directory.CreateDirectory(cacheDirectory);
        var manifestPath = Path.Combine(cacheDirectory, ManifestName);
        var manifest = LoadManifest(manifestPath, cacheDirectory);
        var wireManifest = manifest.ToDictionary(
            pair => pair.Key,
            pair => new WireState(pair.Value.Offset),
            StringComparer.Ordinal);
        var manifestArgument = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(wireManifest, JsonOptions)));
        var script = Convert.ToBase64String(Encoding.UTF8.GetBytes(CollectorScript));
        var loader = $"'import base64;exec(base64.b64decode(\"{script}\"))'";

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ssh",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        foreach (var argument in new[]
                 {
                     "-o", "BatchMode=yes", "-o", "ConnectTimeout=6", "-o", "ServerAliveInterval=5",
                     "-o", "ServerAliveCountMax=1", host, "python3", "-c", loader, manifestArgument,
                     since.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
                 })
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        if (!process.Start())
        {
            throw new IOException($"{host}: SSH를 시작하지 못했습니다.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            var standardOutput = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var standardError = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            var output = await standardOutput.ConfigureAwait(false);
            var error = await standardError.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                throw new IOException($"{host}: 원격 Codex 동기화 실패 ({SanitizeError(error)})");
            }

            ApplyOutput(cacheDirectory, manifestPath, manifest, output);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new IOException($"{host}: 원격 Codex 동기화 시간이 초과되었습니다.");
        }
        finally
        {
            if (!process.HasExited)
            {
                TryKill(process);
            }
        }
    }

    internal static void ApplyOutput(
        string cacheDirectory,
        string manifestPath,
        Dictionary<string, CacheState> manifest,
        string output)
    {
        var changes = new Dictionary<string, RemoteChange>(StringComparer.Ordinal);
        foreach (var line in output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var kind = root.GetProperty("kind").GetString();
            var path = root.GetProperty("path").GetString()
                ?? throw new IOException("원격 Codex 응답에 경로가 없습니다.");
            if (!changes.TryGetValue(path, out var change))
            {
                change = new RemoteChange();
                changes[path] = change;
            }

            switch (kind)
            {
                case "record":
                    change.Records.Add(root.GetProperty("record").GetRawText());
                    break;
                case "file":
                    change.Offset = root.GetProperty("offset").GetInt64();
                    change.Reset = root.GetProperty("reset").GetBoolean();
                    change.HasFileState = true;
                    break;
                case "deleted":
                    change.Deleted = true;
                    break;
                case "error":
                    throw new IOException($"원격 Codex 파일을 읽지 못했습니다: {path}");
            }
        }

        foreach (var (sourcePath, change) in changes)
        {
            if (change.Deleted)
            {
                if (manifest.Remove(sourcePath, out var deleted)) TryDelete(Path.Combine(cacheDirectory, deleted.CacheFile));
                continue;
            }

            if (!change.HasFileState)
            {
                throw new IOException($"원격 Codex 응답이 완전하지 않습니다: {sourcePath}");
            }

            var cacheFile = manifest.TryGetValue(sourcePath, out var previous)
                ? previous.CacheFile
                : CacheFileName(sourcePath);
            var destination = Path.Combine(cacheDirectory, cacheFile);
            var temporary = destination + $".tmp-{Guid.NewGuid():N}";
            try
            {
                if (!change.Reset && File.Exists(destination)) File.Copy(destination, temporary);
                using (var writer = new StreamWriter(temporary, append: !change.Reset && File.Exists(temporary), new UTF8Encoding(false)))
                {
                    foreach (var record in change.Records) writer.WriteLine(record);
                }
                File.Move(temporary, destination, overwrite: true);
                manifest[sourcePath] = new CacheState(change.Offset, cacheFile);
            }
            finally
            {
                TryDelete(temporary);
            }
        }

        WriteManifest(manifestPath, manifest);
    }

    private static Dictionary<string, CacheState> LoadManifest(string path, string cacheDirectory)
    {
        try
        {
            var value = JsonSerializer.Deserialize<Dictionary<string, CacheState>>(File.ReadAllText(path), JsonOptions)
                ?? new Dictionary<string, CacheState>(StringComparer.Ordinal);
            foreach (var key in value.Where(pair => !File.Exists(Path.Combine(cacheDirectory, pair.Value.CacheFile)))
                         .Select(pair => pair.Key).ToArray())
            {
                value[key] = value[key] with { Offset = 0 };
            }
            return new Dictionary<string, CacheState>(value, StringComparer.Ordinal);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException or JsonException or IOException)
        {
            return new Dictionary<string, CacheState>(StringComparer.Ordinal);
        }
    }

    private static void WriteManifest(string path, Dictionary<string, CacheState> manifest)
    {
        var temporary = path + $".tmp-{Guid.NewGuid():N}";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    private static string CacheFileName(string sourcePath) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourcePath)))[..24] + ".jsonl";

    private static string SanitizeError(string value)
    {
        var line = value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        return string.IsNullOrWhiteSpace(line) ? "exit code" : line[..Math.Min(line.Length, 180)];
    }

    private static void TryKill(Process process)
    {
        try { process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    internal sealed record CacheState(long Offset, string CacheFile);
    private sealed record WireState(long Offset);
    private sealed class RemoteChange
    {
        public List<string> Records { get; } = [];
        public long Offset { get; set; }
        public bool Reset { get; set; }
        public bool HasFileState { get; set; }
        public bool Deleted { get; set; }
    }
}
