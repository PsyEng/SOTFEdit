﻿using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using MahApps.Metro.Controls.Dialogs;
using NLog;
using SOTFEdit.Infrastructure.Converters;
using SOTFEdit.Model;
using SOTFEdit.Model.Actors;
using SOTFEdit.Model.Events;
using SOTFEdit.Model.Savegame;
using SOTFEdit.ViewModel;

namespace SOTFEdit.View;

/// <summary>
///     Interaction logic for MainWindow.xaml
/// </summary>
// ReSharper disable once UnusedMember.Global
public partial class MainWindow
{
    private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

    private readonly string _baseTitle;

    private readonly CoordinateConverter _coordinateConverter = new();
    private readonly MainViewModel _dataContext;

    private Window? _exceptionWindowOwner;
    private int? _selectedPosSenderHash;

    public MainWindow()
    {
        SetupListeners();
        DataContext = _dataContext = Ioc.Default.GetRequiredService<MainViewModel>();
        InitializeComponent();

        App.GetAssemblyVersion(out var assemblyName, out var assemblyVersion);

        _baseTitle = $"{assemblyName} v{assemblyVersion}";
        Title = _baseTitle;

        Loaded += OnLoaded;
    }

    private void SetupListeners()
    {
        WeakReferenceMessenger.Default.Register<SavegameStoredEvent>(this,
            (_, message) => { OnSavegameStored(message); });
        WeakReferenceMessenger.Default.Register<RequestRegrowTreesEvent>(this,
            (_, _) => { OnRequestRegrowTreesEvent(); });
        WeakReferenceMessenger.Default.Register<RequestReviveFollowersEvent>(this,
            (_, message) => { OnRequestReviveFollowersEvent(message); });
        WeakReferenceMessenger.Default.Register<RequestSaveChangesEvent>(this,
            (_, message) => { OnRequestSaveChangesEvent(message); });
        WeakReferenceMessenger.Default.Register<RequestSelectSavegameEvent>(this,
            (_, _) => { OnRequestSelectSavegameEvent(); });
        WeakReferenceMessenger.Default.Register<RequestApplicationExitEvent>(this,
            (_, _) => { OnRequestApplicationExitEvent(); });
        WeakReferenceMessenger.Default.Register<SelectedSavegameChangedEvent>(this,
            (_, message) => { OnSelectedSavegameChangedEvent(message.SelectedSavegame); });
        WeakReferenceMessenger.Default.Register<RequestStartProcessEvent>(this,
            (_, message) => { OnRequestStartProcessEvent(message); });
        WeakReferenceMessenger.Default.Register<RequestDeleteBackupsEvent>(this,
            (_, message) => { OnRequestDeleteBackupsEvent(message); });
        WeakReferenceMessenger.Default.Register<RequestCheckForUpdatesEvent>(this,
            (_, message) => { OnRequestCheckForUpdatesEvent(message); });
        WeakReferenceMessenger.Default.Register<VersionCheckResultEvent>(this,
            (_, message) => { OnVersionCheckResultEvent(message); });
        WeakReferenceMessenger.Default.Register<RequestSpawnFollowerEvent>(this,
            (_, message) => { OnRequestSpawnFollowerEvent(message); });
        WeakReferenceMessenger.Default.Register<RequestRestoreBackupsEvent>(this,
            (_, message) => { OnRequestRestoreBackupsEvent(message); });
        WeakReferenceMessenger.Default.Register<UnhandledExceptionEvent>(this,
            (_, message) => { OnUnhandledExceptionEvent(message); });

        WeakReferenceMessenger.Default.Register<ZoomToPosEvent>(this, (_, message) => { OnZoomToPos(message); });
        WeakReferenceMessenger.Default.Register<RequestChangeSettingsEvent>(this,
            (_, _) => { OnRequestChangeSettingsEvent(); });
        WeakReferenceMessenger.Default.Register<RequestEditActorEvent>(this,
            (_, message) => { OnRequestEditActorEvent(message); });
        WeakReferenceMessenger.Default.Register<GenericMessageEvent>(this,
            (_, message) => { OnGenericMessageEvent(message); });
        WeakReferenceMessenger.Default.Register<RequestModifyConsumedItemsEvent>(this,
            (_, _) => OnRequestModifyConsumedItemsEvent());
        WeakReferenceMessenger.Default.Register<RequestTeleportWorldItemEvent>(this,
            (_, _) => OnRequestTeleportWorldItemEvent());
    }

