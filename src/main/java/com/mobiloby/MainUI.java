package com.mobiloby;

import javax.swing.*;
import javax.swing.border.EmptyBorder;
import javax.swing.border.TitledBorder;
import java.awt.*;
import java.awt.image.BufferedImage;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.LocalTime;
import java.time.format.DateTimeFormatter;
import java.util.LinkedHashMap;
import java.util.Map;

/**
 * Kimlik okuma ve kart baskı sistemi — tek arayüzden hem CRT-603-7005
 * kart okuyucusu hem Evolis KC Prime yazıcısı yönetilir.
 *
 * Tüm donanım çağrıları SwingWorker içinde arka planda çalışır; EDT hiç
 * bloklanmaz. Kart varlığı ayrı bir daemon thread ile yoklanır.
 *
 * Çalıştırma: run.bat ui
 */
public class MainUI extends JFrame {

    private static final Color BG = new Color(0xF5F6F8);
    private static final Color OK_GREEN = new Color(0x1B7F3B);
    private static final Color WARN_ORANGE = new Color(0xB8730A);
    private static final Color ERR_RED = new Color(0xB3261E);
    private static final Color NEUTRAL = new Color(0x5A5F66);

    private final IdCardReader reader = new IdCardReader();
    private final EvolisPrinter printer = new EvolisPrinter();

    // Okuyucu tarafı
    private JLabel readerStatus;
    private JLabel photoLabel;
    private final Map<String, JTextField> fields = new LinkedHashMap<>();
    private JTextArea mrzArea;
    private JButton scanBtn, ejectBtn;

    // Yazıcı tarafı
    private JLabel printerStatus;
    private JTextArea printerInfoArea;
    private JButton refreshBtn, clearErrBtn, previewBtn, backPreviewBtn, dryRunBtn, printBtn, calibBtn;

    private JButton reconnectBtn;
    private JTextArea logArea;
    private IdCardReader.IdData lastData;

    private volatile boolean pollingActive = true;
    private volatile int lastCardStatus = -99;
    private volatile boolean pollingStarted;

    public MainUI() {
        super("Kimlik Okuma ve Kart Baskı Sistemi");
        setDefaultCloseOperation(DO_NOTHING_ON_CLOSE);
        setLayout(new BorderLayout(10, 10));
        getContentPane().setBackground(BG);

        add(buildHeader(), BorderLayout.NORTH);

        JSplitPane split = new JSplitPane(JSplitPane.HORIZONTAL_SPLIT,
                buildReaderPanel(), buildPrinterPanel());
        split.setResizeWeight(0.62);
        split.setBorder(null);
        add(split, BorderLayout.CENTER);

        add(buildLogPanel(), BorderLayout.SOUTH);

        reader.setLogger(this::log);

        addWindowListener(new java.awt.event.WindowAdapter() {
            @Override public void windowClosing(java.awt.event.WindowEvent e) { shutdown(); }
        });

        setSize(1220, 860);
        setMinimumSize(new Dimension(1000, 700));
        setLocationRelativeTo(null);
    }

    // === Üst başlık ===

    private JComponent buildHeader() {
        JPanel p = new JPanel(new BorderLayout());
        p.setBackground(BG);
        p.setBorder(new EmptyBorder(14, 16, 4, 16));

        JLabel title = new JLabel("KİMLİK OKUMA VE KART BASKI SİSTEMİ");
        title.setFont(title.getFont().deriveFont(Font.BOLD, 20f));
        p.add(title, BorderLayout.WEST);

        reconnectBtn = new JButton("Cihazları Yeniden Bağla");
        reconnectBtn.addActionListener(e -> connectDevices());
        p.add(reconnectBtn, BorderLayout.EAST);
        return p;
    }

    // === Okuyucu paneli ===

