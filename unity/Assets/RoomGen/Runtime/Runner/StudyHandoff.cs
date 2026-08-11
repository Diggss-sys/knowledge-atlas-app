using System.IO;
using System.Text;
using RoomGen.Contracts;
using UnityEngine;

namespace RoomGen.Runner
{
    /// <summary>
    /// The one place the operator's Publish drops a study and the participant runner picks it up — a
    /// file in the app's save folder. Closes author → run: the operator authors + publishes a study,
    /// the participant app loads THAT study instead of the bundled fixture.
    ///
    /// A file (not a bundled Resource) precisely because it's authored at runtime. The released
    /// team-run build ships without one, so it falls back to the fixture — every teammate runs the
    /// identical study, which is what makes the cross-machine frame-rate numbers comparable.
    /// </summary>
    public static class StudyHandoff
    {
        static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public static string PublishedStudyPath =>
            Path.Combine(Application.persistentDataPath, "published-study.json");

        public static bool HasPublishedStudy => File.Exists(PublishedStudyPath);

        /// <summary>
        /// Update the live handoff slot and preserve an immutable, content-addressed copy. Passing a
        /// root makes the operation deterministic in tests; production defaults to persistentDataPath.
        /// Returns the archive path used for this exact document.
        /// </summary>
        public static string Publish(string studyJson, string studyId, string outputRoot = null)
        {
            if (string.IsNullOrWhiteSpace(studyJson))
                throw new System.ArgumentException("Study JSON is required.", nameof(studyJson));

            var root = outputRoot ?? Application.persistentDataPath;
            var hash = CanonicalJson.Sha256(studyJson);
            Directory.CreateDirectory(root);
            var archiveDir = Path.Combine(root, "published");
            Directory.CreateDirectory(archiveDir);
            var archivePath = Path.Combine(archiveDir,
                SafeFile(studyId) + "-" + hash.Substring(0, 12) + ".json");
            var archiveIsExact = false;
            if (File.Exists(archivePath))
            {
                try
                {
                    var existing = File.ReadAllText(archivePath);
                    archiveIsExact = existing == studyJson && CanonicalJson.Sha256(existing) == hash;
                }
                catch
                {
                    archiveIsExact = false;
                }
            }
            if (!archiveIsExact) WriteAtomic(archivePath, studyJson);
            if (File.ReadAllText(archivePath) != studyJson)
                throw new InvalidDataException("Study archive verification failed: " + archivePath);

            WriteAtomic(Path.Combine(root, "published-study.json"), studyJson);
            return archivePath;
        }

        static void WriteAtomic(string path, string content)
        {
            var temp = path + "." + System.Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, content, Utf8NoBom);
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        static string SafeFile(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "study";
            var text = new StringBuilder();
            foreach (var c in value.Trim())
                text.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
            return text.Length > 0 ? text.ToString() : "study";
        }
    }
}
