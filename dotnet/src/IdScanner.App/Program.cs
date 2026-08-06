using System.Windows.Forms;
using IdScanner.Core.Diagnostics;

namespace IdScanner.App;

/// <summary>
/// Giriş noktası.
///
/// Java karşılığı: MainUI.main.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        MainForm? form = null;

        // Cihazlar kurulurken düşen log satırları form hazır olana kadar
        // biriktirilir; hiçbir başlangıç mesajı kaybolmasın.
        var pending = new List<string>();
        var context = new DeviceContext((level, message) =>
        {
            var line = level >= LogLevel.Warn ? $"[{level}] {message}" : message;
            if (form is null) pending.Add(line);
            else form.Log(line);
        });

        // Yakalanmamış hatalar sessizce kaybolmasın — log dosyasına da düşsün
        System.Windows.Forms.Application.ThreadException += (_, e) =>
        {
            context.Logger.Error("Arayüzde yakalanmamış hata", e.Exception);
            MessageBox.Show(e.Exception.Message, "Beklenmeyen hata",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                context.Logger.Error("Yakalanmamış hata", ex);
            }
        };

        form = new MainForm(context, context.Logger);
        foreach (var line in pending) form.Log(line);

        System.Windows.Forms.Application.Run(form);
    }
}