    private JComponent buildReaderPanel() {
        JPanel panel = new JPanel(new BorderLayout(10, 10));
        panel.setBackground(BG);
        panel.setBorder(titled("Kart Okuyucu (CRT-603-7005)"));

        readerStatus = statusLabel("Başlatılıyor...");
        panel.add(readerStatus, BorderLayout.NORTH);

        JPanel center = new JPanel(new BorderLayout(12, 0));
        center.setOpaque(false);

        // Fotoğraf
        photoLabel = new JLabel("(foto)", SwingConstants.CENTER);
        photoLabel.setPreferredSize(new Dimension(200, 260));
        photoLabel.setBorder(BorderFactory.createLineBorder(new Color(0xC5C9CF)));
        photoLabel.setBackground(Color.WHITE);
        photoLabel.setOpaque(true);
        photoLabel.setForeground(NEUTRAL);
        JPanel photoWrap = new JPanel(new BorderLayout());
        photoWrap.setOpaque(false);
        photoWrap.setBorder(new TitledBorder("Biyometrik Fotoğraf"));
        photoWrap.add(photoLabel, BorderLayout.NORTH);
        center.add(photoWrap, BorderLayout.WEST);

        // Kimlik alanları — pencere küçüldüğünde kaydırılabilir
        JPanel form = new FormPanel();
        form.setOpaque(false);
        String[] labels = {"Ad", "Soyad", "T.C. No", "Belge No", "Doğum Tarihi",
                "Doğum Yeri", "Cinsiyet", "Uyruk", "Veriliş", "Geçerlilik", "Veren Kurum"};
        GridBagConstraints c = new GridBagConstraints();
        c.insets = new Insets(3, 6, 3, 6);
        c.anchor = GridBagConstraints.WEST;
        int row = 0;
        for (String label : labels) {
            c.gridx = 0; c.gridy = row; c.weightx = 0; c.fill = GridBagConstraints.NONE;
            form.add(new JLabel(label + ":"), c);

            JTextField f = new JTextField();
            f.setEditable(false);
            f.setBackground(Color.WHITE);
            c.gridx = 1; c.weightx = 1; c.fill = GridBagConstraints.HORIZONTAL;
            form.add(f, c);
            fields.put(label, f);
            row++;
        }
        c.gridx = 0; c.gridy = row; c.weighty = 1;
        form.add(Box.createVerticalGlue(), c);

        JScrollPane formScroll = new JScrollPane(form,
                JScrollPane.VERTICAL_SCROLLBAR_AS_NEEDED,
                JScrollPane.HORIZONTAL_SCROLLBAR_NEVER);
        formScroll.setBorder(new TitledBorder("Kimlik Bilgileri"));
        formScroll.getViewport().setOpaque(false);
        formScroll.setOpaque(false);
        formScroll.getVerticalScrollBar().setUnitIncrement(16);
        center.add(formScroll, BorderLayout.CENTER);

        panel.add(center, BorderLayout.CENTER);

        // MRZ
        mrzArea = new JTextArea(3, 30);
        mrzArea.setEditable(false);
        mrzArea.setFont(new Font(Font.MONOSPACED, Font.PLAIN, 12));
        mrzArea.setBackground(new Color(0xFFFDF3));
        JScrollPane mrzScroll = new JScrollPane(mrzArea);
        mrzScroll.setBorder(new TitledBorder("MRZ (kart arkası)"));

        JPanel south = new JPanel(new BorderLayout(0, 8));
        south.setOpaque(false);
        south.add(mrzScroll, BorderLayout.CENTER);

        JPanel buttons = new JPanel(new FlowLayout(FlowLayout.CENTER, 12, 6));
        buttons.setOpaque(false);
        scanBtn = bigButton("KARTI TARA");
        ejectBtn = bigButton("KARTI ÇIKAR");
        scanBtn.addActionListener(e -> doScanAndRead());
        ejectBtn.addActionListener(e -> doEject());
        buttons.add(scanBtn);
        buttons.add(ejectBtn);
        south.add(buttons, BorderLayout.SOUTH);

        panel.add(south, BorderLayout.SOUTH);
        return panel;
    }

    // === Yazıcı paneli ===

    private JComponent buildPrinterPanel() {
        JPanel panel = new JPanel(new BorderLayout(10, 10));
        panel.setBackground(BG);
        panel.setBorder(titled("Kart Yazıcı (Evolis KC Prime)"));

        printerStatus = statusLabel("Bağlanılıyor...");
        panel.add(printerStatus, BorderLayout.NORTH);

        printerInfoArea = new JTextArea();
        printerInfoArea.setEditable(false);
        printerInfoArea.setFont(new Font(Font.MONOSPACED, Font.PLAIN, 12));
        printerInfoArea.setBackground(Color.WHITE);
        JScrollPane scroll = new JScrollPane(printerInfoArea);
        scroll.setBorder(new TitledBorder("Durum ve Sayaçlar"));
        panel.add(scroll, BorderLayout.CENTER);

        JPanel buttons = new JPanel(new GridLayout(0, 1, 0, 6));
        buttons.setOpaque(false);
        buttons.setBorder(new EmptyBorder(8, 0, 0, 0));

        refreshBtn = new JButton("Bilgileri Yenile");
        clearErrBtn = new JButton("Hatayı Temizle");
        previewBtn = new JButton("Ön Yüz Önizle");
        backPreviewBtn = new JButton("Ön + Arka Önizle");
        dryRunBtn = new JButton("Prova Bas (kart harcamaz)");
        calibBtn = new JButton("Kalibrasyon Kartı Bas");
        printBtn = bigButton("KART BAS");

        refreshBtn.addActionListener(e -> refreshPrinterInfo());
        clearErrBtn.addActionListener(e -> doClearErrors());
        previewBtn.addActionListener(e -> doRenderCard(false));
        backPreviewBtn.addActionListener(e -> doRenderBoth());
        dryRunBtn.addActionListener(e -> doPrint(true));
        calibBtn.addActionListener(e -> doPrintCalibration());
        printBtn.addActionListener(e -> doPrint(false));

        buttons.add(refreshBtn);
        buttons.add(clearErrBtn);
        buttons.add(previewBtn);
        buttons.add(backPreviewBtn);
        buttons.add(dryRunBtn);
        buttons.add(calibBtn);
        buttons.add(printBtn);
        panel.add(buttons, BorderLayout.SOUTH);

        return panel;
    }

