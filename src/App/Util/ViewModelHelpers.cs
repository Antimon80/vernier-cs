using App.Resources.Strings;

namespace App.Util;

public sealed class ViewModelHelpers
{
    public static Task ShowNotImplementedAsync(string feature)
    {
        Page? page = GetCurrentPage();

        if (page is null)
        {
            return Task.CompletedTask;
        }

        return page.DisplayAlertAsync(feature, "not implemented yet", AppResources.Dialog_Ok);
    }

    public static Task ShowErrorAsync(Exception ex)
    {
        Page? page = GetCurrentPage();

        if (page is null)
        {
            return Task.CompletedTask;
        }

        return page.DisplayAlertAsync(AppResources.Dialog_ErrorTitle, ex.Message, AppResources.Dialog_Ok);
    }

    public static Page? GetCurrentPage()
    {
        Page? root = Application.Current?.Windows.FirstOrDefault()?.Page;

        if (root is null)
        {
            return null;
        }

        IReadOnlyList<Page>? modalStack = root.Navigation?.ModalStack;

        return modalStack is { Count: > 0 } ? modalStack[^1] : root;
    }
}