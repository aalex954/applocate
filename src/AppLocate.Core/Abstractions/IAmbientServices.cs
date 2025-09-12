using System;
using System.Collections.Generic;
using System.Threading;

namespace AppLocate.Core.Abstractions;

/// <summary>Abstractions for external environment (registry, file system, processes, Win32) to aid testability.</summary>
/// <summary>Abstraction over Windows registry queries needed by discovery sources.</summary>
public interface IRegistry
{
    /// <summary>
    /// Enumerates uninstall registry subkeys (<c>HKCU/HKLM\...\Uninstall</c>) honoring scope filters.
    /// Implementations should swallow per-key access errors and stop promptly when <paramref name="ct"/> is cancelled.
    /// </summary>
    /// <param name="user">Include per-user (HKCU) keys when true.</param>
    /// <param name="machine">Include machine (HKLM; 32/64) keys when true.</param>
    /// <param name="ct">Cancellation token to abort enumeration.</param>
    /// <returns>Sequence of lightweight registry key wrappers.</returns>
    IEnumerable<IRegistryKey> EnumerateUninstallKeys(bool user, bool machine, CancellationToken ct);

    /// <summary>
    /// Enumerates App Paths registry subkeys (<c>...\App Paths</c>) honoring scope filters for executable resolution.
    /// </summary>
    /// <param name="user">Include per-user (HKCU) keys.</param>
    /// <param name="machine">Include machine (HKLM) keys.</param>
    /// <param name="ct">Cancellation token.</param>
    IEnumerable<IRegistryKey> EnumerateAppPathKeys(bool user, bool machine, CancellationToken ct);
}

/// <summary>Lightweight read-only view of a registry key used during enumeration.</summary>
public interface IRegistryKey
{
    /// <summary>Reads a string value from the key; returns <c>null</c> if missing or not a string.</summary>
    /// <param name="name">Value name (null / empty for default value).</param>
    string? GetString(string name);

    /// <summary>Key name (leaf, not full path) for display/evidence.</summary>
    string Name { get; }
}

/// <summary>Abstraction for enumerating running OS processes.</summary>
public interface IProcessQuery
{
    /// <summary>
    /// Enumerates current processes (or a single PID) returning basic identifying data.
    /// Implementations should ignore access-denied processes and continue.
    /// </summary>
    /// <param name="singlePid">Optional specific PID to query instead of full enumeration.</param>
    /// <returns>Sequence of tuples containing PID, process name, and main module path (if accessible).</returns>
    IEnumerable<(int Pid, string Name, string? MainModulePath)> EnumerateProcesses(int? singlePid = null);
}

/// <summary>Facade for file system operations to ease substitution in tests.</summary>
public interface IFileSystemFacade
{
    /// <summary>Returns true if the file exists (cheap existence check).</summary>
    bool FileExists(string path);
    /// <summary>Returns true if the directory exists.</summary>
    bool DirectoryExists(string path);
    /// <summary>
    /// Enumerates files under <paramref name="root"/> matching <paramref name="pattern"/>.
    /// Implementations may yield lazily and should swallow per-directory access errors.
    /// </summary>
    /// <param name="root">Root directory to search.</param>
    /// <param name="pattern">Search pattern (e.g. <c>*.exe</c>).</param>
    /// <param name="recursive">When true, descend into subdirectories.</param>
    IEnumerable<string> EnumerateFiles(string root, string pattern, bool recursive);
}

/// <summary>Abstraction for querying installed MSIX / Store packages.</summary>
public interface IPackageQuery
{
    /// <summary>
    /// Asynchronously enumerates installed MSIX packages for the current user.
    /// Implementations should fail soft (skip) on individual package metadata errors.
    /// </summary>
    /// <param name="ct">Cancellation token to abort enumeration.</param>
    /// <returns>Async sequence of package tuples (family name, install path, display name, version).</returns>
    IAsyncEnumerable<(string FamilyName, string InstallLocation, string? DisplayName, string? Version)> EnumerateMsixAsync(CancellationToken ct);
}
