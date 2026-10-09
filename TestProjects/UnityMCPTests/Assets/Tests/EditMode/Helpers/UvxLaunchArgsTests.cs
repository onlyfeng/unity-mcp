using System.Runtime.InteropServices;
using MCPForUnity.Editor.Helpers;
using NUnit.Framework;

namespace MCPForUnityTests.Editor.Helpers
{
    public class UvxLaunchArgsTests
    {
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
        public void GetStdioShimError_FlagsBatchShimsOnWindows()
        {
            string error = McpConfigurationHelper.GetStdioShimError(@"C:\pyenv\shims\uvx.bat", useStdio: true);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                StringAssert.Contains("uvx.bat", error);
            else
                Assert.IsNull(error);
        }

        [Test]
        public void GetStdioShimError_IgnoresHttpTransport()
        {
            Assert.IsNull(McpConfigurationHelper.GetStdioShimError(@"C:\pyenv\shims\uvx.bat", useStdio: false));
        }

        [Test]
        public void GetStdioShimError_AllowsRealExecutables()
        {
            Assert.IsNull(McpConfigurationHelper.GetStdioShimError(@"C:\Users\me\.local\bin\uvx.exe", useStdio: true));
            Assert.IsNull(McpConfigurationHelper.GetStdioShimError("/usr/local/bin/uvx", useStdio: true));
        }
    }
}
