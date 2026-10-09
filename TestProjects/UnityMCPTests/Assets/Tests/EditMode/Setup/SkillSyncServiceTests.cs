using MCPForUnity.Editor.Setup;
using NUnit.Framework;

namespace MCPForUnityTests.Editor.Setup
{
    public class SkillSyncServiceTests
    {
        [TestCase("com.coplaydev.unity-mcp@https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#beta",
            "https://github.com/CoplayDev/unity-mcp.git")]
        [TestCase("com.coplaydev.unity-mcp@git+https://gitlab.example.com/tools/unity-mcp.git?path=/MCPForUnity#beta",
            "https://gitlab.example.com/tools/unity-mcp.git")]
        // scp-style URLs carry their own '@'; only the first one separates the package name.
        [TestCase("com.coplaydev.unity-mcp@git@github.com:CoplayDev/unity-mcp.git?path=/MCPForUnity",
            "git@github.com:CoplayDev/unity-mcp.git")]
        [TestCase("com.coplaydev.unity-mcp", null)]
        [TestCase(null, null)]
        public void GetRepoUrlFromPackageId_StripsNameAndPackageSelectors(string packageId, string expected)
        {
            Assert.AreEqual(expected, SkillSyncService.GetRepoUrlFromPackageId(packageId));
        }

        [Test]
        public void GetDefaultRepoUrl_FallsBackToUpstream_ForNonGitInstall()
        {
            // The test project references the package with a local file: path, not git.
            Assert.AreEqual("https://github.com/CoplayDev/unity-mcp", SkillSyncService.GetDefaultRepoUrl());
        }
    }
}
