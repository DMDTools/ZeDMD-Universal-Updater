using System.IO.Compression;
using System.Text;
using Spectre.Console;
using System.Text.Json;

namespace ZeDMDUpdater
{
    public class GithubRelease
    {
        public string? tag_name { get; set; }
        public string? name { get; set; }
        public string? body { get; set; }
        public bool prerelease { get; set; }
        public DateTime published_at { get; set; }
    }

    internal static class InternetFirmwares
    {
        public const int MIN_MAJOR_VERSION = 5;
        public const int MIN_MINOR_VERSION = 1;
        public const int MIN_PATCH_VERSION = 0;
        const int MAX_VERSIONS_TO_LIST = 64;
        public static byte[] avMVersion = new byte[MAX_VERSIONS_TO_LIST];
        public static byte[] avmVersion = new byte[MAX_VERSIONS_TO_LIST];
        public static byte[] avpVersion = new byte[MAX_VERSIONS_TO_LIST];
        public static byte avmajVersion = 0;
        public static byte avminVersion = 0;
        public static byte avpatVersion = 0;
        private readonly static string[] FirmwareFiles = { "ZeDMD-128x32.zip", "ZeDMD-128x64.zip", "ZeDMD-256x64.zip",
            "ZeDMD-LilygoS3Amoled_128x32.zip", "ZeDMD-LilygoS3Amoled_128x32_wifi.zip",
            "ZeDMD-S3-N16R8_128x32.zip","ZeDMD-S3-N16R8_128x64.zip","ZeDMD-S3-N16R8_256x64.zip"};
        private static bool[] FirmwareFilesAvailable = new bool[FirmwareFiles.Length];
        public static byte navVersions = 0;
        private static Dictionary<string, string> ReleaseDescriptions = new();

        private static readonly HttpClient httpClient = new HttpClient();