    private JComponent buildLogPanel() {
        logArea = new JTextArea(7, 80);
        logArea.setEditable(false);
        logArea.setFont(new Font(Font.MONOSPACED, Font.PLAIN, 11));
        JScrollPane scroll = new JScrollPane(logArea);
        scroll.setBorder(new TitledBorder("İşlem Günlüğü"));
        scroll.setPreferredSize(new Dimension(100, 150));
        JPanel wrap = new JPanel(new BorderLayout());
        wrap.setBackground(BG);
        wrap.setBorder(new EmptyBorder(0, 16, 12, 16));
        wrap.add(scroll, BorderLayout.CENTER);
        return wrap;
    }

    // === Başlatma ===

    /** Donanımı arka planda hazırla, sonra kart yoklamayı başlat. */
    public void startUp() {
        connectDevices();
    }

    /**
     * Okuyucu ve yazıcıya bağlan. Cihazlar sonradan takıldığında da
     * çağrılabilir — "Cihazları Yeniden Bağla" düğmesi bunu kullanır.
     */
    private void connectDevices() {
        setReaderButtonsEnabled(false);
        setPrinterButtonsEnabled(false);
        reconnectBtn.setEnabled(false);
        log("Cihazlar aranıyor...");

        new SwingWorker<Void, Void>() {
            boolean readerOk, printerOk;

            @Override protected Void doInBackground() {
                reader.close();          // varsa eski tutamacı bırak
                printer.disconnect();
                readerOk = reader.openDevice();
                printerOk = printer.connect();
                return null;
            }

            @Override protected void done() {
                if (readerOk) {
                    String fw = reader.getVersionInfo(2);
                    log("Okuyucu hazır" + (fw.isBlank() ? "" : " (firmware " + fw + ")"));
                } else {
                    setStatus(readerStatus, "Okuyucu bulunamadı — USB ve sürücüyü kontrol edin", ERR_RED);
                }
                setReaderButtonsEnabled(readerOk);

                if (printerOk) {
                    log("Yazıcı bağlandı: " + printer.getPrinterName());
                    refreshPrinterInfo();
                } else {
                    setStatus(printerStatus, "Yazıcı bulunamadı — açık mı, bağlı mı?", ERR_RED);
                    printerInfoArea.setText("Yazıcıya bağlanılamadı.\n\n"
                            + "Kontrol listesi:\n"
                            + "  • Yazıcı açık mı?\n"
                            + "  • USB kablosu takılı mı?\n"
                            + "  • Evolis Premium Suite kurulu mu?\n");
                }
                setPrinterButtonsEnabled(printerOk);
                reconnectBtn.setEnabled(true);

                if (readerOk) startCardPolling();
                if (!readerOk && !printerOk) {
                    log("Hiçbir cihaz bulunamadı. USB bağlantılarını kontrol edip "
                            + "'Cihazları Yeniden Bağla'ya basın.");
                }
            }
        }.execute();
    }

    /** Kart varlığını arka planda yokla, değişince UI'ı güncelle. */
    private synchronized void startCardPolling() {
        if (pollingStarted) return;      // yeniden bağlanmada ikinci thread açma
        pollingStarted = true;
        Thread t = new Thread(() -> {
            while (pollingActive) {
                int st = reader.getCardStatus();
                if (st != lastCardStatus) {
                    lastCardStatus = st;
                    SwingUtilities.invokeLater(() -> updateCardStatusLabel(st));
                }
                try { Thread.sleep(800); } catch (InterruptedException e) { return; }
            }
        }, "card-poll");
        t.setDaemon(true);
        t.start();
    }

