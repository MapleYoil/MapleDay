using MapleDay.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;

namespace MapleDay;

public sealed partial class MainWindow
{
    private void ApplyTheme()
    {
        var dark = _settings.DarkMode;
        AppTheme.Apply(dark);
        Root.RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light;
        ThemeNavigationItem.Content = dark ? "다크 모드 · 켬" : "다크 모드 · 끔";
        var action = dark ? "라이트 모드로 전환" : "다크 모드로 전환";
        AutomationProperties.SetName(ThemeNavigationItem, action);
        ToolTipService.SetToolTip(ThemeNavigationItem, action);
        if (!Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported()) return;
        var title = AppWindow.TitleBar;
        title.ButtonForegroundColor = AppTheme.Brush("InkBrush").Color;
        title.ButtonInactiveForegroundColor = AppTheme.Brush("MutedBrush").Color;
        title.ButtonHoverForegroundColor = AppTheme.Brush("InkBrush").Color;
        title.ButtonPressedForegroundColor = AppTheme.Brush("InkBrush").Color;
        title.ButtonHoverBackgroundColor = AppTheme.Brush("SchedulerSelectedBrush").Color;
        title.ButtonPressedBackgroundColor = AppTheme.Brush("LineBrush").Color;
    }

    private void ToggleDarkMode()
    {
        if (_dataDeleting || _closed) return;
        _settings.DarkMode = !_settings.DarkMode;
        ApplyTheme();
        try { _settings.Save(); }
        catch (Exception error) when (IsStorageError(error))
        {
            _settings.DarkMode = !_settings.DarkMode;
            ApplyTheme();
            ShowReminderMessage("화면 모드를 저장하지 못했어요. 데이터 폴더 권한을 확인해주세요.", InfoBarSeverity.Warning);
        }
    }
}