        public static async Task<bool> IsUrlAvailable(string url, int timeoutMilliseconds)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMilliseconds));
                var request = new HttpRequestMessage(HttpMethod.Head, url);
                var response = await httpClient.SendAsync(request, cts.Token);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public static async Task<bool> IsVersionAvailable(byte majv, byte minv, byte patv)
        {
            string URL = $"https://github.com/PPUC/ZeDMD/releases/download/v{majv}.{minv}.{patv}/ZeDMD-128x32.zip";
            return await IsUrlAvailable(URL, 1000);
        }

        public static int ValVersion(byte M, byte m, byte p)
        {
            return (int)(M << 16) + (int)(m << 8) + (int)p;
        }

        public static async Task CheckPages(string url)
        {
            for (int i = 0; i < FirmwareFiles.Length; i++)
            {
                FirmwareFilesAvailable[i] = await IsUrlAvailable(url + FirmwareFiles[i], 1000);
            }
        }

        public static List<string> GetVersionsList()
        {
            var versions = new List<string>();
            for (int ti = 0; ti < navVersions; ti++)
            {
                versions.Add($"v{avMVersion[navVersions - ti - 1]}.{avmVersion[navVersions - ti - 1]}.{avpVersion[navVersions - ti - 1]}");
            }
            return versions;
        }

        public static async Task<string> GetAvailableVersions()
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/PPUC/ZeDMD/releases");
                request.Headers.Add("User-Agent", "ZeDMD-Updater");

                var response = await httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var releases = JsonSerializer.Deserialize<GithubRelease[]>(content);
                if (releases == null)
                {
                    AnsiConsole.MarkupLine("[red]Error: Unable to parse GitHub releases[/]");
                    return "";
                }

                navVersions = 0;
                ReleaseDescriptions.Clear();

                foreach (var release in releases)
                {
                    if (navVersions >= MAX_VERSIONS_TO_LIST) break;

                    var tagName = release.tag_name;
                    if (string.IsNullOrEmpty(tagName) || !tagName.StartsWith("v")) continue;

                    var versionParts = tagName.TrimStart('v').Split('.');
                    if (versionParts.Length != 3) continue;

                    if (byte.TryParse(versionParts[0], out byte major) &&
                        byte.TryParse(versionParts[1], out byte minor) &&
                        byte.TryParse(versionParts[2], out byte patch))
                    {

                        // Skip versions less than the minimuml
                        if (major < MIN_MAJOR_VERSION ||
                            (major == MIN_MAJOR_VERSION && minor < MIN_MINOR_VERSION) ||
                            (major == MIN_MAJOR_VERSION && minor == MIN_MINOR_VERSION && patch < MIN_PATCH_VERSION))
                        {
                            continue;
                        }

                        // Store the version numbers
                        avMVersion[navVersions] = major;
                        avmVersion[navVersions] = minor;
                        avpVersion[navVersions] = patch;

                        // Store release description
                        string version = $"v{major}.{minor}.{patch}";
                        ReleaseDescriptions[version] = FormatReleaseNotes(release);

                        // Store latest version
                        if (navVersions == 0)
                        {
                            avmajVersion = major;
                            avminVersion = minor;
                            avpatVersion = patch;
                        }

                        navVersions++;
                    }
                }

                return $"{avmajVersion}.{avminVersion}.{avpatVersion}";
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error getting available versions: {ex.Message}[/]");
                return "";
            }
        }

        private static string FormatReleaseNotes(GithubRelease release)
        {
            var description = new StringBuilder();

            if (!string.IsNullOrEmpty(release.name))
                description.AppendLine(release.name);

            if (!string.IsNullOrEmpty(release.body))
            {
                // Clean up markdown and format for console
                var notes = release.body
                    .Replace("###", "")
                    .Replace("##", "")
                    .Replace("#", "")
                    .Trim();

                description.AppendLine(notes);
            }

            if (release.prerelease)
                description.AppendLine("[yellow]Pre-release version[/]");

            description.AppendLine($"Released: {release.published_at:yyyy-MM-dd}");

            return description.ToString().Trim();
        }

        public static string GetReleaseDescription(string version)
        {
            return ReleaseDescriptions.TryGetValue(version, out var description)
                ? description
                : "No release notes available";
        }

        public static async Task<string> DownloadFirmware(string version, string panelType, string boardType = "Standard", bool useWifi = false)
        {
            try
            {
                // Construct the download URL
                string baseUrl = "https://github.com/PPUC/ZeDMD/releases/download/";
                string zipFileUrl = baseUrl + version + "/ZeDMD-";

                // Build the filename based on selections
                if (boardType == "LilygoS3Amoled")
                {
                    zipFileUrl += "LilygoS3Amoled_128x32";
                    if (useWifi) zipFileUrl += "_wifi";
                }
                else
                {
                    if (boardType == "S3-N16R8")
                        zipFileUrl += "S3-N16R8_";

                    zipFileUrl += panelType;
                }
                zipFileUrl += ".zip";
                Console.WriteLine(zipFileUrl);
                // Extract the file name from the URL and insert version after ZeDMD-
                string fileName = "ZeDMD-" + version + "-" + Path.GetFileName(zipFileUrl).Substring(6);
                // Create downloads directory if it doesn't exist
                string downloadDir = Path.Combine(Directory.GetCurrentDirectory(), "downloads");
                Directory.CreateDirectory(downloadDir);

                string zipPath = Path.Combine(downloadDir, fileName);

                // Download the firmware
                var response = await httpClient.GetByteArrayAsync(zipFileUrl);
                await File.WriteAllBytesAsync(zipPath, response);

                // Extract the firmware
                string extractPath = Path.Combine(downloadDir, fileName.Replace(".zip", ""));
                if (Directory.Exists(extractPath))
                    Directory.Delete(extractPath, true);

                ZipFile.ExtractToDirectory(zipPath, extractPath);

                return extractPath;
            }
            catch (Exception ex)
            {
                throw new Exception($"Error downloading firmware: {ex.Message}", ex);
            }
        }
    }
}