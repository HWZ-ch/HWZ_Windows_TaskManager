using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HWZ.TaskManager.Helpers;

namespace HWZ.TaskManager.Services;

public enum UpdateCheckStatus
{
    UpToDate,
    UpdateAvailable,
    Failed
}

public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    UpdateManifest? Manifest,
    string Message);

public sealed class UpdateManifest
{
    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("downloadUrl")]
    public string DownloadUrl { get; init; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string Sha256 { get; init; } = string.Empty;

    [JsonPropertyName("releaseNotes")]
    public string ReleaseNotes { get; init; } = string.Empty;

    [JsonPropertyName("releasePageUrl")]
    public string ReleasePageUrl { get; init; } = string.Empty;

    [JsonPropertyName("publishedAt")]
    public string PublishedAt { get; init; } = string.Empty;
}

/// <summary>
/// Reads the hidden version manifest from the repository, falls back to the public
/// GitHub Releases API, downloads the portable executable, and launches a tiny
/// PowerShell hand-off that replaces the running file after the app exits.
/// </summary>
public sealed class UpdateService : IDisposable
{
    private const string Owner = "HWZ-ch";
    private const string Repository = "HWZ_Windows_TaskManager";
    private const string Branch = "main";
    private const string ManifestPath = ".github/update.json";
    private const long MaxDownloadBytes = 250L * 1024 * 1024;
    private static readonly TimeSpan CheckRequestTimeout = TimeSpan.FromSeconds(6);

    private static readonly Uri RawManifestUri = new(
        $"https://raw.githubusercontent.com/{Owner}/{Repository}/{Branch}/{ManifestPath}");

    private static readonly Uri ContentsApiUri = new(
        $"https://api.github.com/repos/{Owner}/{Repository}/contents/{ManifestPath}?ref={Branch}");

    private static readonly Uri LatestReleaseApiUri = new(
        $"https://api.github.com/repos/{Owner}/{Repository}/releases/latest");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly string _updatesFolder;
    private bool _disposed;

