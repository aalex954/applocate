namespace AppLocate.Core.Models {
    /// <summary>Centralized constants for evidence dictionary keys to avoid typos and enable refactors.</summary>
    public static class EvidenceKeys {
        /// <summary>Display name of an app (e.g., from uninstall registry key or shortcut).</summary>
        public const string DisplayName = nameof(DisplayName);
        /// <summary>Registry key name, package id, or unique identifier contributing evidence.</summary>
        public const string Key = nameof(Key);
        /// <summary>Indicates Windows Installer (MSI) metadata was found for this hit.</summary>
        public const string WindowsInstaller = nameof(WindowsInstaller);
        /// <summary>InstallLocation value present and resolved to an existing directory.</summary>
        public const string HasInstallLocation = nameof(HasInstallLocation);
        /// <summary>DisplayIcon path present and file exists.</summary>
        public const string HasDisplayIcon = nameof(HasDisplayIcon);

        /// <summary>Executable discovered via PATH search or environment probing.</summary>
        public const string PATH = nameof(PATH);
        /// <summary>Raw query used in a where.exe lookup or equivalent.</summary>
        public const string WhereQuery = nameof(WhereQuery);
        /// <summary>Collapsed alias match (spaces/punctuation removed) matched candidate.</summary>
        public const string CollapsedMatch = nameof(CollapsedMatch);
        /// <summary>Variant probe (e.g., alternate exe name) produced match.</summary>
        public const string VariantProbe = nameof(VariantProbe);
        /// <summary>Root directory candidate matched vendor/app naming heuristic.</summary>
        public const string Root = nameof(Root);
        /// <summary>Intermediate directory segment matched query tokens.</summary>
        public const string DirMatch = nameof(DirMatch);
        /// <summary>Executable filename matched query tokens or alias.</summary>
        public const string ExeName = nameof(ExeName);
        /// <summary>Derived from a Windows Service image path.</summary>
        public const string FromService = nameof(FromService);
        /// <summary>Derived from a Scheduled Task action path.</summary>
        public const string FromTask = nameof(FromTask);
        /// <summary>Candidate path resides in same directory as a known executable hit.</summary>
        public const string FromExeDir = nameof(FromExeDir);

        /// <summary>MSIX/Store package family name evidence.</summary>
        public const string PackageFamilyName = nameof(PackageFamilyName);
        /// <summary>Package name (e.g., Winget/Store/Scoop/Choco manifest id).</summary>
        public const string PackageName = nameof(PackageName);
        /// <summary>Package version string sourced from registry or package manager.</summary>
        public const string PackageVersion = nameof(PackageVersion);

        /// <summary>Service name (short name) associated with candidate path.</summary>
        public const string Service = nameof(Service);
        /// <summary>Human friendly service display name associated with candidate path.</summary>
        public const string ServiceDisplayName = nameof(ServiceDisplayName);

        /// <summary>Scheduled Task XML file path evidence.</summary>
        public const string TaskFile = nameof(TaskFile);
        /// <summary>Scheduled Task name evidence.</summary>
        public const string TaskName = nameof(TaskName);
        /// <summary>Shortcut (.lnk) file provided evidence (target or name match).</summary>
        public const string Shortcut = nameof(Shortcut);
    }
}
