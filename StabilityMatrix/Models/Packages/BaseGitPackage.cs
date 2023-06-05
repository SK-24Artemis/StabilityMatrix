﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using Octokit;
using StabilityMatrix.Helper;
using StabilityMatrix.Helper.Cache;
using StabilityMatrix.Python;
using StabilityMatrix.Services;
using ApiException = Refit.ApiException;

namespace StabilityMatrix.Models.Packages;

/// <summary>
/// Base class for packages that are hosted on Github.
/// Author and Name should be the Github username and repository name respectively.
/// </summary>
[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
public abstract class BaseGitPackage : BasePackage
{
    protected static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    protected readonly IGithubApiCache GithubApi;
    protected readonly ISettingsManager SettingsManager;
    protected readonly IDownloadService DownloadService;
    protected PyVenvRunner? VenvRunner;
    
    /// <summary>
    /// URL of the hosted web page on launch
    /// </summary>
    protected string WebUrl = string.Empty;

    public override string GithubUrl => $"https://github.com/{Author}/{Name}";

    public override string DownloadLocation =>
        $"{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}\\StabilityMatrix\\Packages\\{Name}.zip";

    public override string InstallLocation { get; set; } =
        $"{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}\\StabilityMatrix\\Packages";
    
    protected string GetDownloadUrl(string tagName, bool isCommitHash = false)
    {
        return isCommitHash
            ? $"https://github.com/{Author}/{Name}/archive/{tagName}.zip"
            : $"https://api.github.com/repos/{Author}/{Name}/zipball/{tagName}";
    }

    protected BaseGitPackage(IGithubApiCache githubApi, ISettingsManager settingsManager, IDownloadService downloadService)
    {
        GithubApi = githubApi;
        SettingsManager = settingsManager;
        this.DownloadService = downloadService;
    }

    protected Task<Release> GetLatestRelease()
    {
        return GithubApi.GetLatestRelease(Author, Name);
    }
    
    public override Task<IReadOnlyList<Branch>> GetAllBranches()
    {
        return GithubApi.GetAllBranches(Author, Name);
    }
    
    public override Task<IOrderedEnumerable<Release>> GetAllReleases()
    {
        return GithubApi.GetAllReleases(Author, Name);
    }
    
    public override Task<IReadOnlyList<GitHubCommit>?> GetAllCommits(string branch, int page = 1, int perPage = 10)
    {
        return GithubApi.GetAllCommits(Author, Name, branch, page, perPage);
    }

    /// <summary>
    /// Setup the virtual environment for the package.
    /// </summary>
    /// <param name="installedPackagePath"></param>
    /// <param name="venvName"></param>
    /// <returns></returns>
    protected async Task<PyVenvRunner> SetupVenv(string installedPackagePath, string venvName = "venv")
    {
        var venvPath = Path.Combine(installedPackagePath, "venv");
        VenvRunner?.Dispose();
        VenvRunner = new PyVenvRunner(venvPath);
        if (!VenvRunner.Exists())
        {
            await VenvRunner.Setup();
        }
        return VenvRunner;
    }
    
    public override async Task<IOrderedEnumerable<Release>> GetReleaseTags()
    {
        var allReleases = await GithubApi.GetAllReleases(Author, Name);
        return allReleases;
    }
    
    public override async Task<string?> DownloadPackage(string version, bool isCommitHash, bool isUpdate = false)
    {
        var downloadUrl = GetDownloadUrl(version, isCommitHash);

        if (!Directory.Exists(DownloadLocation.Replace($"{Name}.zip", "")))
        {
            Directory.CreateDirectory(DownloadLocation.Replace($"{Name}.zip", ""));
        }

        var progress = new Progress<ProgressReport>(progress =>
        {
            DownloadServiceOnDownloadProgressChanged(progress, isUpdate);
        });
        
        await DownloadService.DownloadToFileAsync(downloadUrl, DownloadLocation, progress: progress);
        DownloadServiceOnDownloadFinished(new ProgressReport(100, "Download Complete"), isUpdate);
        
        return version;
    }

    private void DownloadServiceOnDownloadProgressChanged(ProgressReport progress, bool isUpdate)
    {
        if (isUpdate)
        {
            OnUpdateProgressChanged(Convert.ToInt32(progress.Progress));
        }
        else
        {
            OnDownloadProgressChanged(Convert.ToInt32(progress.Progress));
        }
    }
    
    private void DownloadServiceOnDownloadFinished(ProgressReport downloadLocation, bool isUpdate)
    {
        if (isUpdate)
        {
            OnUpdateComplete(downloadLocation.Message);
        }
        else
        {
            OnDownloadComplete(downloadLocation.Message);
        }
    }

    protected void UnzipPackage(bool isUpdate = false)
    {
        if (isUpdate)
        {
            OnInstallProgressChanged(0);
        }
        else
        {
            OnUpdateProgressChanged(0);
        }

        Directory.CreateDirectory(InstallLocation);

        using var zip = ZipFile.OpenRead(DownloadLocation);
        var zipDirName = string.Empty;
        var totalEntries = zip.Entries.Count;
        var currentEntry = 0;

        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name) && entry.FullName.EndsWith("/"))
            {
                if (string.IsNullOrWhiteSpace(zipDirName))
                {
                    zipDirName = entry.FullName;
                    continue;
                }

                var folderPath = Path.Combine(InstallLocation,
                    entry.FullName.Replace(zipDirName, string.Empty));
                Directory.CreateDirectory(folderPath);
                continue;
            }


            var destinationPath = Path.GetFullPath(Path.Combine(InstallLocation,
                entry.FullName.Replace(zipDirName, string.Empty)));
            entry.ExtractToFile(destinationPath, true);
            currentEntry++;

            var progressValue = (int)((double)currentEntry / totalEntries * 100);

            if (isUpdate)
            {
                OnUpdateProgressChanged(progressValue);
            }
            else
            {
                OnInstallProgressChanged(progressValue);
            }
        }
    }
    
    public override Task InstallPackage(bool isUpdate = false)
    {
        UnzipPackage(isUpdate);
        
        if (isUpdate)
        {
            OnUpdateComplete("Update complete");
        }
        else
        {
            OnInstallComplete("Installation complete");
        }

        return Task.CompletedTask;
    }

    public override async Task<bool> CheckForUpdates(string installedPackageName)
    {
        var package =
            SettingsManager.Settings.InstalledPackages.FirstOrDefault(x => x.DisplayName == installedPackageName);
        var currentVersion = package?.PackageVersion;
        if (package == null || string.IsNullOrWhiteSpace(currentVersion))
        {
            return false;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(package.InstalledBranch))
            {
                var latestVersion = await GetLatestVersion();
                UpdateAvailable = latestVersion != currentVersion;
                return latestVersion != currentVersion;
            }
            else
            {
                var allCommits = await GetAllCommits(package.InstalledBranch);
                var latestCommitHash = allCommits.First().Sha;
                return latestCommitHash != currentVersion;
            }
            
        }
        catch (ApiException e)
        {
            Logger.Error(e, "Failed to check for updates");
            return false;
        }
    }

    public override async Task<string> Update(InstalledPackage installedPackage)
    {
        if (string.IsNullOrWhiteSpace(installedPackage.InstalledBranch))
        {
            var releases = await GetAllReleases();
            var latestRelease = releases.First();
            await DownloadPackage(latestRelease.TagName, false, true);
            await InstallPackage(true);
            return latestRelease.TagName;
        }
        else
        {
            var allCommits = await GetAllCommits(installedPackage.InstalledBranch);
            var latestCommit = allCommits.First();
            await DownloadPackage(latestCommit.Sha, true, true);
            await InstallPackage(true);
            return latestCommit.Sha;
        }
    }

    
    public override Task Shutdown()
    {
        VenvRunner?.Dispose();
        VenvRunner?.Process?.WaitForExitAsync();
        return Task.CompletedTask;
    }
}