    private void OnRequestTeleportWorldItemEvent()
    {
        if (SavegameManager.SelectedSavegame is not { } selectedSavegame)
        {
            return;
        }

        var window = new WorldItemTeleportWindow(this, Ioc.Default.GetRequiredService<GameData>(), selectedSavegame);
        window.Show();
    }

    private void OnRequestModifyConsumedItemsEvent()
    {
        if (SavegameManager.SelectedSavegame is not { } selectedSavegame)
        {
            return;
        }

        var window = new ModifyConsumedItemsWindow(this, selectedSavegame, Ioc.Default.GetRequiredService<GameData>());
        window.ShowDialog();
    }

    private async void OnGenericMessageEvent(GenericMessageEvent message)
    {
        await ShowMessageDialog(message.Message, message.Title);
    }

    private void OnRequestEditActorEvent(RequestEditActorEvent message)
    {
        var window = new EditActorWindow(this, message.Actor);
        window.ShowDialog();
    }

    private void OnRequestChangeSettingsEvent()
    {
        var applicationSettings = Ioc.Default.GetRequiredService<ApplicationSettings>();
        var dialog = new SettingsDialog(this, applicationSettings);
        dialog.ShowDialog();
    }

    private void OnZoomToPos(ZoomToPosEvent message)
    {
        var senderHash = RuntimeHelpers.GetHashCode(message.Pos);

        if (senderHash == _selectedPosSenderHash)
        {
            MapFlyout.IsOpen = false;
            _selectedPosSenderHash = null;
            return;
        }

        var ingameToPixel = _coordinateConverter.IngameToPixel(message.Pos.X, message.Pos.Z);
        ZoomCtrl.Zoom = 2;
        ZoomCtrl.TranslateX = -2 * ingameToPixel.Item1 + ZoomCtrl.ActualWidth;
        ZoomCtrl.TranslateY = -2 * ingameToPixel.Item2 + ZoomCtrl.ActualHeight;
        _dataContext.PinLeft = ingameToPixel.Item1 - 16;
        _dataContext.PinTop = ingameToPixel.Item2 - 18;
        _dataContext.PinPos = message.Pos;

        _selectedPosSenderHash = senderHash;
        MapFlyout.IsOpen = true;
    }

