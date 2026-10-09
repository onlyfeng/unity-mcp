using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using NUnit.Framework;
using UnityEditor;

namespace MCPForUnityTests.Editor.Helpers
{
    public class UvxLaunchArgsTests
    {
        private static readonly string[] CorporateCaEnvVars =
            { "SSL_CERT_FILE", "REQUESTS_CA_BUNDLE", "CURL_CA_BUNDLE", "NODE_EXTRA_CA_CERTS" };

        private readonly Dictionary<string, string> _savedEnv = new();
        private bool _hadCertMode;
        private string _savedCertMode;

        [SetUp]
        public void SetUp()
        {
            _hadCertMode = EditorPrefs.HasKey(EditorPrefKeys.UseSystemCertificates);
            _savedCertMode = EditorPrefs.GetString(EditorPrefKeys.UseSystemCertificates, string.Empty);
            EditorPrefs.DeleteKey(EditorPrefKeys.UseSystemCertificates);
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
            if (_hadCertMode)
                EditorPrefs.SetString(EditorPrefKeys.UseSystemCertificates, _savedCertMode);
            else
                EditorPrefs.DeleteKey(EditorPrefKeys.UseSystemCertificates);
        }

        private static bool HasSystemCertsFlag(IList<string> args)
        {
            return args.Contains("--system-certs") || args.Contains("--native-tls");
        }

        [Test]
        public void BuildUvxServerLaunchArgs_OmitsSystemCerts_WithoutCorporateCa()
        {
            Assert.IsFalse(HasSystemCertsFlag(
                AssetPathUtility.BuildUvxServerLaunchArgs("mcp-for-unity", includeTransportStdio: false)));
        }

        [Test]
        public void BuildUvxServerLaunchArgs_AddsSystemCerts_BehindCorporateCa()
        {
            Environment.SetEnvironmentVariable("REQUESTS_CA_BUNDLE", "/etc/corp-ca.pem");

            Assert.IsTrue(HasSystemCertsFlag(
                AssetPathUtility.BuildUvxServerLaunchArgs("mcp-for-unity", includeTransportStdio: false)));
        }

        [Test]
        public void BuildUvxServerLaunchArgs_AlwaysOverride_AddsSystemCertsWithoutCorporateCa()
        {
            EditorPrefs.SetString(EditorPrefKeys.UseSystemCertificates, "always");

            Assert.IsTrue(HasSystemCertsFlag(
                AssetPathUtility.BuildUvxServerLaunchArgs("mcp-for-unity", includeTransportStdio: false)));
        }

        [Test]
        public void BuildUvxServerLaunchArgs_NeverOverride_OmitsSystemCertsBehindCorporateCa()
        {
            EditorPrefs.SetString(EditorPrefKeys.UseSystemCertificates, "never");
            Environment.SetEnvironmentVariable("REQUESTS_CA_BUNDLE", "/etc/corp-ca.pem");

            Assert.IsFalse(HasSystemCertsFlag(
                AssetPathUtility.BuildUvxServerLaunchArgs("mcp-for-unity", includeTransportStdio: false)));
        }

        // uv 0.11 renamed --native-tls to --system-certs; older uv rejects the new name.
        [TestCase("0.9.18", "--native-tls")]
        [TestCase("0.10.12", "--native-tls")]
        [TestCase("0.11.0", "--system-certs")]
        [TestCase("0.12.19", "--system-certs")]
        // Unknown versions keep --system-certs, which earlier builds always emitted.
        [TestCase(null, "--system-certs")]
        [TestCase("not-a-version", "--system-certs")]
        public void SelectSystemCertsFlag_PicksFlagByUvVersion(string uvVersion, string expected)
        {
            Assert.AreEqual(expected, AssetPathUtility.SelectSystemCertsFlag(uvVersion));
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
