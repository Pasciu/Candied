using System;
using System.IO;
using Candied.Core.Models;

namespace Candied.Infrastructure;

internal static class CandiedWorkspaceStorage
{
    public static CandiedStorageLayout GetLayout(string? customRoot = null)
    {
        if (!string.IsNullOrWhiteSpace(customRoot))
        {
            return new CandiedStorageLayout(Path.GetFullPath(customRoot));
        }

        return new CandiedStorageLayout(ResolveRepositoryRoot());
    }

    public static string ResolveRepositoryRoot()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
        {
            if (File.Exists(Path.Combine(dir, "Candied.sln")) || Directory.Exists(Path.Combine(dir, ".git")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        return Directory.GetCurrentDirectory();
    }
}
