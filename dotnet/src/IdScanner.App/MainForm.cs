using System.Drawing;
using System.Windows.Forms;
using IdScanner.Core.Diagnostics;
using IdScanner.Core.Model;
using IdScanner.Render;
using IdScanner.Workflow;
using IdScanner.Workflow.Abstractions;

namespace IdScanner.App;

/// <summary>
/// Ana pencere — kimlik okuma ve kart baskı arayüzü.
///
/// Java karşılığı: MainUI.java.
///
/// <b>Java'dan fark:</b> Orada form hem arayüzü çiziyor, hem cihazları
/// bağlıyor, hem baskı akışını yönetiyordu (837 satır). Burada iş mantığı
/// <see cref="CardIssuanceService"/>'e taşındı; form yalnızca girdi topluyor,
/// servisi çağırıyor ve sonucu gösteriyor.
/// </summary>
public sealed class MainForm : Form
{
    // Java'daki renk paleti
    private static readonly Color Background = ColorTranslator.FromHtml("#F5F6F8");
    private static readonly Color OkGreen = ColorTranslator.FromHtml("#1B7F3B");
    private static readonly Color WarnOrange = ColorTranslator.FromHtml("#B8730A");
    private static readonly Color ErrorRed = ColorTranslator.FromHtml("#B3261E");
    private static readonly Color Neutral = ColorTranslator.FromHtml("#5A5F66");

    /// <summary>Kimlik alanlarının sırası ve etiketleri — Java ile aynı.</summary>
    private static readonly string[] FieldLabels =
    [
        "Ad", "Soyad", "T.C. No", "Belge No", "Doğum Tarihi",
        "Doğum Yeri", "Cinsiyet", "Uyruk", "Veriliş", "Geçerlilik", "Veren Kurum",
    ];

    /// <summary>Kart yoklama aralığı.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(800);

    private readonly DeviceContext _devices;
    private readonly IAppLogger _log;

    // Okuyucu tarafı
    private Label _readerStatus = null!;
    private PictureBox _photoBox = null!;
    private readonly Dictionary<string, TextBox> _fields = [];
    private TextBox _mrzBox = null!;
    private Button _scanButton = null!;
    private Button _ejectButton = null!;

    // Yazıcı tarafı
    private Label _printerStatus = null!;
    private TextBox _printerInfoBox = null!;
    private Button _refreshButton = null!;
    private Button _clearErrorButton = null!;
    private Button _previewButton = null!;
    private Button _bothPreviewButton = null!;
    private Button _dryRunButton = null!;
    private Button _calibrationButton = null!;
    private Button _printButton = null!;
    private Button _overlayPreviewButton = null!;
    private Button _overlayPrintButton = null!;
    private CheckBox _overlayRotateCheck = null!;

    private Button _reconnectButton = null!;
    private TextBox _logBox = null!;

    private IdData? _lastData;
    private string? _lastPhotoPath;
    private CancellationTokenSource? _pollingCancellation;
    private CardStatus _lastCardStatus = CardStatus.Unknown;

    public MainForm(DeviceContext devices, IAppLogger log)
    {
        _devices = devices;
        _log = log;

        Text = "Kimlik Okuma ve Kart Baskı Sistemi";
        BackColor = Background;
        Size = new Size(1220, 860);
        MinimumSize = new Size(1000, 700);
        StartPosition = FormStartPosition.CenterScreen;

        BuildLayout();
        FormClosing += OnFormClosing;
    }

