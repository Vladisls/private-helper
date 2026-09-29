using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;

namespace CAHelper
{
    /// Self-update from the GitHub repo's main branch.
    /// release/version.txt holds two or three lines: version (e.g. 2.1.0), the SHA-256 of release/CabalHelper.exe and,
    /// optionally, the SHA-256 of release/ocr.zip (the OCR package, unpacked into ocr/ next to the exe).
    static class Updater
    {
        public const string DefaultBaseUrl = "https://raw.githubusercontent.com/Vladisls/private-helper/main/release/";

        public static Version Current => Assembly.GetExecutingAssembly().GetName().Version;
        static string ExePath => Process.GetCurrentProcess().MainModule.FileName;
        static string Dir => Path.GetDirectoryName(ExePath);
        static string OldPath => Path.Combine(Dir, "CabalHelper.old.exe");
        static string NewPath => Path.Combine(Dir, "CabalHelper.new.exe");
        /// The OCR package folder next to the exe, and the file in it that says which ocr.zip it came from.
        public static string OcrDir => Path.Combine(Dir, "ocr");
        public const string OcrShaFile = "ocr.sha";

        public enum Result { UpToDate, Updated, Failed, Disabled }

        /// Leftover from the previous update: the old exe could not be deleted while it was running.
        public static void CleanupOld()
        {
            try { if (File.Exists(OldPath)) File.Delete(OldPath); } catch { }
            try { if (File.Exists(NewPath)) File.Delete(NewPath); } catch { }
        }

        public static Result CheckAndApply(string baseUrl, int versionTimeoutMs, out string message) => CheckAndApply(baseUrl, versionTimeoutMs, out message, out _);