    private void updateCardStatusLabel(int st) {
        switch (st) {
            case IdCardReader.CardStatus.NONE:
                setStatus(readerStatus, "Cihazda kart yok", NEUTRAL);
                break;
            case IdCardReader.CardStatus.MOVING:
                setStatus(readerStatus, "Kart hareket ediyor...", WARN_ORANGE);
                break;
            case IdCardReader.CardStatus.FRONT:
            case IdCardReader.CardStatus.INSIDE:
            case IdCardReader.CardStatus.BACK:
                setStatus(readerStatus, "Kart cihazda — TARA'ya basabilirsiniz", OK_GREEN);
                break;
            default:
                setStatus(readerStatus, "Kart durumu okunamıyor", ERR_RED);
        }
    }

    // === Okuyucu işlemleri ===

    private void doScanAndRead() {
        setReaderButtonsEnabled(false);
        clearFields();
        setStatus(readerStatus, "Taranıyor...", WARN_ORANGE);

        new SwingWorker<IdCardReader.IdData, Void>() {
            String error;

            @Override protected IdCardReader.IdData doInBackground() {
                try {
                    IdCardReader.ScanResult scan = reader.scan();
                    reader.moveToNfc();
                    IdCardReader.IdData d = reader.readChip(
                            scan.mrz.documentNumber, scan.mrz.dateOfBirth,
                            scan.mrz.dateOfExpiry, 10_000);
                    d.mrzLine1 = scan.mrz.rawLine1;
                    d.mrzLine2 = scan.mrz.rawLine2;
                    d.mrzLine3 = scan.mrz.rawLine3;
                    return d;
                } catch (Exception e) {
                    error = e.getMessage();
                    return null;
                }
            }

            @Override protected void done() {
                IdCardReader.IdData d;
                try { d = get(); } catch (Exception e) { d = null; error = e.getMessage(); }

                if (d == null) {
                    setStatus(readerStatus, "Okuma başarısız: " + error, ERR_RED);
                    log("HATA: " + error);
                } else {
                    lastData = d;
                    showData(d);
                    setStatus(readerStatus, "Kart okundu. Yeni kart için TARA'ya basın.", OK_GREEN);
                    log("Kimlik okundu: " + d.name + " " + d.surname);
                }
                setReaderButtonsEnabled(true);
            }
        }.execute();
    }

    private void doEject() {
        setReaderButtonsEnabled(false);
        new SwingWorker<Void, Void>() {
            @Override protected Void doInBackground() { reader.ejectCard(); return null; }
            @Override protected void done() {
                setReaderButtonsEnabled(true);
                log("Kart çıkarıldı.");
            }
        }.execute();
    }

    private void showData(IdCardReader.IdData d) {
        fields.get("Ad").setText(d.name);
        fields.get("Soyad").setText(d.surname);
        fields.get("T.C. No").setText(d.tcNo);
        fields.get("Belge No").setText(d.documentNumber);
        fields.get("Doğum Tarihi").setText(d.birthDate);
        fields.get("Doğum Yeri").setText(d.birthPlace);
        fields.get("Cinsiyet").setText(d.gender);
        fields.get("Uyruk").setText(d.nationality);
        fields.get("Veriliş").setText(d.issueDate);
        fields.get("Geçerlilik").setText(d.expiryDate);
        fields.get("Veren Kurum").setText(d.issuingAuthority);
        mrzArea.setText(d.mrzLine1 + "\n" + d.mrzLine2 + "\n" + d.mrzLine3);

        if (d.photo != null) {
            photoLabel.setText(null);
            photoLabel.setIcon(new ImageIcon(scaleToFit(d.photo,
                    photoLabel.getWidth() > 0 ? photoLabel.getWidth() : 200, 260)));
        }
    }

    private void clearFields() {
        fields.values().forEach(f -> f.setText(""));
        mrzArea.setText("");
        photoLabel.setIcon(null);
        photoLabel.setText("(foto)");
    }

    // === Yazıcı işlemleri ===

