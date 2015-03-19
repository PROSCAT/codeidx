﻿using CodeIDX.Helpers;
using CodeIDX.Services;
using CodeIDX.Services.Lucene;
using CodeIDX.Settings;
using CodeIDX.ViewModels.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace CodeIDX.ViewModels
{
    public class ApplicationViewModel : ViewModel
    {

        private bool _PreviewSaved;
        private bool _OperationCancelled;
        private CancellationTokenSource _CurrentOperationCancellationTokenSource;
        private bool _StatusChangeAttempted;
        private bool _IsReady;
        private SearchViewModel _CurrentSearch;
        private bool _AutoUpdateIndex;
        private StatusKind _Status;
        private IndexViewModel _CurrentIndexFile;

        public FileWatcherService FileWatcher { get; private set; }
        public UserSettingsViewModel UserSettings { get; private set; }

        private ObservableCollection<string> _SearchHistory;
        public ReadOnlyObservableCollection<string> SearchHistory { get; private set; }

        private CancellationTokenSource CurrentOperationCancellationTokenSource
        {
            get
            {
                return _CurrentOperationCancellationTokenSource;
            }
            set
            {
                if (_CurrentOperationCancellationTokenSource != value)
                {
                    _CurrentOperationCancellationTokenSource = value;
                    FirePropertyChanged("CanCurrentOperationCancel");
                }
            }
        }

        public bool PreviewSaved
        {
            get
            {
                return _PreviewSaved;
            }
            private set
            {
                if (_PreviewSaved != value)
                {
                    _PreviewSaved = value;
                    FirePropertyChanged("PreviewSaved");
                }
            }
        }

        public bool OperationCancelled
        {
            get
            {
                return _OperationCancelled;
            }
            private set
            {
                if (_OperationCancelled != value)
                {
                    _OperationCancelled = value;
                    FirePropertyChanged("OperationCancelled");
                }
            }
        }

        public bool StatusChangeAttempted
        {
            get
            {
                return _StatusChangeAttempted;
            }
            private set
            {
                if (_StatusChangeAttempted != value)
                {
                    _StatusChangeAttempted = value;
                    FirePropertyChanged("StatusChangeAttempted");
                }
            }
        }

        public bool CanCurrentOperationCancel
        {
            get
            {
                return CurrentOperationCancellationTokenSource != null;
            }
        }

        public bool IsReady
        {
            get
            {
                return _IsReady;
            }
            private set
            {
                _IsReady = value;
                FirePropertyChanged("IsReady");
            }
        }

        public StatusKind Status
        {
            get
            {
                return _Status;
            }
            private set
            {
                _Status = value;
                IsReady = (Status == StatusKind.Ready);

                FirePropertyChanged("Status");
            }
        }

        public SearchViewModel CurrentSearch
        {
            get
            {
                return _CurrentSearch;
            }
            set
            {
                if (_CurrentSearch != value)
                {
                    _CurrentSearch = value;
                    FirePropertyChanged("CurrentSearch");
                }
            }
        }

        public IndexViewModel CurrentIndexFile
        {
            get
            {
                return _CurrentIndexFile;
            }
            set
            {
                if (_CurrentIndexFile != value)
                {
                    _CurrentIndexFile = value;
                    FirePropertyChanged("CurrentIndexFile");
                }
            }
        }

        public bool AutoUpdateIndex
        {
            get
            {
                return _AutoUpdateIndex;
            }
            set
            {
                if (_AutoUpdateIndex != value)
                {
                    _AutoUpdateIndex = value;

                    if (FileWatcher != null)
                        FileWatcher.IsEnabled = _AutoUpdateIndex;
                }
            }
        }

        private ObservableCollection<SearchViewModel> _Searches;
        public ReadOnlyObservableCollection<SearchViewModel> Searches { get; private set; }

        public bool HasValidIndexDirectory
        {
            get
            {
                return CurrentIndexFile != null && LuceneHelper.IsValidIndexDirectory(CurrentIndexFile.IndexDirectory);
            }
        }

        public ApplicationViewModel()
        {
            Status = StatusKind.Ready;
            FileWatcher = new FileWatcherService();
            AutoUpdateIndex = true;

            UserSettings = new UserSettingsViewModel();
            UserSettings.Init();

            _Searches = new ObservableCollection<SearchViewModel>();
            Searches = new ReadOnlyObservableCollection<SearchViewModel>(_Searches);

            _SearchHistory = new ObservableCollection<string>(CodeIDXSettings.Default.SearchHistory);
            SearchHistory = new ReadOnlyObservableCollection<string>(_SearchHistory);

            AddSearch();
        }

        public void AddSearch()
        {
            SearchViewModel newSearch = new SearchViewModel
            {
                IsFilterEnabled = CodeIDXSettings.Search.EnableFilterByDefault
            };

            var availableFileFilters = LuceneSearcher.Instance.GetAvailableFileFilters(CurrentIndexFile).ToList();
            newSearch.UpdateAvailableFileFilters(availableFileFilters);
            _Searches.Add(newSearch);

            CurrentSearch = newSearch;
        }

        public async void SignalPreviewSaved()
        {
            Status = StatusKind.Saved;
            await Task.Delay(1000);
            Status = StatusKind.Ready;
        }

        public void SignalOperationCancelled()
        {
            OperationCancelled = true;
            OperationCancelled = false;
        }

        public void SignalOperationInProgress()
        {
            StatusChangeAttempted = true;
            StatusChangeAttempted = false;
        }

        internal void RemoveSearch(SearchViewModel search)
        {
            if (Searches.Count > 1)
                _Searches.Remove(search);
            else if (Searches.Count == 1)
                Searches[0].Reset();
        }

        internal void UpdateSearchFilter()
        {
            var availableFileFilters = LuceneSearcher.Instance.GetAvailableFileFilters(CurrentIndexFile).ToList();
            foreach (var search in Searches)
                search.UpdateAvailableFileFilters(availableFileFilters);
        }

        internal void CancelCurrentOperation()
        {
            if (IsReady || CurrentOperationCancellationTokenSource == null)
                return;

            CurrentOperationCancellationTokenSource.Cancel();
        }


        internal bool BeginOperation(StatusKind operationKind)
        {
            if (!IsReady)
            {
                SignalOperationInProgress();
                return false;
            }

            Status = operationKind;
            return true;
        }

        internal bool BeginOperation(StatusKind operationStatus, out CancellationToken cancelToken)
        {
            if (!IsReady)
            {
                SignalOperationInProgress();
                return false;
            }

            Status = operationStatus;

            CurrentOperationCancellationTokenSource = new CancellationTokenSource();
            cancelToken = CurrentOperationCancellationTokenSource.Token;

            return true;
        }

        public void EndOperation()
        {
            Status = StatusKind.Ready;

            if (CurrentOperationCancellationTokenSource != null &&
                CurrentOperationCancellationTokenSource.Token.IsCancellationRequested)
            {
                SignalOperationCancelled();
            }

            CurrentOperationCancellationTokenSource = null;
        }

        internal void AddToSearchHistory(string searchText)
        {
            if (!CodeIDXSettings.Search.EnableSearchHistory)
                return;

            if (string.IsNullOrEmpty(searchText))
                return;

            if (SearchHistory.Contains(searchText))
            {
                int index = SearchHistory.IndexOf(searchText);
                if (index != 0)
                    _SearchHistory.Move(index, 0);
            }
            else
            {
                _SearchHistory.Insert(0, searchText);
                while (_SearchHistory.Count > 20)
                    _SearchHistory.Remove(_SearchHistory.LastOrDefault());
            }
        }

        internal void SaveSettings()
        {
            CodeIDXSettings.Default.SearchHistory = SearchHistory.ToList();
            UserSettings.Save();
        }
    }
}
