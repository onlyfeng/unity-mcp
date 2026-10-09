using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MCPForUnity.Editor.Helpers;
using NUnit.Framework;

namespace MCPForUnityTests.Editor.Helpers
{
    public class UvxLaunchArgsTests
    {
        private static readonly string[] CorporateCaEnvVars =
            { "SSL_CERT_FILE", "REQUESTS_CA_BUNDLE", "CURL_CA_BUNDLE", "NODE_EXTRA_CA_CERTS" };

        private readonly Dictionary<string, string> _savedEnv = new();

        [SetUp]
        public void SetUp()
        {
            foreach (string name in CorporateCaEnvVars)
            {
                _savedEnv[name] = Environment.GetEnvironmentVariable(name);
                Environment.SetEnvironmentVariable(name, null);
            }
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var entry in _savedEnv)
                Environment.SetEnvironmentVariable(entry.Key, entry.Value);
        }

        [Test]
        public void BuildUvxServerLaunchArgs_OmitsNativeTls_WithoutCorporateCa()
        {
            CollectionAssert.DoesNotContain(
                AssetPathUtility.BuildUvxServerLaunchArgs("mcp-for-unity", includeTransportStdio: false), "--native-tls");
        }

        [Test]
        public void BuildUvxServerLaunchArgs_AddsNativeTls_BehindCorporateCa()
        {
            Environment.SetEnvironmentVariable("REQUESTS_CA_BUNDLE", "/etc/corp-ca.pem");

            var args = AssetPathUtility.BuildUvxServerLaunchArgs("mcp-for-unity", includeTransportStdio: false);

            CollectionAssert.Contains(args, "--native-tls");
            // uv builds that predate the --system-certs rename reject it outright.
            CollectionAssert.DoesNotContain(args, "--system-certs");
        }

        [TestCase("mcp-for-unity", "mcp-for-unity")]
        [TestCase("", "\"\"")]
        [TestCase("mcpforunityserver>=0.0.0a0", "\"mcpforunityserver>=0.0.0a0\"")]
        // Backslashes that don't precede a quote stay literal.
        [TestCase(@"C:\My Path\Server", "\"C:\\My Path\\Server\"")]
        // A trailing run is doubled so it doesn't escape the closing quote.
        [TestCase(@"C:\My Path\", "\"C:\\My Path\\\\\"")]
        [TestCase("a\"b", "\"a\\\"b\"")]
        [TestCase("a\\\"b", "\"a\\\\\\\"b\"")]
        public void QuoteCommandLineArg_FollowsWindowsArgvRules(string arg, string expected)
        {
            Assert.AreEqual(expected, AssetPathUtility.QuoteCommandLineArg(arg));
        }

        [Test]
        public void GetStdioLauncherError_FlagsBatchShimsOnWindows()
        {
            string error = McpConfigurationHelper.GetStdioLauncherError(@"C:\pyenv\shims\uvx.bat", useStdio: true);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                StringAssert.Contains("uvx.bat", error);
            else
                Assert.IsNull(error);
        }

        [TestCase(null)]
        [TestCase("")]
        public void GetStdioLauncherError_RejectsMissingUvx(string uvxPath)
        {
            StringAssert.Contains("uv package manager not found", McpConfigurationHelper.GetStdioLauncherError(uvxPath, useStdio: true));
            Assert.IsNull(McpConfigurationHelper.GetStdioLauncherError(uvxPath, useStdio: false));
        }

        [Test]
        public void GetStdioLauncherError_IgnoresHttpTransport()
        {
            Assert.IsNull(McpConfigurationHelper.GetStdioLauncherError(@"C:\pyenv\shims\uvx.bat", useStdio: false));
        }

        [Test]
        public void GetStdioLauncherError_AllowsRealExecutables()
        {
            Assert.IsNull(McpConfigurationHelper.GetStdioLauncherError(@"C:\Users\me\.local\bin\uvx.exe", useStdio: true));
            Assert.IsNull(McpConfigurationHelper.GetStdioLauncherError("/usr/local/bin/uvx", useStdio: true));
        }
    }
}