    private void refreshPrinterInfo() {
        setPrinterButtonsEnabled(false);
        new SwingWorker<String, Void>() {
            EvolisPrinter.State state;

            @Override protected String doInBackground() {
                state = printer.readState();
                StringBuilder sb = new StringBuilder();

                EvolisSDK.PrinterInfo.ByReference info = printer.readInfo();
                if (info != null) {
                    sb.append("YAZICI\n");
                    sb.append("  Model            : ").append(info.getModelName()).append('\n');
                    sb.append("  Seri no          : ").append(info.getSerialNumber()).append('\n');
                    sb.append("  Firmware         : ").append(info.getFwVersion()).append('\n');
                    sb.append("  Baskı kafası kiti: ").append(info.getPrintHeadKit()).append('\n');
                    sb.append("  Çift yüz (flip)  : ").append(info.hasFlip ? "var" : "yok").append('\n');
                    sb.append("  Temassız kodlama : ").append(info.hasContactLessEnc ? "var" : "yok").append('\n');
                    sb.append("  Manyetik kodlama : ").append(info.hasMagEnc ? "var" : "yok").append('\n');
                    sb.append('\n');
                } else {
                    sb.append("YAZICI\n  (okunamadı — evolis_get_info → ")
                      .append(EvolisPrinter.returnCodeName(printer.lastInfoRc)).append(")\n\n");
                }

                EvolisSDK.RibbonInfo.ByReference rb = printer.readRibbon();
                if (rb != null) {
                    sb.append("RİBON\n");
                    sb.append("  Tip              : ").append(rb.getDescription()).append('\n');
                    sb.append("  Ürün kodu        : ").append(rb.getProductCode()).append('\n');
                    sb.append("  Kapasite         : ").append(rb.capacity).append(" kart\n");
                    sb.append("  Kalan            : ").append(rb.remaining).append('\n');
                    sb.append('\n');
                } else {
                    sb.append("RİBON\n  (okunamadı — evolis_get_ribbon → ")
                      .append(EvolisPrinter.returnCodeName(printer.lastRibbonRc)).append(")\n\n");
                }

                EvolisSDK.CleaningInfo.ByReference cl = printer.readCleaning();
                if (cl != null) {
                    sb.append("SAYAÇLAR\n");
                    sb.append("  Toplam basılan   : ").append(cl.totalCardCount).append(" kart\n");
                    sb.append("  Son temizlikten  : ").append(cl.cardCount).append(" kart\n");
                    sb.append("  Temizliğe kalan  : ").append(cl.cardCountBeforeWarning).append(" kart\n");
                    sb.append("  Normal temizlik  : ").append(cl.regularCleaningCount).append(" kez\n");
                    sb.append("  Kafa garantide   : ").append(cl.printHeadUnderWarranty ? "evet" : "hayır").append('\n');
                    sb.append('\n');
                } else {
                    sb.append("SAYAÇLAR\n  (okunamadı — evolis_get_cleaning → ")
                      .append(EvolisPrinter.returnCodeName(printer.lastCleaningRc)).append(")\n\n");
                }

                sb.append("DURUM BAYRAKLARI\n");
                if (state.flags.isEmpty()) {
                    sb.append("  (yok)\n");
                } else {
                    for (String f : state.flags) sb.append("  ").append(f).append('\n');
                }
                return sb.toString();
            }

            @Override protected void done() {
                try {
                    printerInfoArea.setText(get());
                    printerInfoArea.setCaretPosition(0);
                } catch (Exception e) {
                    printerInfoArea.setText("Bilgiler okunamadı: " + e.getMessage());
                }
                Color col = state.hasError ? ERR_RED : state.hasWarning ? WARN_ORANGE : OK_GREEN;
                setStatus(printerStatus, printer.getPrinterName() + " — " + state.label(), col);
                setPrinterButtonsEnabled(true);
                if (state.hasError) {
                    log("UYARI: Yazıcıda hata bayrağı var. Baskı öncesi 'Hatayı Temizle' gerekebilir.");
                }
            }
        }.execute();
    }

    private void doClearErrors() {
        setPrinterButtonsEnabled(false);
        new SwingWorker<Boolean, Void>() {
            @Override protected Boolean doInBackground() { return printer.clearMechanicalErrors(); }
            @Override protected void done() {
                boolean ok = false;
                try { ok = get(); } catch (Exception ignore) {}
                log(ok ? "Mekanik hata temizlendi." : "Hata temizlenemedi.");
                setPrinterButtonsEnabled(true);
                refreshPrinterInfo();
            }
        }.execute();
    }

