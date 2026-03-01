using AI_Panel_v2.Contracts.Services;
using AI_Panel_v2.ViewModels;

using Microsoft.UI.Xaml;

namespace AI_Panel_v2.Activation;

public class DefaultActivationHandler : ActivationHandler<LaunchActivatedEventArgs>
{
    private readonly INavigationService _navigationService;

    public DefaultActivationHandler(INavigationService navigationService)
    {
        _navigationService = navigationService;
    }

    protected override bool CanHandleInternal(LaunchActivatedEventArgs args)
    {
        // None of the ActivationHandlers has handled the activation.
        return _navigationService.Frame?.Content == null;
    }

    protected async override Task HandleInternalAsync(LaunchActivatedEventArgs args)
    {
        _navigationService.NavigateTo(typeof(WebViewViewModel).FullName!, 0);

        await Task.CompletedTask;
    }
}
