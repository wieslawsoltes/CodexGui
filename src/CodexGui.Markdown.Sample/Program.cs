using Uno.UI.Hosting;

namespace CodexGui.Markdown.Sample;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Uno.App.InitializeLogging();

        UnoPlatformHostBuilder.Create()
            .App(() => new Uno.App())
            .UseX11()
            .UseLinuxFrameBuffer()
            .UseMacOS()
            .UseWin32()
            .Build()
            .Run();
    }
}
