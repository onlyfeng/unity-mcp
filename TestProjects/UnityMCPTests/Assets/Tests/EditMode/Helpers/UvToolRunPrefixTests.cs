using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
using NUnit.Framework;
using UnityEditor;

namespace MCPForUnityTests.Editor.Helpers
{
    /// <summary>
    /// uv's top-level CLI has no --from/--prerelease, so when the resolved launcher is uv rather
    /// than uvx the launch args need a "tool run" prefix ("uvx" is shorthand for "uv tool run").
    /// </summary>
    public class UvToolRunPrefixTests
    {
        private string _tempDir;
        private bool _hadOverride;
        private string _savedOverride;

        [SetUp]
        public void SetUp()
        {
            _hadOverride = EditorPrefs.HasKey(EditorPrefKeys.UvxPathOverride);
            _savedOverride = EditorPrefs.GetString(EditorPrefKeys.UvxPathOverride, string.Empty);
            _tempDir = Path.Combine(Path.GetTempPath(), "mcp_uvtoolrun_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (_hadOverride)
                EditorPrefs.SetString(EditorPrefKeys.UvxPathOverride, _savedOverride);
            else
                EditorPrefs.DeleteKey(EditorPrefKeys.UvxPathOverride);
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        [TestCase("uv", true)]
        [TestCase("uv.exe", true)]
        [TestCase("UV.EXE", true)]
        [TestCase("uv.cmd", true)]
        [TestCase("uvx", false)]
        [TestCase("uvx.exe", false)]
        public void GetUvToolRunPrefixArgs_PrefixesOnlyUv(string fileName, bool expectPrefix)
        {
            var args = AssetPathUtility.GetUvToolRunPrefixArgs(Path.Combine(_tempDir, fileName));

            CollectionAssert.AreEqual(expectPrefix ? new[] { "tool", "run" } : Array.Empty<string>(), args);
        }

        [TestCase(null)]
        [TestCase("")]
        public void GetUvToolRunPrefixArgs_NoPathNoPrefix(string uvxPath)
        {
            CollectionAssert.IsEmpty(AssetPathUtility.GetUvToolRunPrefixArgs(uvxPath));
        }

        [Test]
        public void BuildUvxServerLaunchArgs_StartsWithToolRun_WhenLauncherIsUv()
        {
            MCPServiceLocator.Paths.SetUvxPathOverride(WriteFakeLauncher("uv"));

            var args = AssetPathUtility.BuildUvxServerLaunchArgs("mcp-for-unity", includeTransportStdio: true);

            CollectionAssert.AreEqual(new[] { "tool", "run" }, args.GetRange(0, 2));
            CollectionAssert.Contains(args, "--from");
        }

        [Test]
        public void BuildUvxServerLaunchArgs_HasNoToolRun_WhenLauncherIsUvx()
        {
            MCPServiceLocator.Paths.SetUvxPathOverride(WriteFakeLauncher("uvx"));

            var args = AssetPathUtility.BuildUvxServerLaunchArgs("mcp-for-unity", includeTransportStdio: true);

            CollectionAssert.DoesNotContain(args, "tool");
            CollectionAssert.Contains(args, "--from");
        }

        /// <summary>
        /// A sh script that answers "--version" the way uv/uvx do, so the override validates.
        /// </summary>
        private string WriteFakeLauncher(string name)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.Ignore("The fake launcher is a sh script; the prefix logic itself is covered cross-platform above.");
            }

            string path = Path.Combine(_tempDir, name);
            File.WriteAllText(path, "#!/bin/sh\necho '" + name + " 9.9.9'\n");
            using var chmod = Process.Start(new ProcessStartInfo("/bin/chmod", $"+x \"{path}\"") { UseShellExecute = false });
            chmod?.WaitForExit(2000);
            return path;
        }
    }
}
