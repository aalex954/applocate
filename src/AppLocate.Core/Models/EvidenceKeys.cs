namespace AppLocate.Core.Models;

/// <summary>Centralized constants for evidence dictionary keys to avoid typos and enable refactors.</summary>
public static class EvidenceKeys
{
    public const string DisplayName = nameof(DisplayName);
    public const string Key = nameof(Key);
    public const string WindowsInstaller = nameof(WindowsInstaller);
    public const string HasInstallLocation = nameof(HasInstallLocation);
    public const string HasDisplayIcon = nameof(HasDisplayIcon);

    public const string PATH = nameof(PATH);
    public const string WhereQuery = nameof(WhereQuery);
    public const string CollapsedMatch = nameof(CollapsedMatch);
    public const string VariantProbe = nameof(VariantProbe);
    public const string Root = nameof(Root);
    public const string DirMatch = nameof(DirMatch);
    public const string ExeName = nameof(ExeName);
    public const string FromService = nameof(FromService);
    public const string FromTask = nameof(FromTask);
    public const string FromExeDir = nameof(FromExeDir);

    public const string PackageFamilyName = nameof(PackageFamilyName);
    public const string PackageName = nameof(PackageName);
    public const string PackageVersion = nameof(PackageVersion);

    public const string Service = nameof(Service);
    public const string ServiceDisplayName = nameof(ServiceDisplayName);

    public const string TaskFile = nameof(TaskFile);
    public const string TaskName = nameof(TaskName);
    public const string Shortcut = nameof(Shortcut);
}
