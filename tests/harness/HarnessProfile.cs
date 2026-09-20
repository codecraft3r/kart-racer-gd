using Godot;
using System;
using System.IO;

/// <summary>
/// Opt-in path resolver for authoritative harness runs. Normal gameplay keeps using
/// Godot's user:// paths; a harness process supplies an explicit directory before the
/// scene enters the tree so settings, bindings, and records cannot touch the player's
/// profile.
/// </summary>
public static class HarnessProfile
{
    private static string _directory = string.Empty;

    public static bool IsConfigured => !string.IsNullOrEmpty(_directory);
    public static string DirectoryPath => _directory;

    public static void Configure(string profileDirectory)
    {
        if (string.IsNullOrWhiteSpace(profileDirectory))
            return;

        string resolved = profileDirectory.Trim();
        if (resolved.StartsWith("res://", StringComparison.OrdinalIgnoreCase) ||
            resolved.StartsWith("user://", StringComparison.OrdinalIgnoreCase))
        {
            resolved = ProjectSettings.GlobalizePath(resolved);
        }

        resolved = Path.GetFullPath(resolved);
        System.IO.Directory.CreateDirectory(resolved);
        _directory = resolved;
        GD.Print($"HarnessProfile: isolated user data at {_directory}");
    }

    public static string Resolve(string godotUserPath)
    {
        if (!IsConfigured || string.IsNullOrEmpty(godotUserPath) ||
            !godotUserPath.StartsWith("user://", StringComparison.OrdinalIgnoreCase))
            return godotUserPath;

        string relative = godotUserPath.Substring("user://".Length)
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        return Path.Combine(_directory, relative);
    }
}
