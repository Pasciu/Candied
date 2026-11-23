using System;
using System.IO;
using Candied.Core.Models;

namespace Candied.Workspace;

internal static class CandiedWorkspaceStorage
{
    public static CandiedStorageLayout GetLayout()
    {
        var root = ResolveWorkspaceRoot();
        return new CandiedStorageLayout(root);
    }

    public static string ResolveWorkspaceRoot(string? currentDirectory = null, string? baseDirectory = null)
    {
        var envRoot = Environment.GetEnvironmentVariable("CANDIED_WORKSPACE_ROOT");
        if (!string.IsNullOrWhiteSpace(envRoot) && Directory.Exists(envRoot))
        {
            return Path.GetFullPath(envRoot);
        }

        var cwd = !string.IsNullOrWhiteSpace(currentDirectory) ? currentDirectory : Directory.GetCurrentDirectory();
        var appBase = !string.IsNullOrWhiteSpace(baseDirectory) ? baseDirectory : AppContext.BaseDirectory;

        // 1. Prefer an explicit repository root (Candied.sln or .git) walking up from currentDirectory
        var repoFromCwd = FindAncestorWithRepoMarker(cwd);
        if (repoFromCwd != null)
        {
            return repoFromCwd;
        }

        // 2. When spawned by an MCP client (e.g. Antigravity, Cursor, Claude Desktop), currentDirectory
        //    may be the IDE installation directory. Walk up from the executable's AppContext.BaseDirectory
        //    (e.g. <RepoRoot>/publish/) to locate the repository root.
        var repoFromBase = FindAncestorWithRepoMarker(appBase);
        if (repoFromBase != null)
        {
            return repoFromBase;
        }

        // 3. Fallback to any ancestor of currentDirectory containing .candied
        var candiedFromCwd = FindAncestorWithCandiedDir(cwd);
        if (candiedFromCwd != null)
        {
            return candiedFromCwd;
        }

        return Path.GetFullPath(cwd);
    }

    private static string? FindAncestorWithRepoMarker(string? startPath)
    {
        if (string.IsNullOrWhiteSpace(startPath))
        {
            return null;
        }

        try
        {
            var dir = new DirectoryInfo(startPath);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Candied.sln")) ||
                    Directory.Exists(Path.Combine(dir.FullName, ".git")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }
        }
        catch
        {
            // Ignore invalid paths or permission errors during traversal
        }

        return null;
    }

    private static string? FindAncestorWithCandiedDir(string? startPath)
    {
        if (string.IsNullOrWhiteSpace(startPath))
        {
            return null;
        }

        try
        {
            var dir = new DirectoryInfo(startPath);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, ".candied")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }
        }
        catch
        {
            // Ignore invalid paths or permission errors during traversal
        }

        return null;
    }
}
