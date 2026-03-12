using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ArtaloBot.App.Services;

public interface INavigationService
{
    void NavigateTo<T>() where T : ObservableObject;
    void NavigateTo(Type viewModelType);
    event EventHandler<Type>? NavigationChanged;
}

public class NavigationService : INavigationService
{
    public event EventHandler<Type>? NavigationChanged;

    public void NavigateTo<T>() where T : ObservableObject
    {
        NavigateTo(typeof(T));
    }

    public void NavigateTo(Type viewModelType)
    {
        NavigationChanged?.Invoke(this, viewModelType);
    }
}
