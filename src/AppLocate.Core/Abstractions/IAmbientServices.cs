using System;
using System.Collections.Generic;
using System.Threading;

namespace AppLocate.Core.Abstractions;

/// <summary>Abstractions for external environment (registry, file system, processes, Win32) to aid testability.</summary>
public interface IRegistry
{
    IEnumerable<IRegistryKey> EnumerateUninstallKeys(bool user, bool machine, CancellationToken ct);
    IEnumerable<IRegistryKey> EnumerateAppPathKeys(bool user, bool machine, CancellationToken ct);
}

public interface IRegistryKey
{
    string? GetString(string name);
    string Name { get; }
}

public interface IProcessQuery
{
    IEnumerable<(int Pid, string Name, string? MainModulePath)> EnumerateProcesses(int? singlePid = null);
}

public interface IFileSystemFacade
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    IEnumerable<string> EnumerateFiles(string root, string pattern, bool recursive);
}

public interface IPackageQuery
{
    IAsyncEnumerable<(string FamilyName, string InstallLocation, string? DisplayName, string? Version)> EnumerateMsixAsync(CancellationToken ct);
}
