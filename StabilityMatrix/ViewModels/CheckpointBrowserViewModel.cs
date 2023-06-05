﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NLog;
using StabilityMatrix.Api;
using StabilityMatrix.Models;
using StabilityMatrix.Models.Api;
using StabilityMatrix.Services;

namespace StabilityMatrix.ViewModels;

public partial class CheckpointBrowserViewModel : ObservableObject
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly ICivitApi civitApi;
    private readonly IDownloadService downloadService;
    private const int MaxModelsPerPage = 14;

    [ObservableProperty] private string? searchQuery;
    [ObservableProperty] private ObservableCollection<CheckpointBrowserCardViewModel>? modelCards;
    [ObservableProperty] private bool showNsfw;
    [ObservableProperty] private bool showMainLoadingSpinner;
    [ObservableProperty] private CivitPeriod selectedPeriod;
    [ObservableProperty] private CivitSortMode sortMode;
    [ObservableProperty] private int currentPageNumber;
    [ObservableProperty] private int totalPages;
    [ObservableProperty] private bool hasSearched;
    [ObservableProperty] private bool canGoToNextPage;
    [ObservableProperty] private bool canGoToPreviousPage;
    [ObservableProperty] private bool isIndeterminate;

    public IEnumerable<CivitPeriod> AllCivitPeriods => Enum.GetValues(typeof(CivitPeriod)).Cast<CivitPeriod>();
    public IEnumerable<CivitSortMode> AllSortModes => Enum.GetValues(typeof(CivitSortMode)).Cast<CivitSortMode>();

    public CheckpointBrowserViewModel(ICivitApi civitApi, IDownloadService downloadService)
    {
        this.civitApi = civitApi;
        this.downloadService = downloadService;
        
        SelectedPeriod = CivitPeriod.Month;
        SortMode = CivitSortMode.HighestRated;
        HasSearched = false;
        CurrentPageNumber = 1;
        CanGoToPreviousPage = false;
        CanGoToNextPage = true;
    }

    [RelayCommand]
    private async Task SearchModels()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            return;
        }
        
        ShowMainLoadingSpinner = true;

        var models = await civitApi.GetModels(new CivitModelsRequest
        {
            Query = SearchQuery,
            Limit = MaxModelsPerPage,
            Nsfw = ShowNsfw.ToString().ToLower(),
            Sort = SortMode,
            Period = SelectedPeriod,
            Page = CurrentPageNumber
        });

        HasSearched = true;
        TotalPages = models.Metadata.TotalPages;
        CanGoToPreviousPage = CurrentPageNumber > 1;
        CanGoToNextPage = CurrentPageNumber < TotalPages;
        ModelCards = new ObservableCollection<CheckpointBrowserCardViewModel>(models.Items.Select(
            m => new CheckpointBrowserCardViewModel(m, downloadService)));
        ShowMainLoadingSpinner = false;

        Logger.Debug($"Found {models.Items.Length} models");
    }

    [RelayCommand]
    private async Task PreviousPage()
    {
        if (CurrentPageNumber == 1) return;

        CurrentPageNumber--;
        await TrySearchAgain(false);
    }

    [RelayCommand]
    private async Task NextPage()
    {
        CurrentPageNumber++;
        await TrySearchAgain(false);
    }

    partial void OnShowNsfwChanged(bool oldValue, bool newValue)
    {
        TrySearchAgain();
    }

    partial void OnSelectedPeriodChanged(CivitPeriod oldValue, CivitPeriod newValue)
    {
        TrySearchAgain();
    }

    partial void OnSortModeChanged(CivitSortMode oldValue, CivitSortMode newValue)
    {
        TrySearchAgain();
    }

    private async Task TrySearchAgain(bool shouldUpdatePageNumber = true)
    {
        if (!hasSearched) return;
        ModelCards?.Clear();

        if (shouldUpdatePageNumber)
        {
            CurrentPageNumber = 1;
        }

        // execute command instead of calling method directly so that the IsRunning property gets updated
        await SearchModelsCommand.ExecuteAsync(null);
    }
}