    /** Kart görselini üret. preview=false ise sadece dosyaya yazar. */
    private Path doRenderCard(boolean silent) {
        if (lastData == null) {
            JOptionPane.showMessageDialog(this,
                    "Önce bir kimlik kartı okutun.", "Veri yok", JOptionPane.WARNING_MESSAGE);
            return null;
        }
        try {
            CardRenderer.CardData cd = new CardRenderer.CardData();
            cd.name = lastData.name;
            cd.surname = lastData.surname;
            cd.idNumber = lastData.tcNo;
            cd.birthDate = lastData.birthDate;
            cd.expiryDate = lastData.expiryDate;
            Path photo = AppPaths.resolve("output", "dg2_face_1.png");
            if (Files.exists(photo)) cd.photo = photo;

            BufferedImage img = CardRenderer.render(cd);
            AppPaths.ensureDir("output");
            Path bmp = AppPaths.resolve("output", "card_print.bmp");
            javax.imageio.ImageIO.write(img, "bmp", bmp.toFile());
            javax.imageio.ImageIO.write(img, "png",
                    AppPaths.resolve("output", "card_preview.png").toFile());
            log("Kart görseli üretildi: " + bmp);

            if (!silent) showPreviewDialog(img);
            return bmp;
        } catch (Exception e) {
            log("Kart görseli üretilemedi: " + e.getMessage());
            return null;
        }
    }

    /**
     * Ön ve arka yüzü YAN YANA önizle. Ön yüz taranan kimlikten gelir; kart
     * taranmamışsa ön yüz boş şablon olarak (başlık + boş alanlar) gösterilir.
     * Arka yüz statiktir. Kart harcamaz.
     */
    private void doRenderBoth() {
        try {
            CardRenderer.CardData cd = new CardRenderer.CardData();
            if (lastData != null) {
                cd.name = lastData.name;
                cd.surname = lastData.surname;
                cd.idNumber = lastData.tcNo;
                cd.birthDate = lastData.birthDate;
                cd.expiryDate = lastData.expiryDate;
                Path photo = AppPaths.resolve("output", "dg2_face_1.png");
                if (Files.exists(photo)) cd.photo = photo;
            }
            BufferedImage front = CardRenderer.render(cd);
            BufferedImage back = CardRenderer.renderBack();
            BufferedImage both = sideBySide(front, back, mmToPreviewPx(6));

            AppPaths.ensureDir("output");
            javax.imageio.ImageIO.write(front, "bmp",
                    AppPaths.resolve("output", "card_print.bmp").toFile());
            javax.imageio.ImageIO.write(back, "bmp",
                    AppPaths.resolve("output", "card_back_print.bmp").toFile());
            javax.imageio.ImageIO.write(both, "png",
                    AppPaths.resolve("output", "card_both_preview.png").toFile());
            log("Ön+Arka önizleme üretildi"
                    + (lastData == null ? " (ön yüz boş — henüz kart taranmadı)" : ""));
            showPreviewDialog(both, 820, 660);
        } catch (Exception e) {
            log("Önizleme üretilemedi: " + e.getMessage());
        }
    }

    /** İki kart görselini gri zemin üstünde yan yana birleştirir. */
    private static BufferedImage sideBySide(BufferedImage a, BufferedImage b, int gap) {
        int w = a.getWidth() + gap + b.getWidth();
        int h = Math.max(a.getHeight(), b.getHeight());
        BufferedImage out = new BufferedImage(w, h, BufferedImage.TYPE_INT_RGB);
        Graphics2D g = out.createGraphics();
        g.setColor(new Color(0xDDDDDD));
        g.fillRect(0, 0, w, h);
        g.drawImage(a, 0, 0, null);
        g.drawImage(b, a.getWidth() + gap, 0, null);
        g.dispose();
        return out;
    }

    /** Önizleme birleştirmesinde kullanılan boşluk (kart 300 DPI ölçeğinde). */
    private static int mmToPreviewPx(double mm) {
        return (int) Math.round(mm / 25.4 * CardRenderer.DPI);
    }

    private void showPreviewDialog(BufferedImage img) {
        showPreviewDialog(img, 400, 640);
    }

    private void showPreviewDialog(BufferedImage img, int maxW, int maxH) {
        JDialog dlg = new JDialog(this, "Kart Önizleme", true);
        JLabel lbl = new JLabel(new ImageIcon(scaleToFit(img, maxW, maxH)));
        lbl.setBorder(new EmptyBorder(10, 10, 10, 10));
        dlg.add(lbl);
        dlg.pack();
        dlg.setLocationRelativeTo(this);
        dlg.setVisible(true);
    }

