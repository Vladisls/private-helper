using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;

namespace CAHelper
{
    /// Self-update from the GitHub repo's main branch.
    /// release/version.txt holds two lines: version (e.g. 2.1.0) and the SHA-256 of release/CabalHelper.exe.
    static class Updater
    {
        public const string DefaultBaseUrl = "https://raw.githubusercontent.com/Vladisls/private-helper/main/release/";

        public static Version Current => Assembly.GetExecutingAssembly().GetName().Version;
        static string ExePath => Process.GetCurrentProcess().MainModule.FileName;
        static string Dir => Path.GetDirectoryName(ExePath);
        static string OldPath => Path.Combine(Dir, "CabalHelper.old.exe");
        static string NewPath => Path.Combine(Dir, "CabalHelper.new.exe");

        public enum Result { UpToDate, Updated, Failed, Disabled }

        /// Leftover from the previous update: the old exe could not be deleted while it was running.
        public static void CleanupOld()
        {
            try { if (File.Exists(OldPath)) File.Delete(OldPath); } catch { }
            try { if (File.Exists(NewPath)) File.Delete(NewPath); } catch { }
        }

        /// Checks main for a newer build. If found: downloads, verifies the checksum, swaps the exe.
        /// Returns Updated when the new exe is in place and the caller should restart.
        public static Result CheckAndApply(string baseUrl, int versionTimeoutMs, out string message)
        {
            message = "";
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                string url = (string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl.Trim());
                if (!url.EndsWith("/")) url += "/";

                string info = Download(url + "version.txt?t=" + DateTime.UtcNow.Ticks, versionTimeoutMs);
                if (!TryParseInfo(info, out Version latest, out string sha))
                { message = "version.txt on GitHub is not readable"; return Result.Failed; }
                if (latest <= Current) { message = "Up to date (v" + Short(Current) + ")"; return Result.UpToDate; }

                byte[] exe = DownloadBytes(url + "CabalHelper.exe?t=" + DateTime.UtcNow.Ticks, 60000);
                string got = Sha256(exe);
                if (!string.Equals(got, sha, StringComparison.OrdinalIgnoreCase))
                { message = "Download checksum did not match, update skipped"; return Result.Failed; }

                File.WriteAllBytes(NewPath, exe);
                if (File.Exists(OldPath)) File.Delete(OldPath);
                File.Move(ExePath, OldPath);          // Windows allows renaming a running exe
                File.Move(NewPath, ExePath);
                message = "Updated to v" + Short(latest);
                return Result.Updated;
            }
            catch (Exception e)
            {
                // Put the original back if the swap failed half-way.
                try { if (!File.Exists(ExePath) && File.Exists(OldPath)) File.Move(OldPath, ExePath); } catch { }
                message = "Update check failed: " + e.Message;
                return Result.Failed;
            }
        }

        public static void Restart()
        {
            Process.Start(new ProcessStartInfo(ExePath, "--updated") { UseShellExecute = false, WorkingDirectory = Dir });
        }

        public static bool TryParseInfo(string text, out Version version, out string sha)
        {
            version = null; sha = null;
            if (text == null) return false;
            var lines = text.Replace("\r", "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2) return false;
            sha = lines[1].Trim();
            return Version.TryParse(lines[0].Trim(), out version) && sha.Length == 64;
        }

        public static string Short(Version v) => v == null ? "?" : $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";

        static string Sha256(byte[] data)
        {
            using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
        }

        static string Download(string url, int timeoutMs) => System.Text.Encoding.UTF8.GetString(DownloadBytes(url, timeoutMs));

        static byte[] DownloadBytes(string url, int timeoutMs)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = timeoutMs; req.ReadWriteTimeout = timeoutMs;
            req.UserAgent = "CabalHelper/" + Short(Current);
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var s = resp.GetResponseStream())
            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }
    }
}
