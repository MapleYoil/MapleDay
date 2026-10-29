using System.Collections.ObjectModel;
using System.Text.Json;
using MapleDay.Core;
using MapleDay.Models;
using MapleDay.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MapleDay;

public sealed partial class MainWindow
{
    public ObservableCollection<ApiKeyProfile> SavedApiKeys { get; } = [];
    private readonly ApiKeyProfileStore _profileStore = new();
    private readonly SemaphoreSlim _profileSaveGate = new(1, 1);
    private bool _updatingProfiles;
    private int _profileWrites;

    private async Task LoadKeyProfilesAsync(string? activeKey)
    {
        _updatingProfiles = true;
        try
        {
            foreach (var profile in await _profileStore.LoadAsync()) SavedApiKeys.Add(profile);
        }
        catch (Exception error) when (IsStorageError(error) || error is JsonException)
        { ShowError("저장된 키 목록을 읽지 못했어요", "현재 API 키로 조회할 수 있습니다.", false); }
        finally { _updatingProfiles = false; }
        if (!string.IsNullOrWhiteSpace(activeKey) && !SavedApiKeys.Any(profile => profile.Key == activeKey))
        {
            SavedApiKeys.Add(new(activeKey));
            await SaveKeyProfilesAsync();
        }
        await RestoreSavedKeyOwnershipAsync();
        UpdateSavedKeySelector();
    }

    private async Task RestoreSavedKeyOwnershipAsync()
    {
        foreach (var profile in SavedApiKeys)
        {
            try
            {
                if (await _characterCache.LoadAsync(profile.Key) is { } cache)
                    _characterApiKeys.Remember(profile.Key, cache.Characters.Select(character => character.Summary));
            }
            catch (Exception error) when (IsStorageError(error) || error is JsonException) { }
        }
        UpdateSchedulerCharacters();
    }

    private void UpdateSavedKeySelector()
    {
        _updatingProfiles = true;
        try
        {
            SavedKeySelector.Visibility = SavedApiKeys.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            SavedKeySelector.SelectedItem = SavedApiKeys.FirstOrDefault(profile => profile.Key == _apiKey);
            UpdateMultiKeySchedulerHint();
        }
        finally { _updatingProfiles = false; }
    }

    private void UpdateMultiKeySchedulerHint()
    {
        MultiKeySchedulerHint.Visibility = SavedApiKeys.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        MultiKeySchedulerHint.Text = SavedApiKeys.Count > 1
            ? $"저장된 API 키 {SavedApiKeys.Count}개의 등록 캐릭터를 한 스케줄러에서 함께 조회합니다."
            : "다른 계정의 API 키를 추가로 연결하면 등록 캐릭터를 한 스케줄러에서 함께 볼 수 있습니다.";
    }

    private void SavedKeySelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingProfiles || _busy || SavedKeySelector.SelectedItem is not ApiKeyProfile profile) return;
        RevealKeyButton.IsChecked = false;
        ApiKeyInput.Password = profile.Key;
    }

    private void UpdateRepresentativeCharacter()
    {
        _updatingProfiles = true;
        try
        {
            var existing = SavedApiKeys.FirstOrDefault(profile => profile.Key == _apiKey);
            var representative = CharacterRepresentative.Choose(Characters.Select(character => new CharacterSummary
                { Ocid = character.Ocid, Name = character.Name, World = character.World, Level = character.Level }), existing?.RepresentativeOcid);
            var character = Characters.FirstOrDefault(character => character.Ocid == representative);
            RepresentativeCharacter.SelectedItem = character;
            RepresentativeCharacter.IsEnabled = _hasLoadedCharacters && character is not null;
            if (character is not null) SupportCharacter.SelectedItem = character;
            if (!string.IsNullOrEmpty(_apiKey) && _hasLoadedCharacters)
            {
                var updated = new ApiKeyProfile(_apiKey, character?.Ocid, character?.Name, character?.World);
                if (existing != updated)
                {
                    if (existing is null) SavedApiKeys.Add(updated);
                    else SavedApiKeys[SavedApiKeys.IndexOf(existing)] = updated;
                }
            }
            SavedKeySelector.Visibility = SavedApiKeys.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            SavedKeySelector.SelectedItem = SavedApiKeys.FirstOrDefault(profile => profile.Key == _apiKey);
        }
        finally { _updatingProfiles = false; }
        UpdateMultiKeySchedulerHint();
    }

    private async void RepresentativeCharacter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingProfiles || _dataDeleting || !_hasLoadedCharacters || RepresentativeCharacter.SelectedItem is not CharacterCard character) return;
        var profile = SavedApiKeys.FirstOrDefault(profile => profile.Key == _apiKey);
        var updated = new ApiKeyProfile(_apiKey, character.Ocid, character.Name, character.World);
        _updatingProfiles = true;
        try
        {
            if (profile is null) SavedApiKeys.Add(updated);
            else SavedApiKeys[SavedApiKeys.IndexOf(profile)] = updated;
            SupportCharacter.SelectedItem = character;
        }
        finally { _updatingProfiles = false; }
        UpdateSavedKeySelector();
        await SaveKeyProfilesAsync();
    }

    private async Task SaveKeyProfilesAsync()
    {
        if (_closed || _dataDeleting) return;
        _profileWrites++;
        try
        {
            await _profileSaveGate.WaitAsync(_supportLifetime.Token);
            try { await _profileStore.SaveAsync(SavedApiKeys.ToArray(), _supportLifetime.Token); }
            finally { _profileSaveGate.Release(); }
        }
        catch (OperationCanceledException) when (_supportLifetime.IsCancellationRequested) { }
        catch (Exception error) when (IsStorageError(error))
        {
            if (!_closed && !_dataDeleting) ShowError("키별 대표 캐릭터 설정을 저장하지 못했어요", "이번 실행에는 적용됩니다. 저장 공간을 확인해주세요.", false, characterListOnly: true);
        }
        finally { _profileWrites--; }
    }
}
