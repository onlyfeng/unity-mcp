using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using MCPForUnity.Editor.Services;
using NUnit.Framework;

namespace MCPForUnityTests.Editor
{
    /// <summary>
    /// pyenv-win exposes uv only as .bat shims, which MCP clients would run through cmd.exe.
    /// The resolver asks pyenv for the real executable behind the shim instead. The fake
    /// pyenv is a .bat on Windows and an executable sh script elsewhere, so this runs on both.
    /// </summary>
    public class PathResolverPyenvTests
    {
        private string _pyenvRoot;

        [SetUp]
        public void SetUp()
        {
            _pyenvRoot = Path.Combine(Path.GetTempPath(), "mcp_pyenv_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_pyenvRoot, "bin"));
            Directory.CreateDirectory(Path.Combine(_pyenvRoot, "shims"));
        }

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(_pyenvRoot)) Directory.Delete(_pyenvRoot, true); } catch { }
        }

        [Test]
        public void ResolvesRealUvxBehindShim()
        {
            WriteShim("uvx");
            string realUvx = CreateFakeExe("uvx.exe");
            WriteFakePyenv(realUvx);

            Assert.AreEqual(realUvx, PathResolverService.ResolveUvxBehindPyenvShim(_pyenvRoot));
        }

        [Test]
        public void FallsBackToUvShim()
        {
            WriteShim("uv");
            string realUv = CreateFakeExe("uv.exe");
            WriteFakePyenv(realUv);

            Assert.AreEqual(realUv, PathResolverService.ResolveUvxBehindPyenvShim(_pyenvRoot));
        }

        [Test]
        public void ReturnsNull_WhenNoUvShim()
        {
            WriteFakePyenv(CreateFakeExe("uvx.exe"));

            Assert.IsNull(PathResolverService.ResolveUvxBehindPyenvShim(_pyenvRoot));
        }

        [Test]
        public void ReturnsNull_WhenPyenvCannotResolve()
        {
            WriteShim("uvx");
            WriteFakePyenv("pyenv: uvx: command not found");

            Assert.IsNull(PathResolverService.ResolveUvxBehindPyenvShim(_pyenvRoot));
        }

        [Test]
        public void ReturnsNull_WithoutPyenv()
        {
            WriteShim("uvx");

            Assert.IsNull(PathResolverService.ResolveUvxBehindPyenvShim(_pyenvRoot));
        }

        private void WriteShim(string command)
        {
            File.WriteAllText(Path.Combine(_pyenvRoot, "shims", command + ".bat"), "@echo off\r\n");
        }

        private string CreateFakeExe(string fileName)
        {
            string dir = Path.Combine(_pyenvRoot, "versions", "3.12.1", "Scripts");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, fileName);
            File.WriteAllText(path, string.Empty);
            return path;
        }

        private void WriteFakePyenv(string output)
        {
            string path = Path.Combine(_pyenvRoot, "bin", "pyenv.bat");
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                File.WriteAllText(path, "@echo off\r\necho " + output + "\r\n");
                return;
            }

            File.WriteAllText(path, "#!/bin/sh\necho '" + output + "'\n");
            using var chmod = Process.Start(new ProcessStartInfo("/bin/chmod", $"+x \"{path}\"") { UseShellExecute = false });
            chmod?.WaitForExit(2000);
        }
    }
}
