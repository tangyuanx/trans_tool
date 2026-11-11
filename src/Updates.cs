using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace TransTool
{
    internal sealed class UpdatePackage
    {
        internal Version Version;
        internal Uri Download;
        internal long Size;
        internal string Hash;
    }

    internal sealed class UpdateService : IDisposable
    {
        internal const string ReleasePage = "https://github.com/tangyuanx/trans_tool/releases/latest";
        internal const string SetupName = "TransTool-Windows-Setup.exe";
        internal static readonly string CachePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Trans Tool", "Updates");

        private const string ReleaseApi = "https://api.github.com/repos/tangyuanx/trans_tool/releases/latest";
        private const int MaxSize = 16 * 1024 * 1024;
        private readonly HttpClient client;

        internal UpdateService() : this(new HttpClientHandler { AllowAutoRedirect = false }) {}

        internal UpdateService(HttpMessageHandler handler)
        {
            client = new HttpClient(handler);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("TransTool/" + Program.AppVersion.ToString(3));
        }

        internal async Task<UpdatePackage> CheckAsync(Version current, CancellationToken cancel)
        {
            using (var response = await client.GetAsync(ReleaseApi, cancel).ConfigureAwait(false))
            {
                if (response.StatusCode == HttpStatusCode.NotFound) return null;
                response.EnsureSuccessStatusCode();
                var serializer = new JavaScriptSerializer();
                string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var release = serializer.Deserialize<Release>(json);
                Version version;
                if (release == null || release.draft || release.prerelease ||
                    !TryVersion(release.tag_name, out version) || version <= current)
                    return null;

                foreach (var asset in release.assets ?? new Asset[0])
                {
                    if (asset.name != SetupName || asset.state != "uploaded") continue;
                    string url = "https://github.com/tangyuanx/trans_tool/releases/download/" +
                        release.tag_name + "/" + SetupName;
                    if (asset.browser_download_url != url || asset.size < 1 || asset.size > MaxSize ||
                        asset.digest == null || !Regex.IsMatch(asset.digest, @"\Asha256:[0-9a-fA-F]{64}\z"))
                        break;
                    return new UpdatePackage
                    {
                        Version = version, Download = new Uri(url), Size = asset.size,
                        Hash = asset.digest.Substring("sha256:".Length).ToLowerInvariant()
                    };
                }
                throw new InvalidDataException("发布页缺少完整的 Windows 安装包或 SHA-256 校验值。");
            }
        }

        internal async Task<string> DownloadAsync(UpdatePackage package, IProgress<int> progress, CancellationToken cancel)
        {
            string directory = Path.Combine(CachePath, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, SetupName);
            try
            {
                using (var response = await GetDownloadAsync(package.Download, cancel).ConfigureAwait(false))
                using (var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    32768, FileOptions.Asynchronous))
                {
                    byte[] buffer = new byte[32768];
                    long received = 0;
                    int previous = -1;
                    int read;
                    while ((read = await source.ReadAsync(buffer, 0, buffer.Length, cancel).ConfigureAwait(false)) != 0)
                    {
                        received += read;
                        if (received > package.Size) throw new InvalidDataException("安装包大小超过发布记录。");
                        await target.WriteAsync(buffer, 0, read, cancel).ConfigureAwait(false);
                        int percent = (int)(received * 100 / package.Size);
                        if (progress != null && percent != previous) progress.Report(percent);
                        previous = percent;
                    }
                    if (received != package.Size) throw new InvalidDataException("安装包下载不完整。");
                }

                if (!HashMatches(path, package.Hash)) throw new InvalidDataException("安装包 SHA-256 校验失败。");
                return path;
            }
            catch
            {
                DeleteCache(directory);
                throw;
            }
        }

        private async Task<HttpResponseMessage> GetDownloadAsync(Uri uri, CancellationToken cancel)
        {
            for (int redirects = 0; redirects <= 5; redirects++)
            {
                if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort ||
                    uri.UserInfo.Length != 0 || (uri.Host != "github.com" &&
                    uri.Host != "release-assets.githubusercontent.com" &&
                    uri.Host != "objects.githubusercontent.com"))
                    throw new InvalidDataException("安装包下载地址无效。");

                var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancel)
                    .ConfigureAwait(false);
                int status = (int)response.StatusCode;
                if (status >= 300 && status <= 399 && response.Headers.Location != null)
                {
                    uri = new Uri(uri, response.Headers.Location);
                    response.Dispose();
                    continue;
                }
                if (response.IsSuccessStatusCode) return response;
                response.Dispose();
                throw new HttpRequestException("安装包下载失败（HTTP " + status + "）。");
            }
            throw new HttpRequestException("安装包下载重定向次数过多。");
        }

        internal static bool TryVersion(string tag, out Version version)
        {
            version = null;
            return tag != null && Regex.IsMatch(tag, @"\Av(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z") &&
                Version.TryParse(tag.Substring(1), out version);
        }

        internal static bool HashMatches(string path, string expected)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
            {
                string actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
                return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
            }
        }

        internal static string Quote(string value)
        {
            return "\"" + value + "\"";
        }

        internal static void StartInstaller(string executable, string setup, string digest)
        {
            string helper = Path.Combine(Path.GetDirectoryName(setup), "TransTool-Update.exe");
            File.Copy(executable, helper, true);
            string arguments = "--apply " + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture) +
                " " + Quote(executable) + " " + Quote(digest);
            Process.Start(new ProcessStartInfo(helper, arguments) { UseShellExecute = false });
        }

        internal static void Apply(int parentId, string executable, string digest, string helper)
        {
            string directory = Path.GetDirectoryName(helper);
            string setup = Path.Combine(directory, SetupName);
            WaitForExit(parentId);
            try
            {
                if (!HashMatches(setup, digest)) throw new InvalidDataException("安装前的 SHA-256 校验失败。");
                string arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /RESTARTEXITCODE=9 " +
                    "/DIR=" + Quote(Path.GetDirectoryName(executable));
                using (var installer = Process.Start(new ProcessStartInfo(setup, arguments) { UseShellExecute = false }))
                {
                    installer.WaitForExit();
                    if (installer.ExitCode != 0) throw new InvalidOperationException("安装程序返回 " + installer.ExitCode + "。");
                }
            }
            finally
            {
                if (File.Exists(executable))
                    Process.Start(new ProcessStartInfo(executable) { UseShellExecute = false });
            }
        }

        private static void WaitForExit(int parentId)
        {
            Process parent;
            try { parent = Process.GetProcessById(parentId); }
            catch (ArgumentException) { return; }
            using (parent)
            {
                if (!parent.WaitForExit(15000)) throw new InvalidOperationException("应用尚未退出，请重新安装更新。");
            }
        }

        internal static void DeleteCache(string directory)
        {
            try { Directory.Delete(directory, true); }
            catch (IOException) {}
            catch (UnauthorizedAccessException) {}
        }

        internal static void CleanOldCache()
        {
            try
            {
                if (!Directory.Exists(CachePath)) return;
                foreach (var directory in Directory.GetDirectories(CachePath))
                {
                    Guid id;
                    if (Guid.TryParseExact(Path.GetFileName(directory), "N", out id) &&
                        Directory.GetLastWriteTimeUtc(directory) < DateTime.UtcNow.AddDays(-1))
                        DeleteCache(directory);
                }
            }
            catch (IOException) {}
            catch (UnauthorizedAccessException) {}
        }

        public void Dispose() { client.Dispose(); }

        public sealed class Release
        {
            public string tag_name { get; set; }
            public bool draft { get; set; }
            public bool prerelease { get; set; }
            public Asset[] assets { get; set; }
        }

        public sealed class Asset
        {
            public string name { get; set; }
            public string state { get; set; }
            public string browser_download_url { get; set; }
            public string digest { get; set; }
            public long size { get; set; }
        }
    }
}
