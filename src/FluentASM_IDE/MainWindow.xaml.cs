using FluentASM_IDE.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace FluentASM_IDE;

public sealed partial class MainWindow : Window
{
    public string WindowTitle => MainPageViewModel.WindowTitle;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        if (Content is FrameworkElement fe)
        {
            fe.ActualThemeChanged += (s, e) => UpdateTitleBarTheme(s.ActualTheme);
            UpdateTitleBarTheme(fe.ActualTheme);
        }

        try
        {
            AppWindow.SetIcon("Assets/AppIcon.ico");
        }
        catch { }

        AppWindow.Title = MainPageViewModel.WindowTitle;
        AppWindow.Resize(new SizeInt32(1100, 750));

        RootFrame.Navigate(typeof(MainPage));
    }

    private void UpdateTitleBarTheme(ElementTheme theme)
    {
        if (!AppWindowTitleBar.IsCustomizationSupported()) return;

        var titleBar = AppWindow.TitleBar;
        titleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
        titleBar.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);

        if (theme == ElementTheme.Light)
        {
            titleBar.ButtonForegroundColor = Windows.UI.Color.FromArgb(255, 25, 28, 30);
            titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(20, 0, 0, 0);
            titleBar.ButtonHoverForegroundColor = Windows.UI.Color.FromArgb(255, 0, 0, 0);
            titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(35, 0, 0, 0);
            titleBar.ButtonPressedForegroundColor = Windows.UI.Color.FromArgb(255, 0, 0, 0);
        }
        else
        {
            titleBar.ButtonForegroundColor = Windows.UI.Color.FromArgb(255, 189, 200, 207);
            titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(40, 255, 255, 255);
            titleBar.ButtonHoverForegroundColor = Windows.UI.Color.FromArgb(255, 229, 226, 225);
            titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(60, 255, 255, 255);
            titleBar.ButtonPressedForegroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255);
        }
    }
}


