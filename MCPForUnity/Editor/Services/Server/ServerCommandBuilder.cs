using System;
using System.IO;
using System.Linq;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Services.Server
{
    /// <summary>
    /// Builds uvx/server command strings for starting the MCP HTTP server.
    /// Handles platform-specific command construction.
    /// </summary>
    public class ServerCommandBuilder : IServerCommandBuilder
    {
        /// <inheritdoc/>
        public bool TryBuildCommand(out string fileName, out string arguments, out string displayCommand, out string error)
        {
            fileName = null;
            arguments = null;
            displayCommand = null;
            error = null;

            bool useHttpTransport = EditorConfigurationCache.Instance.UseHttpTransport;
            if (!useHttpTransport)
            {
                error = "HTTP transport is disabled. Enable it in the MCP For Unity window first.";
                return false;
            }

            string httpUrl = HttpEndpointUtility.GetLocalBaseUrl();
            if (!HttpEndpointUtility.IsHttpLocalUrlAllowedForLaunch(httpUrl, out string localUrlError))
            {
                error = string.IsNullOrEmpty(localUrlError)
                    ? $"The configured URL ({httpUrl}) is not allowed for HTTP Local launch."
                    : $"{localUrlError} (configured URL: {httpUrl})";
                return false;
            }

            var (uvxPath, _, packageName) = AssetPathUtility.GetUvxCommandParts();
            if (string.IsNullOrEmpty(uvxPath))
            {
                error = "uv is not installed or found in PATH. Install it or set an override in Advanced Settings.";
                return false;
            }

            bool projectScopedTools = EditorPrefs.GetBool(
                EditorPrefKeys.ProjectScopedToolsLocalHttp,
                true
            );

            // Reuse the centralized uvx launch builder (system-certs / dev-flags / --from /
            // package) then append the HTTP-specific suffix. Pass every arg through
            // QuoteCommandLineArg so we are safe whether uvxPath is a real .exe or a
            // pyenv-win .bat shim — cmd.exe would otherwise interpret '>' in
            // "mcpforunityserver>=0.0.0a0" as stdout redirection.
            var argsList = new System.Collections.Generic.List<string>(
                AssetPathUtility.BuildUvxServerLaunchArgs(packageName, includeTransportStdio: false))
            {
                "--transport",
                "http",
                "--http-url",
                httpUrl,
            };
            if (projectScopedTools)
                argsList.Add("--project-scoped-tools");

            string args = string.Join(" ", argsList.ConvertAll(AssetPathUtility.QuoteCommandLineArg));

            fileName = uvxPath;
            arguments = args;
            displayCommand = $"{QuoteIfNeeded(uvxPath)} {args}";
            return true;
        }

        /// <inheritdoc/>
        public string BuildUvPathFromUvx(string uvxPath)
        {
            if (string.IsNullOrWhiteSpace(uvxPath))
            {
                return uvxPath;
            }

            string directory = Path.GetDirectoryName(uvxPath);
            string extension = Path.GetExtension(uvxPath);
            string uvFileName = "uv" + extension;

            return string.IsNullOrEmpty(directory)
                ? uvFileName
                : Path.Combine(directory, uvFileName);
        }

        /// <inheritdoc/>
        public string GetPlatformSpecificPathPrepend()
        {
            if (Application.platform == RuntimePlatform.OSXEditor)
            {
                return string.Join(Path.PathSeparator.ToString(), new[]
                {
                    "/opt/homebrew/bin",
                    "/usr/local/bin",
                    "/usr/bin",
                    "/bin"
                });
            }

            if (Application.platform == RuntimePlatform.LinuxEditor)
            {
                return string.Join(Path.PathSeparator.ToString(), new[]
                {
                    "/usr/local/bin",
                    "/usr/bin",
                    "/bin"
                });
            }

            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

                return string.Join(Path.PathSeparator.ToString(), new[]
                {
                    !string.IsNullOrEmpty(localAppData) ? Path.Combine(localAppData, "Programs", "uv") : null,
                    !string.IsNullOrEmpty(programFiles) ? Path.Combine(programFiles, "uv") : null
                }.Where(p => !string.IsNullOrEmpty(p)).ToArray());
            }

            return null;
        }

        /// <inheritdoc/>
        public string QuoteIfNeeded(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            return input.IndexOf(' ') >= 0 ? $"\"{input}\"" : input;
        }

    }
}
