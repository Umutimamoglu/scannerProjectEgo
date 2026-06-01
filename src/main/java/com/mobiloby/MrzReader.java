package com.mobiloby;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.TimeUnit;

/**
 * Demonun paketlediği tesseract.exe'yi çağırıp BMP'den MRZ'yi çıkartır,
 * pozisyon-bazlı OCR düzeltmesi yapıp BAC için 3 değer döndürür.
 *
 * Türk kimlik kartı MRZ formatı: ICAO 9303 TD1 (3 satır × 30 karakter)
 * Satır 1: I<TUR + docNo(9) + check(1) + opt(15)
 * Satır 2: dob(6) + dobCheck(1) + sex(1) + expiry(6) + expCheck(1) + nat(3) + opt(11) + composite(1)
 * Satır 3: surname<<names
 */
public class MrzReader {

    public static class Mrz {
        public final String documentNumber;
        public final String dateOfBirth;     // YYMMDD
        public final String dateOfExpiry;    // YYMMDD
        public final String sex;
        public final String nationality;
        public final String primaryName;     // soyad
        public final String secondaryName;   // ad
        public final String rawLine1, rawLine2, rawLine3;

        public Mrz(String docNo, String dob, String exp, String sex, String nat,
                   String surname, String givenName, String l1, String l2, String l3) {
            this.documentNumber = docNo;
            this.dateOfBirth = dob;
            this.dateOfExpiry = exp;
            this.sex = sex;
            this.nationality = nat;
            this.primaryName = surname;
            this.secondaryName = givenName;
            this.rawLine1 = l1; this.rawLine2 = l2; this.rawLine3 = l3;
        }

        @Override
        public String toString() {
            return "MRZ{ docNo=" + documentNumber +
                    ", dob=" + dateOfBirth +
                    ", exp=" + dateOfExpiry +
                    ", sex=" + sex +
                    ", nat=" + nationality +
                    ", surname=" + primaryName +
                    ", given=" + secondaryName + " }";
        }
    }

    private final Path tesseractExe;
    private final Path tesseractDir;

    public MrzReader(Path tesseractExe) {
        this.tesseractExe = tesseractExe;
        this.tesseractDir = tesseractExe.getParent();
    }

    /** BMP'yi OCR'la, MRZ satırlarını bul, parse et ve BAC değerlerini döndür. */
    public Mrz read(Path bmp) throws IOException, InterruptedException {
        if (!Files.exists(bmp)) {
            throw new IOException("BMP bulunamadı: " + bmp);
        }

        String outPrefix = "ocr_" + System.currentTimeMillis();
        Path outFile = tesseractDir.resolve(outPrefix + ".txt");
        try {
            ProcessBuilder pb = new ProcessBuilder(
                    tesseractExe.toString(),
                    bmp.toAbsolutePath().toString(),
                    outPrefix,
                    "-l", "mrz"
            );
            pb.directory(tesseractDir.toFile());
            pb.redirectErrorStream(true);
            Process p = pb.start();
            byte[] stderr = readAll(p.getInputStream());
            if (!p.waitFor(60, TimeUnit.SECONDS)) {
                p.destroyForcibly();
                throw new IOException("tesseract zaman aşımı");
            }
            if (!Files.exists(outFile)) {
                throw new IOException("OCR çıktısı yok. Tesseract stderr: " + new String(stderr));
            }

            String text = new String(Files.readAllBytes(outFile), StandardCharsets.UTF_8);
            return parse(text);
        } finally {
            Files.deleteIfExists(outFile);
        }
    }

    private static byte[] readAll(InputStream in) throws IOException {
        ByteArrayOutputStream out = new ByteArrayOutputStream();
        byte[] buf = new byte[4096];
        int n;
        while ((n = in.read(buf)) > 0) out.write(buf, 0, n);
        return out.toByteArray();
    }

