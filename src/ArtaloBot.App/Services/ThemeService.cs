using MaterialDesignThemes.Wpf;

namespace ArtaloBot.App.Services;

public interface IThemeService
{
    bool IsDarkTheme { get; }
    void ToggleTheme();
    void SetTheme(bool isDark);
}

public class ThemeService : IThemeService
{
    private readonly PaletteHelper _paletteHelper = new();

    public bool IsDarkTheme
    {
        get
        {
            var theme = _paletteHelper.GetTheme();
            return theme.GetBaseTheme() == BaseTheme.Dark;
        }
    }

    public void ToggleTheme()
    {
        SetTheme(!IsDarkTheme);
    }

    public void SetTheme(bool isDark)
    {
        var theme = _paletteHelper.GetTheme();
        theme.SetBaseTheme(isDark ? BaseTheme.Dark : BaseTheme.Light);
        _paletteHelper.SetTheme(theme);
    }
}
