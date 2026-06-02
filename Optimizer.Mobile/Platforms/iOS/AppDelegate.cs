using Microsoft.Maui;
using Microsoft.Maui.Hosting;
using UIKit;

namespace Optimizer.Mobile.Platforms.iOS;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