    // === Yerleşim ===

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Background,
            Padding = new Padding(0),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 170));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildSplit(), 0, 1);
        root.Controls.Add(BuildLogPanel(), 0, 2);

        Controls.Add(root);
    }

    private Control BuildHeader()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Height = 56,
            BackColor = Background,
            Padding = new Padding(16, 14, 16, 4),
        };

        var title = new Label
        {
            Text = "KİMLİK OKUMA VE KART BASKI SİSTEMİ",
            Font = new Font(Font.FontFamily, 14f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(16, 16),
        };

        _reconnectButton = new Button
        {
            Text = "Cihazları Yeniden Bağla",
            AutoSize = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        _reconnectButton.Click += (_, _) => ConnectDevicesAsync().Forget();

        panel.Controls.Add(title);
        panel.Controls.Add(_reconnectButton);
        panel.Resize += (_, _) =>
            _reconnectButton.Location = new Point(panel.Width - _reconnectButton.Width - 16, 14);

        return panel;
    }

    private Control BuildSplit()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            BackColor = Background,
        };

        split.Panel1.Controls.Add(BuildReaderPanel());
        split.Panel2.Controls.Add(BuildPrinterPanel());

        // Java'daki resizeWeight 0.62 karşılığı — pencere ilk açıldığında uygula
        split.HandleCreated += (_, _) =>
        {
            if (split.Width > 0) split.SplitterDistance = (int)(split.Width * 0.62);
        };

        return split;
    }

    private Control BuildReaderPanel()
    {
        var group = new GroupBox
        {
            Text = "Kart Okuyucu (CRT-603-7005)",
            Dock = DockStyle.Fill,
            BackColor = Background,
            Padding = new Padding(10),
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Background,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));

        _readerStatus = CreateStatusLabel("Başlatılıyor...");
        layout.Controls.Add(_readerStatus, 0, 0);
        layout.Controls.Add(BuildIdentitySection(), 0, 1);
        layout.Controls.Add(BuildMrzAndButtons(), 0, 2);

        group.Controls.Add(layout);
        return group;
    }

    private Control BuildIdentitySection()
    {
        var container = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Background,
        };
        container.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        container.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // Fotoğraf
        var photoGroup = new GroupBox
        {
            Text = "Biyometrik Fotoğraf",
            Dock = DockStyle.Fill,
            BackColor = Background,
        };
        _photoBox = new PictureBox
        {
            Dock = DockStyle.Top,
            Height = 260,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            SizeMode = PictureBoxSizeMode.Zoom,
        };
        photoGroup.Controls.Add(_photoBox);
        container.Controls.Add(photoGroup, 0, 0);

        // Kimlik alanları
        var fieldsGroup = new GroupBox
        {
            Text = "Kimlik Bilgileri",
            Dock = DockStyle.Fill,
            BackColor = Background,
        };

        var form = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoScroll = true,
            BackColor = Background,
            Padding = new Padding(6),
        };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        foreach (var label in FieldLabels)
        {
            form.Controls.Add(new Label
            {
                Text = label + ":",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(6, 6, 6, 3),
            });

            var box = new TextBox
            {
                ReadOnly = true,
                BackColor = Color.White,
                Dock = DockStyle.Fill,
                Margin = new Padding(6, 3, 6, 3),
            };
            form.Controls.Add(box);
            _fields[label] = box;
        }

        fieldsGroup.Controls.Add(form);
        container.Controls.Add(fieldsGroup, 1, 0);

        return container;
    }

    private Control BuildMrzAndButtons()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Background,
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var mrzGroup = new GroupBox
        {
            Text = "MRZ (kart arkası)",
            Dock = DockStyle.Fill,
            BackColor = Background,
        };
        _mrzBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            Font = new Font(FontFamily.GenericMonospace, 9f),
            BackColor = ColorTranslator.FromHtml("#FFFDF3"),
            ScrollBars = ScrollBars.Vertical,
        };
        mrzGroup.Controls.Add(_mrzBox);
        panel.Controls.Add(mrzGroup, 0, 0);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Background,
            Padding = new Padding(0, 6, 0, 6),
        };

        _scanButton = CreateBigButton("KARTI TARA");
        _ejectButton = CreateBigButton("KARTI ÇIKAR");
        _scanButton.Click += (_, _) => ScanAndReadAsync().Forget();
        _ejectButton.Click += (_, _) => EjectAsync().Forget();

        buttons.Controls.Add(_scanButton);
        buttons.Controls.Add(_ejectButton);
        panel.Controls.Add(buttons, 0, 1);

        return panel;
    }

    private Control BuildPrinterPanel()
    {
        var group = new GroupBox
        {
            Text = "Kart Yazıcı (Evolis KC Prime)",
            Dock = DockStyle.Fill,
            BackColor = Background,
            Padding = new Padding(10),
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Background,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _printerStatus = CreateStatusLabel("Bağlanılıyor...");
        layout.Controls.Add(_printerStatus, 0, 0);

        var infoGroup = new GroupBox
        {
            Text = "Durum ve Sayaçlar",
            Dock = DockStyle.Fill,
            BackColor = Background,
        };
        _printerInfoBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            Font = new Font(FontFamily.GenericMonospace, 9f),
            BackColor = Color.White,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
        };
        infoGroup.Controls.Add(_printerInfoBox);
        layout.Controls.Add(infoGroup, 0, 1);

        layout.Controls.Add(BuildPrinterButtons(), 0, 2);
        group.Controls.Add(layout);
        return group;
    }

    private Control BuildPrinterButtons()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            AutoSize = true,
            BackColor = Background,
            Padding = new Padding(0, 8, 0, 0),
        };

        _refreshButton = CreateButton("Bilgileri Yenile", () => RefreshPrinterInfoAsync().Forget());
        _clearErrorButton = CreateButton("Hatayı Temizle", () => ClearPrinterErrorsAsync().Forget());
        _previewButton = CreateButton("Ön Yüz Önizle", () => PreviewAsync(bothFaces: false).Forget());
        _bothPreviewButton = CreateButton("Ön + Arka Önizle", () => PreviewAsync(bothFaces: true).Forget());
        _dryRunButton = CreateButton("Prova Bas (kart harcamaz)", () => PrintAsync(dryRun: true).Forget());
        _calibrationButton = CreateButton("Kalibrasyon Kartı Bas", () => PrintCalibrationAsync().Forget());

        _printButton = CreateBigButton("KART BAS (boş karta, çift yüz)");
        _printButton.Dock = DockStyle.Fill;
        _printButton.Click += (_, _) => PrintAsync(dryRun: false).Forget();

        // --- Hazır basılı kart üzerine baskı ---
        _overlayPreviewButton = CreateButton("Hazır Kart Önizle (kılavuzlu)",
            () => PreviewOverlayAsync().Forget());
        _overlayPrintButton = CreateBigButton("HAZIR KARTA BAS (tek yüz)");
        _overlayPrintButton.Dock = DockStyle.Fill;
        _overlayPrintButton.Click += (_, _) => PrintOverlayAsync().Forget();

        _overlayRotateCheck = new CheckBox
        {
            Text = "Hazır kart ters besleniyor (180° döndür)",
            Checked = true,
            AutoSize = true,
            Margin = new Padding(2, 6, 2, 2),
        };

        foreach (Control control in new Control[]
                 {
                     _refreshButton, _clearErrorButton, _previewButton,
                     _bothPreviewButton, _dryRunButton, _calibrationButton, _printButton,
                     CreateSeparatorLabel("— Hazır basılı kart —"),
                     _overlayRotateCheck, _overlayPreviewButton, _overlayPrintButton,
                 })
        {
            panel.Controls.Add(control);
        }

        return panel;
    }

    private static Label CreateSeparatorLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Neutral,
        Margin = new Padding(2, 10, 2, 2),
    };

    private Control BuildLogPanel()
    {
        var group = new GroupBox
        {
            Text = "İşlem Günlüğü",
            Dock = DockStyle.Fill,
            BackColor = Background,
            Margin = new Padding(16, 0, 16, 12),
        };

        _logBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            Font = new Font(FontFamily.GenericMonospace, 8.5f),
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.White,
        };

        group.Controls.Add(_logBox);
        return group;
    }

    // === Başlatma ve cihaz bağlantısı ===

    /// <summary>Pencere açıldıktan sonra donanımı hazırla.</summary>
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Log($"Log dosyası: {_devices.LogFilePath}");
        if (_devices.DumpDirectory is { } dump) Log($"Tanılama dökümü: {dump}");
        ConnectDevicesAsync().Forget();
    }

    /// <summary>
    /// Okuyucu ve yazıcıya bağlan. Cihazlar sonradan takıldığında da
    /// çağrılabilir — "Cihazları Yeniden Bağla" düğmesi bunu kullanır.
    /// </summary>
    private async Task ConnectDevicesAsync()
    {
        SetReaderButtonsEnabled(false);
        SetPrinterButtonsEnabled(false);
        _reconnectButton.Enabled = false;
        Log("Cihazlar aranıyor...");

        var (readerOk, printerOk, printerName, firmware) = await Task.Run(() =>
        {
            _devices.Scanner.Dispose();
            _devices.Printer.Disconnect();

            var scannerOk = _devices.Scanner.Open();
            var evolisOk = _devices.Printer.Connect();

            var fw = scannerOk && _devices.Scanner is Native.DocumentScanner ds ? ds.GetVersionInfo(2) : "";
            return (scannerOk, evolisOk, _devices.Printer.PrinterName, fw);
        });

        if (readerOk)
        {
            Log("Okuyucu hazır" + (string.IsNullOrWhiteSpace(firmware) ? "" : $" (firmware {firmware})"));
            StartCardPolling();
        }
        else
        {
            SetStatus(_readerStatus, "Okuyucu bulunamadı — USB ve sürücüyü kontrol edin", ErrorRed);
        }
        SetReaderButtonsEnabled(readerOk);

        if (printerOk)
        {
            Log($"Yazıcı bağlandı: {printerName}");
            await RefreshPrinterInfoAsync();
        }
        else
        {
            SetStatus(_printerStatus, "Yazıcı bulunamadı — açık mı, bağlı mı?", ErrorRed);
            _printerInfoBox.Text = string.Join(Environment.NewLine,
                "Yazıcıya bağlanılamadı.",
                "",
                "Kontrol listesi:",
                "  • Yazıcı açık mı?",
                "  • USB kablosu takılı mı?",
                "  • Evolis Premium Suite kurulu mu?");
        }
        SetPrinterButtonsEnabled(printerOk);
        _reconnectButton.Enabled = true;

        if (!readerOk && !printerOk)
        {
            Log("Hiçbir cihaz bulunamadı. USB bağlantılarını kontrol edip " +
                "'Cihazları Yeniden Bağla'ya basın.");
        }
    }

    /// <summary>Kart varlığını arka planda yokla, değişince arayüzü güncelle.</summary>
    private void StartCardPolling()
    {
        if (_pollingCancellation is not null) return; // ikinci döngü açma

        _pollingCancellation = new CancellationTokenSource();
        var token = _pollingCancellation.Token;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                var status = _devices.Scanner.GetCardStatus();
                if (status != _lastCardStatus)
                {
                    _lastCardStatus = status;
                    RunOnUiThread(() => UpdateCardStatusLabel(status));
                }

                try
                {
                    await Task.Delay(PollInterval, token);
                }
                catch (TaskCanceledException)
                {
                    return;
                }
            }
        }, token);
    }

    private void UpdateCardStatusLabel(CardStatus status)
    {
        switch (status)
        {
            case CardStatus.None:
                SetStatus(_readerStatus, "Cihazda kart yok", Neutral);
                break;
            case CardStatus.Moving:
                SetStatus(_readerStatus, "Kart hareket ediyor...", WarnOrange);
                break;
            case CardStatus.Front or CardStatus.Inside or CardStatus.Back:
                SetStatus(_readerStatus, "Kart cihazda — TARA'ya basabilirsiniz", OkGreen);
                break;
            default:
                SetStatus(_readerStatus, "Kart durumu okunamıyor", ErrorRed);
                break;
        }
    }

    // === Okuyucu işlemleri ===

    private async Task ScanAndReadAsync()
    {
        SetReaderButtonsEnabled(false);
        ClearFields();
        SetStatus(_readerStatus, "Taranıyor...", WarnOrange);

        var result = await Task.Run(() => _devices.Service.ScanAndRead());

        if (result is { Ok: true, Value: { } data })
        {
            _lastData = data;
            ShowData(data);
            SetStatus(_readerStatus, "Kart okundu. Yeni kart için TARA'ya basın.", OkGreen);
            Log($"Kimlik okundu: {data.Name} {data.Surname}");
        }
        else
        {
            SetStatus(_readerStatus, "Okuma başarısız: " + result.Message, ErrorRed);
            Log("HATA: " + result.Message);
        }

        SetReaderButtonsEnabled(true);
    }

    private async Task EjectAsync()
    {
        SetReaderButtonsEnabled(false);
        await Task.Run(() => _devices.Service.Eject());
        SetReaderButtonsEnabled(true);
        Log("Kart çıkarıldı.");
    }

    private void ShowData(IdData data)
    {
        _fields["Ad"].Text = data.Name;
        _fields["Soyad"].Text = data.Surname;
        _fields["T.C. No"].Text = data.TcNo;
        _fields["Belge No"].Text = data.DocumentNumber;
        _fields["Doğum Tarihi"].Text = data.BirthDate;
        _fields["Doğum Yeri"].Text = data.BirthPlace;
        _fields["Cinsiyet"].Text = data.Gender;
        _fields["Uyruk"].Text = data.Nationality;
        _fields["Veriliş"].Text = data.IssueDate;
        _fields["Geçerlilik"].Text = data.ExpiryDate;
        _fields["Veren Kurum"].Text = data.IssuingAuthority;

        _mrzBox.Text = string.Join(Environment.NewLine, data.MrzLine1, data.MrzLine2, data.MrzLine3);

        ShowPhoto(data);
        ShowVerification(data);
    }

    /// <summary>Fotoğrafı çöz, ekranda göster ve baskı için diske yaz.</summary>
    private void ShowPhoto(IdData data)
    {
        if (data.Photo is null)
        {
            Log("Çipte fotoğraf bulunamadı.");
            return;
        }

        var bitmap = FaceImageDecoder.Decode(data.Photo, _log);
        if (bitmap is null)
        {
            Log("Fotoğraf çözülemedi — ayrıntı log dosyasında.");
            return;
        }

        _photoBox.Image?.Dispose();
        _photoBox.Image = bitmap;

        try
        {
            var path = Path.Combine(Core.AppPaths.EnsureDir("output"), "photo.png");
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            _lastPhotoPath = path;
        }
        catch (Exception e)
        {
            _log.Warn("Fotoğraf diske yazılamadı — kartta boş çıkacak", e);
            _lastPhotoPath = null;
        }
    }

    /// <summary>Doğrulama sonucunu log paneline yaz.</summary>
    private void ShowVerification(IdData data)
    {
        if (data.Verification is not { } verification) return;
        foreach (var line in verification.Report()) Log(line);
    }

    private void ClearFields()
    {
        foreach (var box in _fields.Values) box.Text = "";
        _mrzBox.Text = "";
        _photoBox.Image?.Dispose();
        _photoBox.Image = null;
        _lastPhotoPath = null;
    }

    // === Yazıcı işlemleri ===

    private async Task RefreshPrinterInfoAsync()
    {
        SetPrinterButtonsEnabled(false);

        var (text, state) = await Task.Run(() =>
        {
            var printerState = _devices.Printer.ReadState();
            var report = PrinterReport.Build(_devices.Printer, printerState);
            return (report, printerState);
        });

        _printerInfoBox.Text = text;

        var color = state.Kind switch
        {
            PrinterStateKind.Ready => OkGreen,
            PrinterStateKind.Warning => WarnOrange,
            PrinterStateKind.Error => ErrorRed,
            _ => Neutral,
        };
        SetStatus(_printerStatus, state.Label, color);

        SetPrinterButtonsEnabled(true);
    }

    private async Task ClearPrinterErrorsAsync()
    {
        SetPrinterButtonsEnabled(false);
        var ok = await Task.Run(() => _devices.Printer.ClearMechanicalErrors());
        Log(ok ? "Mekanik hata temizlendi." : "Mekanik hata temizlenemedi.");
        await RefreshPrinterInfoAsync();
    }

    private async Task PreviewAsync(bool bothFaces)
    {
        if (!EnsureDataRead()) return;

        var cardData = _devices.Service.BuildCardData(_lastData!, _lastPhotoPath);
        var png = await Task.Run(() => bothFaces
            ? _devices.Service.PreviewBoth(cardData)
            : _devices.Service.PreviewFront(cardData));

        ShowPreviewDialog(png, bothFaces ? "Ön + Arka Önizleme" : "Ön Yüz Önizleme");
    }

    private async Task PrintAsync(bool dryRun)
    {
        if (!EnsureDataRead()) return;

        if (!dryRun)
        {
            var confirm = MessageBox.Show(
                $"{_lastData!.Name} {_lastData.Surname} için kart basılacak.\n\nDevam edilsin mi?",
                "Kart Baskı Onayı", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes) return;
        }

        SetPrinterButtonsEnabled(false);
        Log(dryRun ? "Prova baskı başlıyor (kart harcanmaz)..." : "Kart baskısı başlıyor...");

        var cardData = _devices.Service.BuildCardData(_lastData!, _lastPhotoPath);
        var result = await Task.Run(() => _devices.Service.Print(cardData, dryRun));

        Log(result.Ok ? result.Message : "HATA: " + result.Message);
        if (!result.Ok)
        {
            MessageBox.Show(result.Message, "Baskı başarısız", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        SetPrinterButtonsEnabled(true);
        await RefreshPrinterInfoAsync();
    }

    /// <summary>
    /// Hazır kart yerleşimini ölçü dosyasından oku.
    ///
    /// Ölçüler ilk baskılardan sonra değişecek; her seferinde kod derlemek
    /// gerekmesin diye <c>overlay-layout.txt</c> dosyasından okunuyor.
    /// Dosya yoksa varsayılan değerler kullanılır.
    /// </summary>
    private OverlayLayout CurrentOverlayLayout(bool showGuides) =>
        OverlayLayoutFile.Load(_devices.Logger) with
        {
            Rotate180 = _overlayRotateCheck.Checked,
            ShowGuides = showGuides,
        };

    private async Task PreviewOverlayAsync()
    {
        if (!EnsureDataRead()) return;

        var cardData = _devices.Service.BuildCardData(_lastData!, _lastPhotoPath);
        var layout = CurrentOverlayLayout(showGuides: true);

        var png = await Task.Run(() => _devices.Service.PreviewOverlay(cardData, layout));
        ShowPreviewDialog(png, "Hazır Kart Önizleme (kesikli çizgiler baskıya gitmez)");
    }

    private async Task PrintOverlayAsync()
    {
        if (!EnsureDataRead()) return;

        var confirm = MessageBox.Show(
            $"{_lastData!.Name} {_lastData.Surname} için HAZIR KARTA basılacak.\n\n" +
            "Kartın doğru yönde takılı olduğundan emin olun:\n" +
            "fotoğraf kutusu, normal baskıda fotoğrafın çıktığı uca gelmeli.\n\n" +
            "Devam edilsin mi?",
            "Hazır Kart Baskı Onayı", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;

        SetPrinterButtonsEnabled(false);
        Log("Hazır karta baskı başlıyor (tek yüz)...");

        var cardData = _devices.Service.BuildCardData(_lastData!, _lastPhotoPath);
        var layout = CurrentOverlayLayout(showGuides: false);

        var result = await Task.Run(() => _devices.Service.PrintOverlay(cardData, layout, dryRun: false));

        Log(result.Ok ? result.Message : "HATA: " + result.Message);
        if (!result.Ok)
        {
            MessageBox.Show(result.Message, "Baskı başarısız", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        SetPrinterButtonsEnabled(true);
        await RefreshPrinterInfoAsync();
    }

    private async Task PrintCalibrationAsync()
    {
        var confirm = MessageBox.Show(
            "Kalibrasyon kartı basılacak — bir kart harcanacak.\n\nDevam edilsin mi?",
            "Kalibrasyon", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;

        SetPrinterButtonsEnabled(false);
        Log("Kalibrasyon kartı basılıyor...");

        var result = await Task.Run(() => _devices.Service.PrintCalibration(dryRun: false));
        Log(result.Ok ? result.Message : "HATA: " + result.Message);

        SetPrinterButtonsEnabled(true);
    }

    /// <summary>Baskı ve önizleme için önce kart okunmuş olmalı.</summary>
    private bool EnsureDataRead()
    {
        if (_lastData is not null) return true;

        MessageBox.Show("Önce bir kart okuyun.", "Veri yok",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
        return false;
    }

    private void ShowPreviewDialog(byte[] png, string title)
    {
        using var stream = new MemoryStream(png);
        var image = Image.FromStream(stream);

        var dialog = new Form
        {
            Text = title,
            Size = new Size(Math.Min(image.Width + 40, 900), Math.Min(image.Height + 60, 900)),
            StartPosition = FormStartPosition.CenterParent,
        };

        var box = new PictureBox
        {
            Dock = DockStyle.Fill,
            Image = image,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.White,
        };

        dialog.Controls.Add(box);
        dialog.FormClosed += (_, _) => { box.Image?.Dispose(); dialog.Dispose(); };
        dialog.ShowDialog(this);
    }

    // === Arayüz yardımcıları ===

    private static Label CreateStatusLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Neutral,
        Font = new Font(DefaultFont.FontFamily, 10f, FontStyle.Bold),
        Margin = new Padding(4, 6, 4, 6),
    };

    private static Button CreateButton(string text, Action onClick)
    {
        var button = new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
            Height = 32,
            Margin = new Padding(0, 3, 0, 3),
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private static Button CreateBigButton(string text) => new()
    {
        Text = text,
        Height = 48,
        Width = 200,
        Font = new Font(DefaultFont.FontFamily, 11f, FontStyle.Bold),
        Margin = new Padding(6, 6, 6, 6),
    };

    private void SetStatus(Label label, string text, Color color)
    {
        RunOnUiThread(() =>
        {
            label.Text = text;
            label.ForeColor = color;
        });
    }

    private void SetReaderButtonsEnabled(bool enabled)
    {
        RunOnUiThread(() =>
        {
            _scanButton.Enabled = enabled;
            _ejectButton.Enabled = enabled;
        });
    }

    private void SetPrinterButtonsEnabled(bool enabled)
    {
        RunOnUiThread(() =>
        {
            foreach (var button in new[]
                     {
                         _refreshButton, _clearErrorButton, _previewButton,
                         _bothPreviewButton, _dryRunButton, _calibrationButton, _printButton,
                     })
            {
                button.Enabled = enabled;
            }
        });
    }

    /// <summary>
    /// Log paneline satır ekle.
    ///
    /// Java'da <c>SwingUtilities.invokeLater</c> ile yapılıyordu; WinForms
    /// karşılığı <see cref="Control.Invoke(Delegate)"/>.
    /// </summary>
    public void Log(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss}  {message}";
        RunOnUiThread(() =>
        {
            _logBox.AppendText(line + Environment.NewLine);
            _logBox.SelectionStart = _logBox.TextLength;
            _logBox.ScrollToCaret();
        });
    }

    /// <summary>İşi arayüz iş parçacığında çalıştır.</summary>
    private void RunOnUiThread(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;

        if (InvokeRequired) BeginInvoke(action);
        else action();
    }

    // === Kapanış ===

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        _pollingCancellation?.Cancel();
        _log.Info("Uygulama kapatılıyor");
        _devices.Dispose();
    }
}

/// <summary>
/// <c>async void</c> olay işleyicilerinden kaçınmak için — hatayı yutmak
/// yerine görünür kılar.
/// </summary>
internal static class TaskExtensions
{
    internal static void Forget(this Task task)
    {
        task.ContinueWith(t =>
        {
            if (t.Exception is { } ex)
            {
                MessageBox.Show(
                    ex.GetBaseException().Message,
                    "Beklenmeyen hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
    }
}
