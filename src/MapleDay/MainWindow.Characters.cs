using System.Collections.ObjectModel;
using MapleDay.Services;
using MapleDay.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MapleDay;

public sealed partial class MainWindow
{
    public ObservableCollection<CharacterCard> FilteredCharacters { get; } = [];
    public ObservableCollection<CharacterWorldOption> CharacterWorlds { get; } = [];
    private bool _updatingCharacterWorlds;
    private Dictionary<string, CachedCharacter> _cachedCharacters = [];
    private int _characterAccountCount;

    private async Task RestoreCachedCharactersAsync(string key)
    {
        try
        {
            var cache = await _characterCache.LoadAsync(key);
            if (cache is null || _closed || _apiKey != key) return;
            _cachedCharacters = cache.Characters.ToDictionary(character => character.Summary.Ocid);
            _characterAccountCount = cache.AccountCount;
            Characters.Clear();
            foreach (var saved in cache.Characters)
            {
                var character = new CharacterCard(saved.Summary);
                if (saved.Basic is not null) character.Apply(saved.Basic, saved.Image, saved.ImageFailed, saved.UpdatedAt);
                else character.MarkFailed("저장된 목록 정보 · 기본 정보 새로고침 대기");
                Characters.Add(character);
            }
            _hasLoadedCharacters = true;
            CharacterCount.Text = $"계정 {cache.AccountCount}개 · 캐릭터 {Characters.Count}개";
            UpdateCharacterWorlds();
            UpdateSchedulerCharacters();
            UpdateRepresentativeCharacter();
            await SaveKeyProfilesAsync();
            UpdateSupportLogin();
            ConnectState.Visibility = Visibility.Collapsed;
        }
        catch (Exception error) when (IsStorageError(error) || error is System.Text.Json.JsonException)
        {
            // The startup refresh replaces an unreadable cache.
        }
    }

    private void UpdateCharacterWorlds()
    {
        var selectedWorld = _hasLoadedCharacters ? (CharacterWorldFilter.SelectedItem as CharacterWorldOption)?.World : null;
        _updatingCharacterWorlds = true;
        try
        {
            CharacterWorlds.Clear();
            CharacterWorlds.Add(new(null));
            foreach (var world in Characters.Select(character => character.World).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
                CharacterWorlds.Add(new(world));
            CharacterWorldFilter.SelectedItem = CharacterWorlds.FirstOrDefault(option => option.World == selectedWorld) ?? CharacterWorlds[0];
        }
        finally { _updatingCharacterWorlds = false; }
        ApplyCharacterWorldFilter();
    }

    private void CharacterWorldFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updatingCharacterWorlds && CharacterWorldFilter is not null) ApplyCharacterWorldFilter();
    }

    private void ApplyCharacterWorldFilter()
    {
        var world = (CharacterWorldFilter.SelectedItem as CharacterWorldOption)?.World;
        FilteredCharacters.Clear();
        foreach (var character in Characters.Where(character => world is null || character.World == world))
            FilteredCharacters.Add(character);
        CharacterFilterControls.Visibility = _hasLoadedCharacters ? Visibility.Visible : Visibility.Collapsed;
        CharacterFilterCount.Text = $"캐릭터 {FilteredCharacters.Count}개";
        EmptyState.Visibility = _hasLoadedCharacters && FilteredCharacters.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CharacterAddSchedulerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string ocid })
        {
            var character = Characters.FirstOrDefault(character => character.Ocid == ocid);
            if (character is not null) AddSchedulerCharacter(character);
        }
    }

    private void SchedulerOpenCharacterList_Click(object sender, RoutedEventArgs e) => NavigateTo("characters");
}