    /** ÇİFT YÜZ baskı: taranan kimliğin ön yüzü + statik arka yüz tek işte. */
    private void doPrint(boolean dryRun) {
        if (lastData == null) {
            JOptionPane.showMessageDialog(this,
                    "Önce bir kimlik kartı okutun.", "Veri yok", JOptionPane.WARNING_MESSAGE);
            return;
        }
        Path[] faces = renderFacesToBmp();
        if (faces == null) return;
        Path front = faces[0], back = faces[1];
        runPrintJob(!dryRun,
                "Gerçek ÇİFT YÜZ baskı yapılacak ve bir kart harcanacak.\n"
                + "Ön: kimlik  •  Arka: EGO tasarımı\n\nDevam edilsin mi?",
                dryRun ? "Çift yüz prova başlatıldı..." : "Çift yüz baskı başlatıldı...",
                () -> printer.printDuplex(front, back, dryRun));
    }

    /** Ön (kimlik) ve arka (statik) yüzü BMP'ye yazar, yollarını döndürür. */
    private Path[] renderFacesToBmp() {
        try {
            CardRenderer.CardData cd = new CardRenderer.CardData();
            cd.name = lastData.name;
            cd.surname = lastData.surname;
            cd.idNumber = lastData.tcNo;
            cd.birthDate = lastData.birthDate;
            cd.expiryDate = lastData.expiryDate;
            Path photo = AppPaths.resolve("output", "dg2_face_1.png");
            if (Files.exists(photo)) cd.photo = photo;

            AppPaths.ensureDir("output");
            Path front = AppPaths.resolve("output", "card_print.bmp");
            Path back = AppPaths.resolve("output", "card_back_print.bmp");
            javax.imageio.ImageIO.write(CardRenderer.render(cd), "bmp", front.toFile());
            // Baskıda arka yüz 180° döndürülür — kart çevrilince düz+renkli okunsun
            javax.imageio.ImageIO.write(CardRenderer.renderBackForPrint(), "bmp", back.toFile());
            return new Path[]{front, back};
        } catch (Exception e) {
            log("Görsel üretilemedi: " + e.getMessage());
            return null;
        }
    }

    /**
     * ÇİFT YÜZ kalibrasyon kartı bas — tek kartta hem ön hem arka bandın nereye
     * düştüğünü, hem de arka yüzün ters/düz mü çıktığını (ÜST/ALT işaretleri)
     * gösterir. Sonuca göre EvolisPrinter.shortPanelShift / CardRenderer.backRotate180
     * ayarlanır.
     */
    private void doPrintCalibration() {
        Path front, back;
        try {
            AppPaths.ensureDir("output");
            front = AppPaths.resolve("output", "card_calibration.bmp");
            back = AppPaths.resolve("output", "card_calibration_back.bmp");
            javax.imageio.ImageIO.write(CardRenderer.renderCalibration(), "bmp", front.toFile());
            javax.imageio.ImageIO.write(CardRenderer.renderCalibrationBack(), "bmp", back.toFile());
            log("Çift yüz kalibrasyon görselleri üretildi.");
        } catch (Exception e) {
            log("Kalibrasyon görseli üretilemedi: " + e.getMessage());
            return;
        }
        runPrintJob(true,
                "Çift yüz KALİBRASYON kartı basılacak ve bir kart harcanacak.\n\n"
                + "Baskı bitince NOT ET:\n"
                + "• Ön yüzde RENKLİ çıkan mm aralığı\n"
                + "• Arka yüzde RENKLİ çıkan mm aralığı\n"
                + "• Arka yüzde 'ARKA UST ^' yazısı üstte mi altta mı\n\n"
                + "Devam edilsin mi?",
                "Kalibrasyon baskısı başlatıldı...",
                () -> printer.printDuplex(front, back, false));
    }

    /** Onay + arka planda baskı + sonuç bildirimi (tek/çift yüz için ortak). */
    private void runPrintJob(boolean confirm, String confirmText, String startLog,
                             java.util.function.Supplier<EvolisPrinter.PrintResult> job) {
        if (confirm) {
            int answer = JOptionPane.showConfirmDialog(this, confirmText,
                    "Baskı onayı", JOptionPane.YES_NO_OPTION, JOptionPane.WARNING_MESSAGE);
            if (answer != JOptionPane.YES_OPTION) {
                log("Baskı iptal edildi.");
                return;
            }
        }

        setPrinterButtonsEnabled(false);
        log(startLog);

        new SwingWorker<EvolisPrinter.PrintResult, Void>() {
            @Override protected EvolisPrinter.PrintResult doInBackground() {
                return job.get();
            }

            @Override protected void done() {
                EvolisPrinter.PrintResult r;
                try { r = get(); } catch (Exception e) {
                    r = new EvolisPrinter.PrintResult(false, 0, e.getMessage());
                }
                log((r.ok ? "OK: " : "HATA: ") + r.message);
                if (!r.ok) {
                    JOptionPane.showMessageDialog(MainUI.this, r.message,
                            "Baskı sonucu", JOptionPane.ERROR_MESSAGE);
                }
                setPrinterButtonsEnabled(true);
                refreshPrinterInfo();
            }
        }.execute();
    }

