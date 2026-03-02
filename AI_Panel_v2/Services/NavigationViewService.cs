using System.Diagnostics.CodeAnalysis;

using AI_Panel_v2.Contracts.Services;
using AI_Panel_v2.Helpers;
using AI_Panel_v2.ViewModels;

using Microsoft.UI.Xaml.Controls;

namespace AI_Panel_v2.Services;

public class NavigationViewService : INavigationViewService
{
    private readonly INavigationService _navigationService;

    private readonly IPageService _pageService;

    private NavigationView? _navigationView;

    public IList<object>? MenuItems => _navigationView?.MenuItems;

    public object? SettingsItem => _navigationView?.SettingsItem;

    public NavigationViewService(INavigationService navigationService, IPageService pageService)
    {
        _navigationService = navigationService;
        _pageService = pageService;
    }

    [MemberNotNull(nameof(_navigationView))]
    public void Initialize(NavigationView navigationView)
    {
        _navigationView = navigationView;
        _navigationView.BackRequested += OnBackRequested;
        _navigationView.ItemInvoked += OnItemInvoked;
    }

    public void UnregisterEvents()
    {
        if (_navigationView != null)
        {
            _navigationView.BackRequested -= OnBackRequested;
            _navigationView.ItemInvoked -= OnItemInvoked;
        }
    }

    public NavigationViewItem? GetSelectedItem(Type pageType, object? navigationParameter = null)
    {
        if (_navigationView != null)
        {
            return GetSelectedItem(_navigationView.MenuItems, pageType, navigationParameter) ??
                   GetSelectedItem(_navigationView.FooterMenuItems, pageType, navigationParameter);
        }

        return null;
    }

    private void OnBackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args) => _navigationService.GoBack();

    private void OnItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.IsSettingsInvoked)
        {
            _navigationService.NavigateTo(typeof(SettingsViewModel).FullName!);
        }
        else
        {
            var selectedItem = args.InvokedItemContainer as NavigationViewItem;

            if (selectedItem?.GetValue(NavigationHelper.NavigateToProperty) is string pageKey)
            {
                var parameter = selectedItem.GetValue(NavigationHelper.NavigationParameterProperty);
                _navigationService.NavigateTo(pageKey, parameter);
            }
        }
    }

    private NavigationViewItem? GetSelectedItem(IEnumerable<object> menuItems, Type pageType, object? navigationParameter)
    {
        foreach (var item in menuItems.OfType<NavigationViewItem>())
        {
            if (IsMenuItemForPageType(item, pageType, navigationParameter))
            {
                return item;
            }

            var selectedChild = GetSelectedItem(item.MenuItems, pageType, navigationParameter);
            if (selectedChild != null)
            {
                return selectedChild;
            }
        }

        return null;
    }

    private bool IsMenuItemForPageType(NavigationViewItem menuItem, Type sourcePageType, object? navigationParameter)
    {
        if (menuItem.GetValue(NavigationHelper.NavigateToProperty) is string pageKey)
        {
            if (_pageService.GetPageType(pageKey) != sourcePageType)
            {
                return false;
            }

            if (navigationParameter == null)
            {
                return true;
            }

            var itemParameter = menuItem.GetValue(NavigationHelper.NavigationParameterProperty);
            return Equals(itemParameter, navigationParameter);
        }

        return false;
    }
}
