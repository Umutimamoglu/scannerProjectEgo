using System.Text;
using IdScanner.Core.Model;
using IdScanner.Workflow.Abstractions;

namespace IdScanner.App;

/// <summary>
/// Yazıcı durum panelinin metnini üretir.
///
/// Java karşılığı: MainUI.refreshPrinterInfo içindeki StringBuilder bloğu.
/// Formun içinden çıkarıldı çünkü metin üretmek arayüz işi değil — ayrıca
/// böylece test edilebilir.
/// </summary>
internal static class PrinterReport
{
    /// <summary>Etiket genişliği — sütunlar hizalı dursun.</summary>
    private const int LabelWidth = -17;

    internal static string Build(ICardPrinter printer, PrinterState state)
    {
        var sb = new StringBuilder();

        AppendPrinterSection(sb, printer.ReadInfo());
        AppendRibbonSection(sb, printer.ReadRibbon());
        AppendCounterSection(sb, printer.ReadCleaning());
        AppendFlagSection(sb, state);

        return sb.ToString();
    }

    private static void AppendPrinterSection(StringBuilder sb, PrinterInfo? info)
    {
        sb.AppendLine("YAZICI");

        if (info is null)
        {
            sb.AppendLine("  (okunamadı — evolis_get_info başarısız)").AppendLine();
            return;
        }

        Row(sb, "Model", info.ModelName);
        Row(sb, "Seri no", info.SerialNumber);
        Row(sb, "Firmware", info.FirmwareVersion);
        Row(sb, "Baskı kafası kiti", info.PrintHeadKitNumber);
        Row(sb, "Çift yüz (flip)", YesNo(info.HasFlip));
        Row(sb, "Temassız kodlama", YesNo(info.HasContactlessEncoder));
        Row(sb, "Manyetik kodlama", YesNo(info.HasMagneticEncoder));
        sb.AppendLine();
    }

    private static void AppendRibbonSection(StringBuilder sb, RibbonInfo? ribbon)
    {
        sb.AppendLine("RİBON");

        if (ribbon is null)
        {
            sb.AppendLine("  (okunamadı — evolis_get_ribbon başarısız)").AppendLine();
            return;
        }

        Row(sb, "Tip", ribbon.Description);
        Row(sb, "Ürün kodu", ribbon.ProductCode);
        Row(sb, "Kapasite", $"{ribbon.Capacity} kart");
        Row(sb, "Kalan", ribbon.Remaining.ToString());
        sb.AppendLine();
    }

    private static void AppendCounterSection(StringBuilder sb, CleaningInfo? cleaning)
    {
        sb.AppendLine("SAYAÇLAR");

        if (cleaning is null)
        {
            sb.AppendLine("  (okunamadı — evolis_get_cleaning başarısız)").AppendLine();
            return;
        }

        Row(sb, "Toplam basılan", $"{cleaning.TotalCardCount} kart");
        Row(sb, "Son temizlikten", $"{cleaning.CardCount} kart");
        Row(sb, "Temizliğe kalan", $"{cleaning.CardCountBeforeWarning} kart");
        Row(sb, "Normal temizlik", $"{cleaning.RegularCleaningCount} kez");
        Row(sb, "Kafa garantide", cleaning.PrintHeadUnderWarranty ? "evet" : "hayır");
        sb.AppendLine();
    }

    private static void AppendFlagSection(StringBuilder sb, PrinterState state)
    {
        sb.AppendLine("DURUM BAYRAKLARI");

        if (state.Flags.Count == 0)
        {
            sb.AppendLine("  (yok)");
            return;
        }

        foreach (var flag in state.Flags) sb.Append("  ").AppendLine(flag);
    }

    private static void Row(StringBuilder sb, string label, string value)
        => sb.Append("  ").Append(string.Format($"{{0,{LabelWidth}}}", label)).Append(": ").AppendLine(value);

    private static string YesNo(bool value) => value ? "var" : "yok";
}
