using System.ComponentModel;
using System.Diagnostics;
using CodexGui.AppServer.Models;

namespace CodexGui.App.Services;

internal interface IGitDiffService
{
    string BuildLocalGitDiff(ThreadItem item, string workingDirectory);
}

internal sealed class GitDiffService : IGitDiffService
{
    public string BuildLocalGitDiff(ThreadItem item, string workingDirectory)
    {
        if (item.Changes is not { Count: > 0 } changes)
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
        {
            return string.Empty;
        }

        var paths = changes
            .Select(static change => change.Path?.Trim())
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => path!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (paths.Length == 0)
        {
            return string.Empty;
        }

        var unstagedDiff = RunGitDiffCommand(workingDirectory, includeStagedChanges: false, paths);
        var stagedDiff = RunGitDiffCommand(workingDirectory, includeStagedChanges: true, paths);

        if (string.IsNullOrWhiteSpace(unstagedDiff))
        {
            return stagedDiff;
        }

        if (string.IsNullOrWhiteSpace(stagedDiff))
        {
            return unstagedDiff;
        }

        return $"{unstagedDiff.TrimEnd()}\n\n{stagedDiff.TrimEnd()}";
    }

    private static string RunGitDiffCommand(string workingDirectory, bool includeStagedChanges, IReadOnlyList<string> paths)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("--no-pager");
        startInfo.ArgumentList.Add("diff");
        if (includeStagedChanges)
        {
            startInfo.ArgumentList.Add("--cached");
        }

        startInfo.ArgumentList.Add("--");
        foreach (var path in paths)
        {
            startInfo.ArgumentList.Add(path);
        }

        using var process = new Process
        {
            StartInfo = startInfo
        };

        try
        {
            if (!process.Start())
            {
                return string.Empty;
            }
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
        catch (Win32Exception)
        {
            return string.Empty;
        }

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            if (!string.IsNullOrWhiteSpace(error)
                && !error.Contains("not a git repository", StringComparison.OrdinalIgnoreCase)
                && !error.Contains("unknown revision or path", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return string.Empty;
        }

        return output.TrimEnd();
    }
}