    public UpdateService()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All
        };

        _http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"HWZ-TaskManager/{AppInfo.VersionText}");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

        _updatesFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HWZ", "TaskManager", "updates");
    }

    public string UpdatesFolder => _updatesFolder;

    public static bool CanDownload(UpdateManifest manifest)
        => IsTrustedDownloadUrl(manifest.DownloadUrl) && IsSha256(manifest.Sha256);

    public bool CanSelfUpdate(out string reason)
    {
        reason = string.Empty;

        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath) ||
            !string.Equals(Path.GetExtension(processPath), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            reason = "当前运行方式不支持自动替换，请前往 Releases 下载新版。";
            return false;
        }

        var directory = Path.GetDirectoryName(processPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            reason = "无法确定程序所在目录，请前往 Releases 下载新版。";
            return false;
        }

        // A framework-dependent build keeps its managed assemblies next to the apphost.
        // Replacing only the apphost would mix two different versions.
        if (File.Exists(Path.Combine(directory, "HWZ.TaskManager.dll")))
        {
            reason = "当前为开发构建，自动替换已停用；请使用发布版或前往 Releases 下载。";
            return false;
        }

        try
        {
            var probe = Path.Combine(directory, $".hwz-update-{Guid.NewGuid():N}.tmp");
            using (File.Create(probe, 1, FileOptions.DeleteOnClose))
            {
            }

            reason = string.Empty;
            return true;
        }
        catch
        {
            reason = "程序目录不可写，请以管理员身份运行或手动下载新版。";
            return false;
        }
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        var errors = new List<string>();

        // Primary source: a deliberately unintrusive manifest kept under .github.
        try
        {
            var manifest = await FetchRawManifestAsync(cancellationToken);
            return Evaluate(manifest);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            errors.Add("raw:" + exception.Message);
        }

        // Raw content can be unavailable behind some proxies. The contents API
        // serves the same hidden file and is usually reachable where raw is not.
        try
        {
            var manifest = await FetchManifestThroughApiAsync(cancellationToken);
            return Evaluate(manifest);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            errors.Add("contents:" + exception.Message);
        }

        // Final fallback: the public Releases API. GitHub includes a sha256 digest
        // for release assets in recent API versions, which keeps downloads verifiable.
        try
        {
            var manifest = await FetchLatestReleaseAsync(cancellationToken);
            return Evaluate(manifest);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            errors.Add("release:" + exception.Message);
        }

        var detail = string.Join(" | ", errors);
        Log("Update check failed: " + detail);
        return new UpdateCheckResult(
            UpdateCheckStatus.Failed,
            null,
            "无法连接 GitHub 更新服务。请检查网络、代理或稍后重试。");
    }

    public async Task<string> DownloadAsync(
        UpdateManifest manifest,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        if (!IsTrustedDownloadUrl(manifest.DownloadUrl))
        {
            throw new InvalidOperationException("更新下载地址未通过安全校验。");
        }

        if (!IsSha256(manifest.Sha256))
        {
            throw new InvalidOperationException("版本清单缺少有效的 SHA-256 校验值。");
        }

        Directory.CreateDirectory(_updatesFolder);
        CleanupOldFiles();

        var fileName = BuildFileName(manifest);
        var finalPath = Path.Combine(_updatesFolder, fileName);
        var temporaryPath = finalPath + ".part";

        TryDelete(temporaryPath);
        TryDelete(finalPath);

        using var response = await _http.GetAsync(
            manifest.DownloadUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        if (totalBytes is > MaxDownloadBytes)
        {
            throw new InvalidDataException("更新包超过允许的大小。");
        }

        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var destination = new FileStream(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[128 * 1024];
            long received = 0;

            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                received += read;
                if (received > MaxDownloadBytes)
                {
                    throw new InvalidDataException("更新包超过允许的大小。");
                }

                sha.AppendData(buffer, 0, read);
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);

                if (totalBytes is > 0)
                {
                    progress?.Report(received * 100d / totalBytes.Value);
                }
            }

            await destination.FlushAsync(cancellationToken);

            var actualHash = Convert.ToHexString(sha.GetHashAndReset());
            if (!string.Equals(actualHash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("更新包校验失败，下载内容可能不完整。");
            }
        }

        File.Move(temporaryPath, finalPath, true);
        progress?.Report(100);
        Log($"Downloaded update {manifest.Version} ({new FileInfo(finalPath).Length} bytes).");
        return finalPath;
    }

    public bool IsDownloadedFileValid(string path, string expectedSha256)
    {
        try
        {
            if (!File.Exists(path) || !IsSha256(expectedSha256))
            {
                return false;
            }

            using var stream = File.OpenRead(path);
            var actual = Convert.ToHexString(SHA256.HashData(stream));
            return string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public void LaunchInstaller(string downloadedPath, string expectedSha256)
    {
        if (!IsDownloadedFileValid(downloadedPath, expectedSha256))
        {
            throw new InvalidDataException("更新文件校验失败，请重新下载。");
        }

        if (!CanSelfUpdate(out var reason))
        {
            throw new InvalidOperationException(reason);
        }

        var targetPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("无法确定当前程序路径。");
        var targetDirectory = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException("无法确定当前程序目录。");
        var backupPath = targetPath + ".old";
        var scriptPath = Path.Combine(
            _updatesFolder,
            $"apply-update-{DateTime.Now:yyyyMMddHHmmss}.ps1");

        var script = BuildInstallerScript(
            Environment.ProcessId,
            downloadedPath,
            targetPath,
            targetDirectory,
            backupPath,
            scriptPath,
            LogPath);

        Directory.CreateDirectory(_updatesFolder);
        File.WriteAllText(scriptPath, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = _updatesFolder
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-WindowStyle");
        startInfo.ArgumentList.Add("Hidden");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);

        if (Process.Start(startInfo) is null)
        {
            throw new InvalidOperationException("无法启动更新安装程序。");
        }

        Log($"Update installer launched for {targetPath}.");
    }

    public void CleanupOldFiles()
    {
        try
        {
            if (!Directory.Exists(_updatesFolder))
            {
                return;
            }

            var cutoff = DateTime.UtcNow.AddDays(-7);
            foreach (var file in Directory.EnumerateFiles(_updatesFolder))
            {
                try
                {
                    var info = new FileInfo(file);
                    if (info.LastWriteTimeUtc < cutoff ||
                        file.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
                    {
                        info.Delete();
                    }
                }
                catch
                {
                    // Cleanup is best-effort.
                }
            }
        }
        catch
        {
            // Cleanup is best-effort.
        }
    }

    public static void Log(string message)
    {
        try
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HWZ", "TaskManager");
            Directory.CreateDirectory(folder);
            File.AppendAllText(
                Path.Combine(folder, "update.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
        }
        catch
        {
            // Logging must never interrupt an update check.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _http.Dispose();
    }

    private static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HWZ", "TaskManager", "update.log");

    private async Task<UpdateManifest> FetchRawManifestAsync(CancellationToken cancellationToken)
    {
        var separator = RawManifestUri.Query.Length == 0 ? "?" : "&";
        var uri = new Uri(RawManifestUri + separator + "t=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(CheckRequestTimeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
            using var response = await _http.SendAsync(request, timeoutCts.Token);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            return ParseManifest(json) ?? throw new InvalidDataException("版本清单格式无效。");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("读取隐藏版本清单超时。");
        }
    }

    private async Task<UpdateManifest> FetchManifestThroughApiAsync(CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(CheckRequestTimeout);
        try
        {
            using var response = await _http.GetAsync(ContentsApiUri, timeoutCts.Token);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(timeoutCts.Token);

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var encoding = root.TryGetProperty("encoding", out var encodingElement)
                ? encodingElement.GetString()
                : null;
            var content = root.TryGetProperty("content", out var contentElement)
                ? contentElement.GetString()
                : null;

            if (!string.Equals(encoding, "base64", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(content))
            {
                throw new InvalidDataException("版本清单内容无效。");
            }

            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(content.Replace("\n", string.Empty)));
            return ParseManifest(decoded) ?? throw new InvalidDataException("版本清单格式无效。");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("通过 GitHub API 读取版本清单超时。");
        }
    }

    private async Task<UpdateManifest> FetchLatestReleaseAsync(CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(CheckRequestTimeout);
        try
        {
            using var response = await _http.GetAsync(LatestReleaseApiUri, timeoutCts.Token);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(timeoutCts.Token);

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var tag = root.TryGetProperty("tag_name", out var tagElement)
                ? tagElement.GetString()?.TrimStart('v', 'V')
                : null;
            if (!IsVersion(tag))
            {
                throw new InvalidDataException("Release 版本号无效。");
            }

            var pageUrl = root.TryGetProperty("html_url", out var pageElement)
                ? pageElement.GetString() ?? string.Empty
                : string.Empty;
            var notes = root.TryGetProperty("body", out var bodyElement)
                ? bodyElement.GetString() ?? string.Empty
                : string.Empty;
            var publishedAt = root.TryGetProperty("published_at", out var publishedElement)
                ? publishedElement.GetString() ?? string.Empty
                : string.Empty;

            var assetUrl = string.Empty;
            var digest = string.Empty;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var nameElement)
                        ? nameElement.GetString() ?? string.Empty
                        : string.Empty;
                    if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                        !name.Contains("win-x64", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    assetUrl = asset.TryGetProperty("browser_download_url", out var urlElement)
                        ? urlElement.GetString() ?? string.Empty
                        : string.Empty;
                    digest = asset.TryGetProperty("digest", out var digestElement)
                        ? digestElement.GetString() ?? string.Empty
                        : string.Empty;
                    break;
                }
            }

            if (digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                digest = digest["sha256:".Length..];
            }
            else
            {
                digest = string.Empty;
            }

            return new UpdateManifest
            {
                Version = tag!,
                DownloadUrl = assetUrl,
                Sha256 = digest,
                ReleaseNotes = notes,
                ReleasePageUrl = pageUrl,
                PublishedAt = publishedAt
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("通过 GitHub Releases API 检查更新超时。");
        }
    }

    private static UpdateCheckResult Evaluate(UpdateManifest manifest)
    {
        if (!IsVersion(manifest.Version))
        {
            return new UpdateCheckResult(
                UpdateCheckStatus.Failed,
                null,
                "版本清单中的版本号无效。");
        }

        var remote = ParseVersion(manifest.Version);
        if (remote <= AppInfo.Version)
        {
            return new UpdateCheckResult(
                UpdateCheckStatus.UpToDate,
                manifest,
                $"当前已是最新版本 v{AppInfo.VersionText}。");
        }

        var canDownload = IsTrustedDownloadUrl(manifest.DownloadUrl) && IsSha256(manifest.Sha256);
        return new UpdateCheckResult(
            UpdateCheckStatus.UpdateAvailable,
            manifest,
            canDownload
                ? $"发现新版本 v{manifest.Version}。"
                : $"发现新版本 v{manifest.Version}，请在 Releases 页面手动下载。");
    }

    private static UpdateManifest? ParseManifest(string json)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, JsonOptions);
            return manifest is null || !IsVersion(manifest.Version) ? null : manifest;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Trim().TrimStart('v', 'V').Split('.');
        if (parts.Length is < 2 or > 4)
        {
            return false;
        }

        return parts.All(part => int.TryParse(part, out var number) && number >= 0);
    }

    private static Version ParseVersion(string value)
    {
        var normalized = value.Trim().TrimStart('v', 'V');
        return Version.TryParse(normalized, out var version)
            ? AppInfo.NormalizeVersion(version)
            : new Version(0, 0, 0);
    }

    private static bool IsSha256(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           value.Length == 64 &&
           value.All(Uri.IsHexDigit);

    private static bool IsTrustedDownloadUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return uri.AbsolutePath.StartsWith(
            $"/{Owner}/{Repository}/releases/download/",
            StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildFileName(UpdateManifest manifest)
    {
        var fromUrl = string.Empty;
        if (Uri.TryCreate(manifest.DownloadUrl, UriKind.Absolute, out var uri))
        {
            fromUrl = Path.GetFileName(uri.LocalPath);
        }

        if (string.IsNullOrWhiteSpace(fromUrl) ||
            !fromUrl.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            fromUrl = $"HWZ.TaskManager-{manifest.Version}-win-x64.exe";
        }

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            fromUrl = fromUrl.Replace(invalid, '-');
        }

        return fromUrl;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // A stale .part file must not block a fresh download.
        }
    }

    private static string BuildInstallerScript(
        int processId,
        string sourcePath,
        string targetPath,
        string targetDirectory,
        string backupPath,
        string scriptPath,
        string logPath)
    {
        return $$"""
            $ErrorActionPreference = 'Stop'
            $log = '{{EscapePowerShell(logPath)}}'

            function Write-UpdateLog([string]$message) {
                try {
                    Add-Content -LiteralPath $log -Value ("[{0}] {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $message)
                } catch { }
            }

            try {
                $waited = 0
                while ((Get-Process -Id {{processId}} -ErrorAction SilentlyContinue) -and $waited -lt 60) {
                    Start-Sleep -Milliseconds 500
                    $waited++
                }

                $source = '{{EscapePowerShell(sourcePath)}}'
                $target = '{{EscapePowerShell(targetPath)}}'
                $backup = '{{EscapePowerShell(backupPath)}}'
                $replaced = $false

                for ($attempt = 0; $attempt -lt 40; $attempt++) {
                    $movedOld = $false
                    try {
                        if (Test-Path -LiteralPath $target) {
                            if (Test-Path -LiteralPath $backup) {
                                Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
                            }
                            Move-Item -LiteralPath $target -Destination $backup -Force -ErrorAction Stop
                            $movedOld = $true
                        }
                        elseif (-not (Test-Path -LiteralPath $backup)) {
                            throw "Neither the current executable nor its backup exists."
                        }

                        Move-Item -LiteralPath $source -Destination $target -Force -ErrorAction Stop
                        $replaced = $true
                        break
                    }
                    catch {
                        Write-UpdateLog $_.Exception.Message
                        if ($movedOld -and -not (Test-Path -LiteralPath $target) -and (Test-Path -LiteralPath $backup)) {
                            try { Move-Item -LiteralPath $backup -Destination $target -Force -ErrorAction Stop } catch { }
                        }
                        if ($attempt -eq 39) { throw }
                        Start-Sleep -Milliseconds 500
                    }
                }

                if (-not $replaced) {
                    throw "The update file could not be moved into place."
                }

                Write-UpdateLog "Update replaced: $target"
                Start-Process -FilePath $target -WorkingDirectory '{{EscapePowerShell(targetDirectory)}}'
                Start-Sleep -Milliseconds 1800
                Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
            }
            catch {
                Write-UpdateLog ("Update failed: " + $_.Exception.Message)
            }
            finally {
                Remove-Item -LiteralPath '{{EscapePowerShell(scriptPath)}}' -Force -ErrorAction SilentlyContinue
            }
            """;
    }

    private static string EscapePowerShell(string value) => value.Replace("'", "''");
}
