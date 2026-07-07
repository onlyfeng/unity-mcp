using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Implementation of path resolver service with override support
    /// </summary>
    public class PathResolverService : IPathResolverService
    {
        private bool _hasUvxPathFallback;
        private bool _resolvedUvxIsShim;

        public bool HasUvxPathOverride => !string.IsNullOrEmpty(EditorPrefs.GetString(EditorPrefKeys.UvxPathOverride, null));
        public bool HasClaudeCliPathOverride => !string.IsNullOrEmpty(EditorPrefs.GetString(EditorPrefKeys.ClaudeCliPathOverride, null));
        public bool HasUvxPathFallback => _hasUvxPathFallback;
        public bool ResolvedUvxIsShim => _resolvedUvxIsShim;

        /// <summary>
        /// Returns true if a path points to a Windows .bat/.cmd shim (e.g. pyenv-win's
        /// uvx.bat / uv.cmd). Shim launchers route arguments through cmd.exe, which
        /// interprets shell metacharacters in our args — most notably the '>' in
        /// "mcpforunityserver&gt;=0.0.0a0" is parsed as stdout redirection, leaving uvx
        /// with a bare "--from" and no value. Configurators should emit real .exe paths
        /// in client configs and treat shim resolution as a fallback only.
        /// </summary>
        public static bool IsShimPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return path.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase);
        }

        public string GetUvxPath()
        {
            // Reset transient flags at the start of each resolution
            _hasUvxPathFallback = false;
            _resolvedUvxIsShim = false;

            // Check override first - only validate if explicitly set
            if (HasUvxPathOverride)
            {
                string overridePath = EditorPrefs.GetString(EditorPrefKeys.UvxPathOverride, string.Empty);
                // Validate the override - if invalid, fall back to system discovery
                if (TryValidateUvxExecutable(overridePath, out string version))
                {
                    _resolvedUvxIsShim = IsShimPath(overridePath);
                    return overridePath;
                }
                // Override is set but invalid - fall back to system discovery
                string fallbackPath = ResolveUvxFromSystem();
                if (!string.IsNullOrEmpty(fallbackPath))
                {
                    _hasUvxPathFallback = true;
                    _resolvedUvxIsShim = IsShimPath(fallbackPath);
                    return fallbackPath;
                }
                // Return null to indicate override is invalid and no system fallback found
                return null;
            }

            // No override set - try discovery (uvx.exe before uvx.bat/.cmd, then uv variants)
            string discovered = ResolveUvxFromSystem();
            if (!string.IsNullOrEmpty(discovered))
            {
                _resolvedUvxIsShim = IsShimPath(discovered);
                return discovered;
            }

            // Fallback to bare command
            return "uvx";
        }

        /// <summary>
        /// Resolves uv/uvx from system by trying both commands.
        /// Returns the full path if found, null otherwise.
        /// </summary>
        private static string ResolveUvxFromSystem()
        {
            try
            {
                // Probe order on Windows: every real .exe before any .bat/.cmd shim,
                // even across the uvx/uv family boundary. PreflightStdioServerLaunchIfNeeded
                // now hard-rejects .bat / .cmd commands (cmd.exe corrupts the '>' in
                // "mcpforunityserver>=0.0.0a0"), so ranking uvx.bat above uv.exe would
                // convert a runnable "uv.exe + uvx.bat" host into a blocking error.
                // AssetPathUtility.BuildUvxServerLaunchArgs prepends "tool run" when the
                // resolved launcher is uv.*, so uv.exe with uvx-style args still works.
                string[] commandNames = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? new[] { "uvx.exe", "uv.exe", "uvx.bat", "uvx.cmd", "uv.bat", "uv.cmd" }
                    : new[] { "uvx", "uv" };

                foreach (string commandName in commandNames)
                {
                    foreach (string candidate in EnumerateCommandCandidates(commandName))
                    {
                        if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate))
                        {
                            return candidate;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                McpLog.Debug($"PathResolver error: {ex.Message}");
            }

            return null;
        }



        public string GetClaudeCliPath()
        {
            // Check override first - only validate if explicitly set
            if (HasClaudeCliPathOverride)
            {
                string overridePath = EditorPrefs.GetString(EditorPrefKeys.ClaudeCliPathOverride, string.Empty);
                // Validate the override - if invalid, don't fall back to discovery
                if (File.Exists(overridePath))
                {
                    return overridePath;
                }
                // Override is set but invalid - return null (no fallback)
                return null;
            }

            // No override: delegate to the shared discovery in ExecPath, which covers the
            // native-installer, npm, NVM and PATH-scan locations for every platform. Kept in
            // one place so both call sites (this and ExecPath's own callers) stay in sync.
            return ExecPath.ResolveClaude();
        }

        public bool IsPythonDetected()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return ExecPath.TryRun("python3", "--version", null, out _, out _, 2000);
            }

            // Windows: try real binaries first, then shim variants (.bat/.cmd) used by pyenv-win.
            foreach (string candidate in new[] {
                "python.exe", "python3.exe",
                "python.bat", "python3.bat",
                "python.cmd", "python3.cmd"
            })
            {
                if (ExecPath.TryRun(candidate, "--version", null, out _, out _, 2000))
                {
                    return true;
                }
            }
            return false;
        }

        public bool IsClaudeCliDetected()
        {
            return !string.IsNullOrEmpty(GetClaudeCliPath());
        }

        public void SetUvxPathOverride(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                ClearUvxPathOverride();
                return;
            }

            if (!File.Exists(path))
            {
                throw new ArgumentException("The selected uvx executable does not exist");
            }

            EditorPrefs.SetString(EditorPrefKeys.UvxPathOverride, path);
        }

        public void SetClaudeCliPathOverride(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                ClearClaudeCliPathOverride();
                return;
            }

            if (!File.Exists(path))
            {
                throw new ArgumentException("The selected Claude CLI executable does not exist");
            }

            EditorPrefs.SetString(EditorPrefKeys.ClaudeCliPathOverride, path);
        }

        public void ClearUvxPathOverride()
        {
            EditorPrefs.DeleteKey(EditorPrefKeys.UvxPathOverride);
        }

        public void ClearClaudeCliPathOverride()
        {
            EditorPrefs.DeleteKey(EditorPrefKeys.ClaudeCliPathOverride);
        }

        /// <summary>
        /// Validates the provided uv executable by running "--version" and parsing the output.
        /// </summary>
        /// <param name="uvxPath">Absolute or relative path to the uv/uvx executable.</param>
        /// <param name="version">Parsed version string if successful.</param>
        /// <returns>True when the executable runs and returns a uvx version string.</returns>
        public bool TryValidateUvxExecutable(string uvxPath, out string version)
        {
            version = null;

            if (string.IsNullOrEmpty(uvxPath))
                return false;

            try
            {
                // Check if the path is just a command name (no directory separator)
                bool isBareCommand = !uvxPath.Contains('/') && !uvxPath.Contains('\\');

                if (isBareCommand)
                {
                    // For bare commands like "uvx" or "uv", use EnumerateCommandCandidates to find full path first
                    string fullPath = FindUvxExecutableInPath(uvxPath);
                    if (string.IsNullOrEmpty(fullPath))
                        return false;
                    uvxPath = fullPath;
                }

                // Use ExecPath.TryRun which properly handles async output reading and timeouts
                if (!ExecPath.TryRun(uvxPath, "--version", null, out string stdout, out string stderr, 5000))
                    return false;

                // Check stdout first, then stderr (some tools output to stderr)
                string versionOutput = !string.IsNullOrWhiteSpace(stdout) ? stdout.Trim() : stderr.Trim();

                // uv/uvx outputs "uv x.y.z" or "uvx x.y.z", extract version number
                if (versionOutput.StartsWith("uvx ") || versionOutput.StartsWith("uv "))
                {
                    // Extract version: "uv 0.9.18 (hash date)" -> "0.9.18"
                    int spaceIndex = versionOutput.IndexOf(' ');
                    if (spaceIndex >= 0)
                    {
                        string afterCommand = versionOutput.Substring(spaceIndex + 1).Trim();
                        // Version is up to the first space or parenthesis
                        int nextSpace = afterCommand.IndexOf(' ');
                        int parenIndex = afterCommand.IndexOf('(');
                        int endIndex = Math.Min(
                            nextSpace >= 0 ? nextSpace : int.MaxValue,
                            parenIndex >= 0 ? parenIndex : int.MaxValue
                        );
                        version = endIndex < int.MaxValue ? afterCommand.Substring(0, endIndex).Trim() : afterCommand;
                        return true;
                    }
                }
            }
            catch
            {
                // Ignore validation errors
            }

            return false;
        }

        private string FindUvxExecutableInPath(string commandName)
        {
            try
            {
                // On Windows, a bare command name like "uvx" may resolve to .exe, .bat, or .cmd
                // (pyenv-win publishes .bat shims on PATH). Probe each variant in turn.
                IEnumerable<string> namesToProbe;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !HasExecutableExtension(commandName))
                {
                    namesToProbe = new[] { commandName + ".exe", commandName + ".bat", commandName + ".cmd" };
                }
                else
                {
                    namesToProbe = new[] { commandName };
                }

                foreach (string name in namesToProbe)
                {
                    foreach (string candidate in EnumerateCommandCandidates(name))
                    {
                        if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate))
                        {
                            return candidate;
                        }
                    }
                }
            }
            catch
            {
                // Ignore errors
            }

            return null;
        }

        private static bool HasExecutableExtension(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string ext = Path.GetExtension(name);
            if (string.IsNullOrEmpty(ext)) return false;
            return ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".bat", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Enumerates candidate paths for a generic command name.
        /// Searches PATH and common locations.
        /// </summary>
        private static IEnumerable<string> EnumerateCommandCandidates(string commandName)
        {
            // On Windows, only append ".exe" when no executable extension is present.
            // Previously this also appended ".exe" to names like "uvx.bat", producing
            // bogus probes such as "uvx.bat.exe".
            string exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !HasExecutableExtension(commandName)
                ? commandName + ".exe"
                : commandName;

            // Search PATH first
            string pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                foreach (string rawDir in pathEnv.Split(Path.PathSeparator))
                {
                    if (string.IsNullOrWhiteSpace(rawDir)) continue;
                    string dir = rawDir.Trim();
                    yield return Path.Combine(dir, exeName);
                }
            }

            // User-local binary directories
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home))
            {
                yield return Path.Combine(home, ".local", "bin", exeName);
                yield return Path.Combine(home, ".cargo", "bin", exeName);
            }

            // System directories (platform-specific)
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                yield return "/opt/homebrew/bin/" + exeName;
                yield return "/usr/local/bin/" + exeName;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                yield return "/usr/local/bin/" + exeName;
                yield return "/usr/bin/" + exeName;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

                if (!string.IsNullOrEmpty(localAppData))
                {
                    yield return Path.Combine(localAppData, "Programs", "uv", exeName);
                    // WinGet creates shim files in this location
                    yield return Path.Combine(localAppData, "Microsoft", "WinGet", "Links", exeName);
                }

                if (!string.IsNullOrEmpty(programFiles))
                {
                    yield return Path.Combine(programFiles, "uv", exeName);
                }
            }
        }
    }
}
