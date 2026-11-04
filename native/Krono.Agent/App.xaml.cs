using Microsoft.UI.Xaml;

namespace Krono_Agent;
public partial class App : Application
{
    Window? window;
    public App() => InitializeComponent();
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        window = new MainWindow();
        window.Activate();
    }
}