        /// Checks main for a newer build. If found: downloads, verifies the checksum, swaps the exe.
        /// Returns Updated when the new exe is in place and the caller should restart (the new copy then checks the
        /// OCR package itself, see CheckOcrPackage). Otherwise the OCR package is checked here: ocrMessage says what
        /// happened to it (null = nothing to do). A failing OCR package never changes the result.
        public static Result CheckAndApply(string baseUrl, int versionTimeoutMs, out string message, out string ocrMessage)
        {
            message = ""; ocrMessage = null;
            try
            {
                string url = BaseUrl(baseUrl);
                string info = Download(url + "version.txt?t=" + DateTime.UtcNow.Ticks, versionTimeoutMs);
                if (!TryParseInfo(info, out Version latest, out string sha, out string ocrSha))
                { message = "version.txt on GitHub is not readable"; return Result.Failed; }
                if (latest <= Current)
                {
                    message = "Up to date (v" + Short(Current) + ")";
                    ocrMessage = ApplyOcrPackage(url, ocrSha);
                    return Result.UpToDate;
                }

                byte[] exe = DownloadBytes(url + "CabalHelper.exe?t=" + DateTime.UtcNow.Ticks, 60000);
                string got = Sha256(exe);
                if (!string.Equals(got, sha, StringComparison.OrdinalIgnoreCase))
                {
                    // GitHub can serve the new version file with the old exe for a few minutes: try once more.
                    System.Threading.Thread.Sleep(20000);
                    exe = DownloadBytes(url + "CabalHelper.exe?r=" + DateTime.UtcNow.Ticks, 60000);
                    got = Sha256(exe);
                    if (!string.Equals(got, sha, StringComparison.OrdinalIgnoreCase))
                    { message = "Update " + Short(latest) + " is published but the download didn't match yet (GitHub cache). Restart in a few minutes."; return Result.Failed; }
                }

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

        /// Only the OCR package (used right after an exe update, when the new copy starts with --updated).
        /// Returns what happened, or null when there was nothing to do. Never throws.
        public static string CheckOcrPackage(string baseUrl, int versionTimeoutMs)
        {
            try
            {
                string url = BaseUrl(baseUrl);
                string info = Download(url + "version.txt?t=" + DateTime.UtcNow.Ticks, versionTimeoutMs);
                if (!TryParseInfo(info, out _, out _, out string ocrSha)) return "OCR package: version.txt on GitHub is not readable";
                return ApplyOcrPackage(url, ocrSha);
            }
            catch (Exception e) { return "OCR package check failed: " + e.Message; }
        }

        /// Downloads release/ocr.zip when version.txt names one and ocr/ocr.sha is missing or different, checks its
        /// SHA-256 (once more after 20 s if GitHub still serves the old file), unpacks it into ocr/ and writes
        /// ocr/ocr.sha. Returns what happened (null = already up to date or no OCR package published). Never throws.
        static string ApplyOcrPackage(string url, string ocrSha)
        {
            try
            {
                if (!OcrPackageNeeded(ocrSha, ReadLocalOcrSha(OcrDir))) return null;
                byte[] zip = DownloadBytes(url + "ocr.zip?t=" + DateTime.UtcNow.Ticks, 180000);
                if (!string.Equals(Sha256(zip), ocrSha, StringComparison.OrdinalIgnoreCase))
                {
                    System.Threading.Thread.Sleep(20000);
                    zip = DownloadBytes(url + "ocr.zip?r=" + DateTime.UtcNow.Ticks, 180000);
                    if (!string.Equals(Sha256(zip), ocrSha, StringComparison.OrdinalIgnoreCase))
                        return "OCR package is published but the download didn't match yet (GitHub cache). Restart in a few minutes.";
                }
                int written = InstallOcrPackage(zip, ocrSha, OcrDir);
                return "OCR package installed (" + written + " file" + (written == 1 ? "" : "s") + " updated, " + (zip.Length / 1024 / 1024.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " MB)";
            }
            catch (Exception e)
            {
                return "OCR package update failed: " + e.Message + (e is IOException || e is UnauthorizedAccessException ? " (a file in ocr\\ may be in use: restart the helper)" : "");
            }
        }

        // ---------- pure parts (unit-tested without network) ----------

        /// True when the published OCR package (3rd line of version.txt) differs from the installed one (ocr/ocr.sha).
        public static bool OcrPackageNeeded(string publishedSha, string localSha) =>
            !string.IsNullOrEmpty(publishedSha) && !string.Equals(publishedSha.Trim(), (localSha ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

        /// The SHA-256 in ocr/ocr.sha, or null when there is none.
        public static string ReadLocalOcrSha(string ocrDir)
        {
            try { var p = Path.Combine(ocrDir, OcrShaFile); return File.Exists(p) ? File.ReadAllText(p).Trim() : null; }
            catch { return null; }
        }

        /// Checks a downloaded ocr.zip before anything is written: its SHA-256, that it opens, that every entry stays
        /// inside ocr/ and that the language data (tessdata/eng.traineddata) is in it.
        public static bool ValidateOcrZip(byte[] zip, string expectedSha, out string error)
        {
            error = null;
            if (zip == null || zip.Length == 0) { error = "ocr.zip is empty"; return false; }
            if (!string.Equals(Sha256(zip), (expectedSha ?? "").Trim(), StringComparison.OrdinalIgnoreCase)) { error = "ocr.zip SHA-256 doesn't match version.txt"; return false; }
            try
            {
                using (var za = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read))
                {
                    bool data = false;
                    foreach (var e in za.Entries)
                    {
                        if (SafeEntryPath(e.FullName) == null) { error = "ocr.zip has a path outside ocr\\: " + e.FullName; return false; }
                        if (string.Equals(e.FullName.Replace('\\', '/'), "tessdata/eng.traineddata", StringComparison.OrdinalIgnoreCase) && e.Length > 0) data = true;
                    }
                    if (!data) { error = "ocr.zip has no tessdata/eng.traineddata"; return false; }
                }
            }
            catch (InvalidDataException e) { error = "ocr.zip is not a readable zip: " + e.Message; return false; }
            return true;
        }

        /// Unpacks a validated ocr.zip into ocrDir (files that are already identical are left alone, so DLLs in use
        /// don't block an update that doesn't change them), then writes ocr.sha. Returns how many files were written.
        /// Throws InvalidDataException when the zip doesn't validate (nothing is written then).
        public static int InstallOcrPackage(byte[] zip, string expectedSha, string ocrDir)
        {
            if (!ValidateOcrZip(zip, expectedSha, out string error)) throw new InvalidDataException(error);
            Directory.CreateDirectory(ocrDir);
            int written = 0;
            using (var za = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read))
                foreach (var e in za.Entries)
                {
                    string rel = SafeEntryPath(e.FullName);
                    if (rel.Length == 0 || e.FullName.EndsWith("/") || e.FullName.EndsWith("\\")) continue;     // directory entry
                    if (string.Equals(rel, OcrShaFile, StringComparison.OrdinalIgnoreCase)) continue;
                    string target = Path.Combine(ocrDir, rel);
                    byte[] data;
                    using (var s = e.Open()) using (var ms = new MemoryStream()) { s.CopyTo(ms); data = ms.ToArray(); }
                    if (File.Exists(target) && new FileInfo(target).Length == data.Length && File.ReadAllBytes(target).SequenceEqual(data)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    string tmp = target + ".new";
                    File.WriteAllBytes(tmp, data);
                    if (File.Exists(target)) File.Delete(target);
                    File.Move(tmp, target);
                    written++;
                }
            File.WriteAllText(Path.Combine(ocrDir, OcrShaFile), expectedSha.Trim().ToLowerInvariant() + "\n");
            return written;
        }

        /// A zip entry's path relative to ocr/ with the platform's separators, or null when it would leave ocr/.
        static string SafeEntryPath(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var parts = name.Replace('\\', '/').Split('/');
            if (name.StartsWith("/") || name.StartsWith("\\") || name.Contains(":") || parts.Any(p => p == "..")) return null;
            return string.Join(Path.DirectorySeparatorChar.ToString(), parts.Where(p => p.Length > 0 && p != "."));
        }

        /// Starts a new copy after an update. The new copy waits for this one to exit.
        public static void Restart() => Launch("--updated");

        /// Plain restart from the menu: the new copy waits for this one, then starts normally (including the update check).
        public static void Launch(string arg)
        {
            Process.Start(new ProcessStartInfo(ExePath, arg) { UseShellExecute = false, WorkingDirectory = Dir });
        }

        public static bool TryParseInfo(string text, out Version version, out string sha) => TryParseInfo(text, out version, out sha, out _);

        /// version.txt: version, exe SHA-256 and an optional 3rd line, the OCR package's SHA-256 (null when absent).
        /// Both hashes must be 64 hex characters.
        public static bool TryParseInfo(string text, out Version version, out string sha, out string ocrSha)
        {
            version = null; sha = null; ocrSha = null;
            if (text == null) return false;
            var lines = text.Replace("\r", "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
            if (lines.Length < 2 || lines.Length > 3) return false;
            sha = lines[1];
            if (lines.Length == 3) { ocrSha = lines[2]; if (!IsSha256(ocrSha)) return false; }
            return Version.TryParse(lines[0], out version) && IsSha256(sha);
        }

        static bool IsSha256(string s) => s != null && s.Length == 64 && s.All(Uri.IsHexDigit);

        public static string Short(Version v) => v == null ? "?" : $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";

        public static string Sha256(byte[] data)
        {
            using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
        }

        static string BaseUrl(string baseUrl)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            string url = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl.Trim();
            return url.EndsWith("/") ? url : url + "/";
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
