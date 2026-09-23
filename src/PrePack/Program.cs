namespace PrePack;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var i = Array.IndexOf(args, "--print-test-pdf");
        Application.Run(new MainForm(i >= 0 && i + 1 < args.Length ? args[i + 1] : null));
    }
}
