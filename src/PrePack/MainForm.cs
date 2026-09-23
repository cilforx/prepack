using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using PrePack.Bridge;

namespace PrePack;

/// <summary>WinForms shell: one WebView2 serving the embedded UI at https://prepack.app/.</summary>
public sealed class MainForm : Form
{
    private readonly WebView2 _webView = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.White };
    private WebBridge? _bridge;
    private readonly string? _pdfTestPath;

    public MainForm(string? pdfTestPath = null)
    {
        _pdfTestPath = pdfTestPath;
        Text = "PrePack — แบ่งบรรจุยา";
        Size = new Size(1280, 820);
        MinimumSize = new Size(960, 640);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        using (var ico = WebAssets.Open("app.ico"))
        {
            if (ico != null) Icon = new Icon(ico);
        }
        Controls.Add(_webView);
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        try
        {
            await InitWebViewAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "เปิดหน้าจอไม่ได้ — ตรวจว่าเครื่องมี Microsoft Edge WebView2 Runtime\n\n" + ex.Message,
                "PrePack", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
    }

    private async Task InitWebViewAsync()
    {
        // single-file exe: โฟลเดอร์ข้าง exe อาจเขียนไม่ได้ จึงระบุ user data folder เอง
        var userData = Path.Combine(AppPaths.DataDir, "WebView2");
        var env = await CoreWebView2Environment.CreateAsync(null, userData,
            new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = "--lang=th-TH" });
        await _webView.EnsureCoreWebView2Async(env);

        var wv = _webView.CoreWebView2;
        WebAssets.Attach(wv);

        _bridge = new WebBridge(this, wv);
        wv.AddHostObjectToScript("bridge", _bridge);

        // Screen zoom remembered per machine (⚙ → การแสดงผล, or Ctrl + mouse wheel). Printing is not affected.
        _webView.ZoomFactor = _bridge.UiZoomPercent / 100.0;
        _bridge.ApplyZoom = f => _webView.ZoomFactor = f;
        _webView.ZoomFactorChanged += (_, _) => _bridge.RememberZoom(_webView.ZoomFactor);

        wv.Settings.IsStatusBarEnabled = false;
        wv.Settings.AreDefaultContextMenusEnabled = false;
#if DEBUG
        wv.Settings.AreDevToolsEnabled = true;
#else
        wv.Settings.AreDevToolsEnabled = false;
#endif
        if (_pdfTestPath != null)
            wv.NavigationCompleted += async (_, _) => await PrintTestPdfAsync(wv, _pdfTestPath);
        wv.Navigate(WebAssets.EntryUrl);
    }

    /// <summary>
    /// "PrePack.exe --print-test-pdf out.pdf": clicks "ทดสอบตำแหน่ง" through the real path
    /// (page → bridge.Print → print settings), but the bridge writes a PDF instead of using the printer.
    /// The message shown on the page is written to out.pdf.txt. Exit code 0 = success.
    /// </summary>
    private async Task PrintTestPdfAsync(CoreWebView2 wv, string path)
    {
        var exitCode = 1;
        var full = Path.GetFullPath(path);
        try
        {
            _bridge!.PdfSinkPath = full;
            await Task.Delay(1500); // let init() finish
            // Init reads the local SQLite database; if that (or anything else in start-up) failed, stop here.
            if (await wv.ExecuteScriptAsync("app.config !== null") != "true")
            {
                var err = await wv.ExecuteScriptAsync("document.getElementById('msg').textContent");
                await File.WriteAllTextAsync(full + ".txt", "INIT FAILED: " + err);
                exitCode = 3;
                return;
            }
            await wv.ExecuteScriptAsync("doPrint(true)");
            var msg = "";
            for (var i = 0; i < 100; i++) // up to 20 s
            {
                await Task.Delay(200);
                msg = System.Text.Json.JsonSerializer.Deserialize<string>(
                    await wv.ExecuteScriptAsync("document.getElementById('msg').textContent")) ?? "";
                if (msg.Length > 0 && !msg.StartsWith("กำลังพิมพ์", StringComparison.Ordinal)) break;
            }
            var staffCount = await wv.ExecuteScriptAsync("app.staff.length");
            var fonts = await wv.ExecuteScriptAsync(
                "[...document.fonts].filter(f => f.status === 'loaded').map(f => f.family).join(',')");
            await File.WriteAllTextAsync(full + ".txt", msg + "\nstaff=" + staffCount + "\nfonts=" + fonts);
            exitCode = File.Exists(full) && msg.StartsWith("พิมพ์ทดสอบแล้ว", StringComparison.Ordinal) ? 0 : 2;
        }
        finally
        {
            Environment.Exit(exitCode);
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.R))
        {
            _webView.CoreWebView2?.Navigate(WebAssets.EntryUrl);
            return true;
        }
#if DEBUG
        if (keyData == Keys.F12)
        {
            _webView.CoreWebView2?.OpenDevToolsWindow();
            return true;
        }
#endif
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