    private void OnUnhandledExceptionEvent(UnhandledExceptionEvent message)
    {
        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            var unhandledExceptionWindow = new UnhandledExceptionWindow(_exceptionWindowOwner, message.Exception);
            unhandledExceptionWindow.ShowDialog();
        }));
    }

    private async void OnRequestRestoreBackupsEvent(RequestRestoreBackupsEvent message)
    {
        var result = await ShowConfirmDialog("Are you sure that you want to restore backups?", "Restore backups");
        if (result != MessageDialogResult.Affirmative)
        {
            return;
        }

        var countRestored =
            message.Savegame.SavegameStore.RestoreBackups(message.RestoreFromNewest, ApplicationSettings.BackupFlags);

        if (countRestored > 0)
        {
            _dataContext.ReloadSavegameCommand.Execute(null);
        }

        await ShowMessageDialog(
            string.Format(SOTFEdit.Resources.BackupsRestoredMessage, countRestored),
            SOTFEdit.Resources.BackupsRestoredTitle);
    }

    private async void OnRequestSpawnFollowerEvent(RequestSpawnFollowerEvent message)
    {
        var dialog = new SpawnFollowerInputDialog(this)
        {
            Max = message.TypeId switch
            {
                Constants.Actors.KelvinTypeId => 20,
                Constants.Actors.VirginiaTypeId => 4,
                _ => 1
            }
        };

        if (dialog.ShowDialog() is not true || dialog.Count is not { } count || count < 1)
        {
            return;
        }

        if (message.Savegame.SavegameStore.LoadJsonRaw(SavegameStore.FileType.SaveData) is not { } saveDataWrapper)
        {
            Logger.Warn("Save data could not be loaded");
            await ShowMessageDialog("Unable to spawn followers", "Error");
            return;
        }

        var followerModifier = new FollowerModifier(saveDataWrapper);
        var hasChanges =
            followerModifier.CreateFollowers(message.TypeId, count, message.ItemIds, message.Outfit, message.Pos);

        if (hasChanges)
        {
            await ShowMessageDialog($"{count} followers have been created", "Followers created");
        }
    }

    private async void OnVersionCheckResultEvent(VersionCheckResultEvent message)
    {
        if (!message.InvokedManually && message.LatestTagVersion?.ToString() is { } latestTagVersionAsString)
        {
            if (!message.InvokedManually && latestTagVersionAsString.Equals(Settings.Default.LastFoundVersion))
            {
                return;
            }

            Settings.Default.LastFoundVersion = latestTagVersionAsString;
            Settings.Default.Save();
        }

        if (message.IsError)
        {
            await Application.Current.Dispatcher.Invoke(async () =>
                await ShowMessageDialog("An error occured while checking for latest version", "Error"));
            return;
        }

        if (!message.IsNewer)
        {
            await Application.Current.Dispatcher.Invoke(async () =>
                await ShowMessageDialog("You are already using the latest version", "No update"));
            return;
        }

        Application.Current.Dispatcher.Invoke(() =>
        {
            var window = new UpdateAvailableWindow(this, message);
            window.ShowDialog();
        });
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _exceptionWindowOwner = this;
        if (Settings.Default.CheckForUpdates)
        {
            CheckForUpdate(false, false, false);
        }
    }

    private static void OnRequestCheckForUpdatesEvent(RequestCheckForUpdatesEvent message)
    {
        CheckForUpdate(message.NotifyOnSameVersion, message.NotifyOnError, true);
    }

    private static void CheckForUpdate(bool notifyOnSameVersion, bool notifyOnError, bool invokedManually)
    {
        Ioc.Default.GetRequiredService<UpdateChecker>()
            .CheckForUpdates(notifyOnSameVersion, notifyOnError, invokedManually);
    }

    private async void OnRequestDeleteBackupsEvent(RequestDeleteBackupsEvent message)
    {
        var result = await ShowConfirmDialog("Do you really want to delete all backups?", "Delete all backups");
        if (result != MessageDialogResult.Affirmative)
        {
            return;
        }

        try
        {
            var countDeleted = message.Savegame.SavegameStore.DeleteBackups(ApplicationSettings.BackupFlags);
            await ShowMessageDialog($"Deleted {countDeleted} backups", "Success");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Unable to delete backups at {message.Savegame.FullPath}");
            await ShowMessageDialog($"Unable to delete backups: {ex.Message}", "Error");
        }
    }

    private static void OnRequestStartProcessEvent(RequestStartProcessEvent message)
    {
        Process.Start(message.ProcessStartInfo);
    }

    private void OnSelectedSavegameChangedEvent(Savegame? selectedSavegame)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Title = _baseTitle + (selectedSavegame != null
                ? $" (Selected: {selectedSavegame.Title}, {selectedSavegame.PrintableType} - Saved at: {selectedSavegame.LastSaveTime}, Last Modified: {selectedSavegame.SavegameStore.LastWriteTime})"
                : "");
        });
    }

    private static void OnRequestApplicationExitEvent()
    {
        Application.Current.Shutdown();
    }

    private void OnRequestSelectSavegameEvent()
    {
        var window = new SelectSavegameWindow
        {
            Owner = this
        };
        window.ShowDialog();
    }

    private async void OnRequestSaveChangesEvent(RequestSaveChangesEvent message)
    {
        if (message.SelectedSavegame.SavegameStore.HasChanged())
        {
            var overwriteResult =
                await ShowConfirmDialog(
                    "The savegame has been modified outside. Do you really want to overwrite any changes?",
                    "Overwrite Changes");
            if (overwriteResult != MessageDialogResult.Affirmative)
            {
                return;
            }
        }

        var applicationSettings = Ioc.Default.GetRequiredService<ApplicationSettings>();
        var effectiveBackupMode = applicationSettings.CurrentBackupMode;

        if (applicationSettings.HasBackupFlag(ApplicationSettings.BackupFlag.ASK_FOR_BACKUP) &&
            effectiveBackupMode != ApplicationSettings.BackupMode.None)
        {
            var dialogSettings = BuildDefaultDialogSettings();
            dialogSettings.AffirmativeButtonText = "Yes";
            dialogSettings.NegativeButtonText = "No";
            dialogSettings.FirstAuxiliaryButtonText = "Cancel";
            dialogSettings.DialogResultOnCancel = MessageDialogResult.Canceled;

            var askResult = await ShowConfirmDialog("Do you want to create a backup?", "Create a backup?",
                MessageDialogStyle.AffirmativeAndNegativeAndSingleAuxiliary, dialogSettings);

            switch (askResult)
            {
                case MessageDialogResult.Negative:
                    effectiveBackupMode = ApplicationSettings.BackupMode.None;
                    break;
                case MessageDialogResult.Canceled:
                case MessageDialogResult.FirstAuxiliary:
                    return;
            }
        }

        message.InvokeCallback(effectiveBackupMode);
    }

    private void OnRequestRegrowTreesEvent()
    {
        var window = new RegrowTreesWindow(this);
        window.ShowDialog();
    }

    private static void OnRequestReviveFollowersEvent(RequestReviveFollowersEvent message)
    {
        var hasChanges =
            message.SelectedSavegame.ReviveFollower(message.TypeId, message.ItemIds, message.Outfit, message.Pos);

        var actorName = message.TypeId == Constants.Actors.KelvinTypeId ? "Kelvin" : "Virginia";

        if (hasChanges)
        {
            WeakReferenceMessenger.Default.Send(new GenericMessageEvent($"{actorName} should now be back again",
                "Revived"));
        }
        else
        {
            WeakReferenceMessenger.Default.Send(new GenericMessageEvent($"{actorName} should be alive already",
                "No changes"));
        }
    }

    private async void OnSavegameStored(SavegameStoredEvent message)
    {
        if (message.Message is { } text)
        {
            await ShowMessageDialog(text, "Save Changes");
        }
    }

    private Task<MessageDialogResult> ShowMessageDialog(string message, string title)
    {
        return this.ShowMessageAsync(title, message, MessageDialogStyle.Affirmative, new MetroDialogSettings
        {
            AnimateShow = false,
            AnimateHide = false,
            ColorScheme = MetroDialogColorScheme.Theme
        });
    }

    private Task<MessageDialogResult> ShowConfirmDialog(string message, string title)
    {
        return ShowConfirmDialog(message, title, MessageDialogStyle.AffirmativeAndNegative,
            BuildDefaultDialogSettings());
    }

    private static MetroDialogSettings BuildDefaultDialogSettings()
    {
        return new MetroDialogSettings
        {
            AnimateShow = false,
            AnimateHide = false,
            ColorScheme = MetroDialogColorScheme.Theme
        };
    }

    private Task<MessageDialogResult> ShowConfirmDialog(string message, string title, MessageDialogStyle dialogStyle,
        MetroDialogSettings dialogSettings)
    {
        return this.ShowMessageAsync(title, message, dialogStyle, dialogSettings);
    }

    private void OpenReadme_Click(object sender, RoutedEventArgs e)
    {
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "README.md");
        var markdownViewer = new MarkdownViewer(this, path, "Readme");
        markdownViewer.ShowDialog();
    }

    private void OpenChangelog_Click(object sender, RoutedEventArgs e)
    {
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CHANGELOG.md");
        var markdownViewer = new MarkdownViewer(this, path, "Changelog");
        markdownViewer.ShowDialog();
    }

    private async void MainWindow_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Q && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;

            if (await ShowConfirmDialog("Do you want to close the application?", "Close Application") ==
                MessageDialogResult.Affirmative)
            {
                Close();
            }
        }

        if (e.Key == Key.Escape)
        {
            if (MapFlyout.IsOpen)
            {
                MapFlyout.IsOpen = false;
                e.Handled = true;
            }
        }

        _dataContext.OnPreviewKeyDown(e);
    }
}