    // === Yardımcılar ===

    private void setReaderButtonsEnabled(boolean on) {
        scanBtn.setEnabled(on);
        ejectBtn.setEnabled(on);
    }

    private void setPrinterButtonsEnabled(boolean on) {
        refreshBtn.setEnabled(on);
        clearErrBtn.setEnabled(on);
        previewBtn.setEnabled(on);
        backPreviewBtn.setEnabled(on);
        dryRunBtn.setEnabled(on);
        calibBtn.setEnabled(on);
        printBtn.setEnabled(on);
    }

    private void setStatus(JLabel label, String text, Color color) {
        label.setText("● " + text);
        label.setForeground(color);
    }

    private JLabel statusLabel(String text) {
        JLabel l = new JLabel("● " + text);
        l.setFont(l.getFont().deriveFont(Font.BOLD, 13f));
        l.setForeground(NEUTRAL);
        l.setBorder(new EmptyBorder(2, 4, 8, 4));
        return l;
    }

    private JButton bigButton(String text) {
        JButton b = new JButton(text);
        b.setFont(b.getFont().deriveFont(Font.BOLD, 14f));
        b.setPreferredSize(new Dimension(190, 42));
        return b;
    }

    /**
     * Kimlik alanları paneli. Scrollable'ı uygular ki JScrollPane içinde
     * genişliği viewport'u takip etsin — aksi halde metin kutuları
     * tercih ettikleri dar boyutta kalır ve yatay kaydırma çubuğu çıkar.
     */
    private static class FormPanel extends JPanel implements Scrollable {
        FormPanel() { super(new GridBagLayout()); }

        @Override public Dimension getPreferredScrollableViewportSize() { return getPreferredSize(); }
        @Override public int getScrollableUnitIncrement(Rectangle r, int orient, int dir) { return 16; }
        @Override public int getScrollableBlockIncrement(Rectangle r, int orient, int dir) { return r.height; }
        @Override public boolean getScrollableTracksViewportWidth() { return true; }
        @Override public boolean getScrollableTracksViewportHeight() {
            Container parent = getParent();
            return parent instanceof JViewport && parent.getHeight() > getPreferredSize().height;
        }
    }

    private static TitledBorder titled(String text) {
        TitledBorder b = BorderFactory.createTitledBorder(text);
        b.setTitleFont(b.getTitleFont().deriveFont(Font.BOLD, 13f));
        return b;
    }

    private static Image scaleToFit(BufferedImage img, int maxW, int maxH) {
        double scale = Math.min((double) maxW / img.getWidth(), (double) maxH / img.getHeight());
        int w = Math.max(1, (int) Math.round(img.getWidth() * scale));
        int h = Math.max(1, (int) Math.round(img.getHeight() * scale));
        return img.getScaledInstance(w, h, Image.SCALE_SMOOTH);
    }

    private void log(String msg) {
        String line = LocalTime.now().format(DateTimeFormatter.ofPattern("HH:mm:ss")) + "  " + msg;
        System.out.println(line);   // konsola da yansıt (tanılama/log dosyası için)
        SwingUtilities.invokeLater(() -> {
            logArea.append(line + "\n");
            logArea.setCaretPosition(logArea.getDocument().getLength());
        });
    }

    private void shutdown() {
        pollingActive = false;
        setReaderButtonsEnabled(false);
        setPrinterButtonsEnabled(false);
        new SwingWorker<Void, Void>() {
            @Override protected Void doInBackground() {
                try { reader.ejectCard(); } catch (Throwable ignore) {}
                try { reader.close(); } catch (Throwable ignore) {}
                try { printer.disconnect(); } catch (Throwable ignore) {}
                return null;
            }
            @Override protected void done() { dispose(); System.exit(0); }
        }.execute();
    }

    public static void main(String[] args) {
        try {
            UIManager.setLookAndFeel(UIManager.getSystemLookAndFeelClassName());
        } catch (Exception ignore) {}

        SwingUtilities.invokeLater(() -> {
            MainUI ui = new MainUI();
            ui.setVisible(true);
            ui.startUp();
        });
    }
}