    /** OCR metninin satırları arasından MRZ'yi yakala ve parse et. */
    public static Mrz parse(String ocrText) {
        List<String> candidates = new ArrayList<>();
        for (String raw : ocrText.split("\\r?\\n")) {
            String line = raw.replaceAll("\\s+", "");
            if (line.length() >= 25) candidates.add(line);
        }

        int idx = -1;
        for (int i = 0; i < candidates.size(); i++) {
            String l = candidates.get(i).toUpperCase();
            if ((l.startsWith("I<") || l.startsWith("ID")) && l.contains("TUR")) {
                idx = i;
                break;
            }
        }
        if (idx < 0 || idx + 2 >= candidates.size()) {
            throw new IllegalStateException(
                    "MRZ 3 satırı bulunamadı. OCR çıktısı:\n" + ocrText);
        }

        String l1 = candidates.get(idx);
        String l2 = candidates.get(idx + 1);
        String l3 = candidates.get(idx + 2);

        // Satır 1: "I<TUR" anchor → belge no = sonraki 9 karakter + 1 check digit
        int turIdx1 = l1.toUpperCase().indexOf("TUR");
        if (turIdx1 < 0 || turIdx1 + 13 > l1.length()) {
            throw new IllegalStateException("Satır 1'de TUR bulunamadı: " + l1);
        }
        String rawDocNo = l1.substring(turIdx1 + 3, turIdx1 + 12);
        char docCheck = l1.charAt(turIdx1 + 12);
        String docNo = correctWithCheckDigit(fixToAlnum(rawDocNo), docCheck);

        // Satır 2: "TUR" anchor → geriye doğru say
        //   ...<dob6><dobChk1><sex1><exp6><expChk1>TUR...
        int turIdx2 = -1;
        for (int i = l2.length() - 3; i >= 14; i--) {
            String sub = l2.substring(i, i + 3).toUpperCase();
            if (sub.matches("[A-Z]{3}")) { turIdx2 = i; break; }
        }
        if (turIdx2 < 0 || turIdx2 - 15 < 0) {
            throw new IllegalStateException("Satır 2'de nationality bulunamadı: " + l2);
        }
        String nat = l2.substring(turIdx2, turIdx2 + 3);
        String rawExp = l2.substring(turIdx2 - 7, turIdx2 - 1);
        char expCheckChar = l2.charAt(turIdx2 - 1);
        String exp = correctWithCheckDigit(fixToDigits(rawExp), expCheckChar);
        String sex = String.valueOf(l2.charAt(turIdx2 - 8)).toUpperCase();
        if (sex.equals("6") || sex.equals("H")) sex = "M";
        if (!sex.equals("F") && !sex.equals("M") && !sex.equals("<")) sex = "<";
        String rawDob = l2.substring(turIdx2 - 15, turIdx2 - 9);
        char dobCheckChar = l2.charAt(turIdx2 - 9);
        String dob = correctWithCheckDigit(fixToDigits(rawDob), dobCheckChar);

        String namesPart = l3.replace("<", " ").trim();
        String surname = namesPart, given = "";
        int sep = l3.indexOf("<<");
        if (sep > 0) {
            surname = l3.substring(0, sep).replace("<", " ").trim();
            given = l3.substring(sep + 2).replace("<", " ").trim();
        }

        return new Mrz(docNo, dob, exp, sex, nat, surname, given, l1, l2, l3);
    }

    /** Rakam olması gereken pozisyonda harfleri rakama çevir (OCR fix). */
    private static String fixToDigits(String s) {
        StringBuilder out = new StringBuilder(s.length());
        for (char c : s.toCharArray()) {
            switch (c) {
                case 'O': case 'o': case 'D': out.append('0'); break;
                case 'I': case 'l': case 'L': out.append('1'); break;
                case 'Z': out.append('2'); break;
                case 'S': out.append('5'); break;
                case 'B': out.append('8'); break;
                case 'G': case 'Q': out.append('9'); break;
                case '?': out.append('7'); break;
                case 'M': case 'H': case 'N': out.append('2'); break;
                default: out.append(c);
            }
        }
        return out.toString();
    }

    /** Alfanumerik pozisyonda yanlış glyph'leri düzelt — belge no için. */
    private static String fixToAlnum(String s) {
        return s.toUpperCase().replaceAll("[^A-Z0-9<]", "<");
    }

    /** ICAO 9303 MRZ check digit: ağırlıklar 7-3-1, mod 10. */
    private static int mrzCheck(String s) {
        int[] w = {7, 3, 1};
        int sum = 0;
        for (int i = 0; i < s.length(); i++) {
            char c = s.charAt(i);
            int v;
            if (c >= '0' && c <= '9') v = c - '0';
            else if (c >= 'A' && c <= 'Z') v = c - 'A' + 10;
            else v = 0; // '<' veya bilinmeyen
            sum += v * w[i % 3];
        }
        return sum % 10;
    }

    /** OCR bazen 0↔O, 1↔I, 5↔S karıştırır. Tüm kombinasyonları check digit'e karşı dene. */
    private static final char[][] CONFUSIONS = {
            {'0', 'O'}, {'1', 'I'}, {'5', 'S'}, {'8', 'B'}, {'2', 'Z'}
    };

    private static String correctWithCheckDigit(String raw, char expectedCheck) {
        if (expectedCheck < '0' || expectedCheck > '9') return raw;
        int target = expectedCheck - '0';
        if (mrzCheck(raw) == target) return raw;

        // Her karakter için olası alternatifleri topla
        int n = raw.length();
        char[][] alts = new char[n][];
        for (int i = 0; i < n; i++) {
            char c = raw.charAt(i);
            char[] a = {c, c, c};
            int k = 1;
            for (char[] pair : CONFUSIONS) {
                if (c == pair[0]) a[k++] = pair[1];
                else if (c == pair[1]) a[k++] = pair[0];
            }
            alts[i] = java.util.Arrays.copyOf(a, k);
        }

        // Tüm kombinasyonları dene (n=9 için max 2^9 = 512, hızlı)
        int[] sizes = new int[n], idx = new int[n];
        for (int i = 0; i < n; i++) sizes[i] = alts[i].length;
        char[] buf = new char[n];
        while (true) {
            for (int i = 0; i < n; i++) buf[i] = alts[i][idx[i]];
            String candidate = new String(buf);
            if (mrzCheck(candidate) == target) {
                if (!candidate.equals(raw)) {
                    System.out.println("  [MRZ fix] OCR='" + raw + "' → check digit '"
                            + expectedCheck + "' uyumlu = '" + candidate + "'");
                }
                return candidate;
            }
            int i = n - 1;
            while (i >= 0 && ++idx[i] >= sizes[i]) { idx[i] = 0; i--; }
            if (i < 0) break;
        }
        System.out.println("  [MRZ fix] check digit uymadı, OCR'ı koruyorum: " + raw);
        return raw;
    }

    private static String padTo30(String s) {
        if (s.length() >= 30) return s.substring(0, 30);
        StringBuilder sb = new StringBuilder(s);
        while (sb.length() < 30) sb.append('<');
        return sb.toString();
    }
}
