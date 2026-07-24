using System.IO;
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
        public static string PublishedStudyPath =>
            Path.Combine(Application.persistentDataPath, "published-study.json");

        public static bool HasPublishedStudy => File.Exists(PublishedStudyPath);
    }
}
