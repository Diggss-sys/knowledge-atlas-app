using NUnit.Framework;
using RoomGen.UI;

namespace RoomGen.Tests
{
    /// <summary>
    /// The participant "Open results folder" button broke on macOS (Raven, 2026-07-20): the save path
    /// has spaces ("COGS 160 Research Lab/Room Studio") and the raw "file://"+path we passed left them
    /// unescaped, which macOS's opener silently drops. These pin the escaping on both path shapes.
    /// </summary>
    public class FolderUriTests
    {
        [Test]
        public void Windows_path_with_spaces_becomes_an_escaped_file_uri()
        {
            var uri = ParticipantFlow.FolderUri(@"C:\Users\raven\COGS 160 Research Lab\Room Studio");
            StringAssert.StartsWith("file:///C:/", uri);
            StringAssert.Contains("%20", uri);
            Assert.IsFalse(uri.Contains(" "), "no raw spaces may survive in the URI");
        }

        [Test]
        public void Posix_path_with_spaces_becomes_an_escaped_file_uri()
        {
            var uri = ParticipantFlow.FolderUri("/Users/raven/Library/Application Support/COGS 160 Research Lab/Room Studio");
            StringAssert.StartsWith("file:///Users/raven/", uri);
            StringAssert.Contains("Application%20Support", uri);
            StringAssert.Contains("Room%20Studio", uri);
            Assert.IsFalse(uri.Contains(" "), "no raw spaces may survive in the URI");
        }
    }
}
