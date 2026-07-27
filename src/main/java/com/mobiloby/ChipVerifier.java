package com.mobiloby;

import org.bouncycastle.jce.provider.BouncyCastleProvider;
import org.jmrtd.lds.SODFile;

import java.io.InputStream;
import java.nio.file.Files;
import java.nio.file.Path;
import java.security.MessageDigest;
import java.security.PublicKey;
import java.security.Security;
import java.security.Signature;
import java.security.cert.CertPath;
import java.security.cert.CertPathValidator;
import java.security.cert.CertificateFactory;
import java.security.cert.PKIXCertPathValidatorResult;
import java.security.cert.PKIXParameters;
import java.security.cert.TrustAnchor;
import java.security.cert.X509Certificate;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collections;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.function.Consumer;

/**
 * Çip verisinin gerçekliğini ICAO 9303 Part 11'e göre doğrular.
 *
 * Şu an sadece Passive Authentication'ın ilk adımı (DG hash karşılaştırma)
 * uygulanmıştır — "çipteki veri tahrif edilmiş mi?" sorusunu cevaplar.
 *
 * Sıradaki adımlar (henüz uygulanmadı):
 *   - Faz 3: SOD imzasını Document Signer sertifikasıyla doğrula + o sertifikayı
 *            CSCA kök sertifikalarına kadar zincirle (sahte çip tespiti)
 *   - Faz 4: DG15 + Active Authentication challenge-response (klon çip tespiti)
 *
 * Bilgilendirici mod: hiçbir şeyi reddetmez, sadece sonucu raporlar. Güven
 * oturduktan sonra çağıran taraf "başarısızsa reddet" politikasına geçebilir.
 */
public class ChipVerifier {

    static {
        if (Security.getProvider(BouncyCastleProvider.PROVIDER_NAME) == null) {
            Security.addProvider(new BouncyCastleProvider());
        }
    }

    /** Tek bir kontrolün sonucu (bir DG hash'i gibi). */
    public static class Check {
        public final String name;
        public final boolean passed;
        public final String detail;
        public Check(String name, boolean passed, String detail) {
            this.name = name; this.passed = passed; this.detail = detail;
        }
    }

    /** Passive Authentication toplam sonucu. */
    public static class Result {
        public final List<Check> checks = new ArrayList<>();
        public boolean hashCheckRun = false;   // hash adımı çalıştı mı
        public boolean allHashesMatched = true; // çalıştıysa hepsi tuttu mu
        public int trustAnchorCount = 0;

        public boolean sodSignatureValid = false;  // SOD imzası DS sertifikasıyla doğrulandı mı
        public boolean chainValid = false;         // DS sertifikası köke kadar zincirlendi mi
        public String matchedRootCN = null;        // hangi kök sertifikaya bağlandı (= sürüm)

        /** İnsan-okur özet satırları. */
        public List<String> report() {
            List<String> out = new ArrayList<>();
            for (Check c : checks) {
                out.add((c.passed ? "  [PA ✓] " : "  [PA ✗] ") + c.name
                        + (c.detail == null || c.detail.isBlank() ? "" : " — " + c.detail));
            }
            if (hashCheckRun) {
                out.add(allHashesMatched
                        ? "  [PA] Tüm DG hash'leri SOD ile eşleşti (veri tahrif edilmemiş)."
                        : "  [PA] DİKKAT: en az bir DG hash'i tutmadı — veri değişmiş olabilir!");
            }
            if (matchedRootCN != null) {
                out.add("  [PA] Kart şu kök sertifikaya bağlı: " + matchedRootCN);
            }
            return out;
        }
    }

    private final Set<TrustAnchor> trustAnchors;
    private final Consumer<String> log;

    public ChipVerifier(Path certDir, Consumer<String> logger) {
        this.log = logger != null ? logger : s -> {};
        this.trustAnchors = loadTrustAnchors(certDir);
    }

    public int trustAnchorCount() { return trustAnchors.size(); }

    /** certs/ klasöründeki tüm X.509 sertifikalarını trust anchor olarak yükle. */
    private Set<TrustAnchor> loadTrustAnchors(Path certDir) {
        Set<TrustAnchor> anchors = new HashSet<>();
        if (certDir == null || !Files.isDirectory(certDir)) {
            log.accept("  [PA] UYARI: sertifika klasörü bulunamadı: " + certDir);
            return anchors;
        }
        try {
            CertificateFactory cf = CertificateFactory.getInstance("X.509");
            List<Path> files = new ArrayList<>();
            try (var stream = Files.list(certDir)) {
                stream.filter(Files::isRegularFile)
                      .filter(p -> {
                          String n = p.getFileName().toString().toLowerCase();
                          return n.endsWith(".cer") || n.endsWith(".crt")
                              || n.endsWith(".der") || n.endsWith(".pem");
                      })
                      .forEach(files::add);
            }
            for (Path f : files) {
                try (InputStream in = Files.newInputStream(f)) {
                    X509Certificate cert = (X509Certificate) cf.generateCertificate(in);
                    anchors.add(new TrustAnchor(cert, null));
                    log.accept("  [PA] Kök sertifika yüklendi: "
                            + cert.getSubjectX500Principal().getName().replaceAll(".*CN=([^,]*).*", "$1"));
                } catch (Exception e) {
                    log.accept("  [PA] Sertifika yüklenemedi (" + f.getFileName() + "): " + e.getMessage());
                }
            }
        } catch (Exception e) {
            log.accept("  [PA] Sertifika klasörü okunamadı: " + e.getMessage());
        }
        return anchors;
    }

    /**
     * Passive Authentication — Kontrol 1: DG hash karşılaştırma.
     *
     * @param sod      çipten okunan Document Security Object
     * @param rawDgs   DG numarası → çipten okunan HAM byte (parse edilmemiş)
     */
    public Result verifyPassiveAuth(SODFile sod, Map<Integer, byte[]> rawDgs) {
        Result r = new Result();
        r.trustAnchorCount = trustAnchors.size();

        if (sod == null) {
            r.checks.add(new Check("SOD okunamadı", false, "doğrulama yapılamıyor"));
            return r;
        }

        String digestAlg = sod.getDigestAlgorithm();   // örn. "SHA-256"
        Map<Integer, byte[]> expected = sod.getDataGroupHashes();
        r.checks.add(new Check("SOD okundu", true,
                "özet algoritması: " + digestAlg + ", " + expected.size() + " DG hash'i içeriyor"));

        MessageDigest md;
        try {
            md = MessageDigest.getInstance(digestAlg);
        } catch (Exception e) {
            try {
                md = MessageDigest.getInstance(digestAlg, BouncyCastleProvider.PROVIDER_NAME);
            } catch (Exception e2) {
                r.checks.add(new Check("Hash algoritması bulunamadı", false, digestAlg));
                return r;
            }
        }

        r.hashCheckRun = true;
        for (Map.Entry<Integer, byte[]> e : rawDgs.entrySet()) {
            int dg = e.getKey();
            byte[] exp = expected.get(dg);
            if (exp == null) {
                r.checks.add(new Check("DG" + dg, false, "SOD'da bu DG için hash yok"));
                r.allHashesMatched = false;
                continue;
            }
            byte[] actual = md.digest(e.getValue());
            boolean ok = Arrays.equals(actual, exp);
            if (!ok) r.allHashesMatched = false;
            r.checks.add(new Check("DG" + dg + " hash", ok,
                    ok ? "eşleşti" : "TUTMADI (beklenen " + hex(exp, 8) + ", bulunan " + hex(actual, 8) + ")"));
        }

        // Kontrol 2: SOD imzası gerçek mi + hangi köke bağlı (sürüm)
        verifyDocumentSigner(sod, r);
        return r;
    }

    /**
     * Passive Authentication — Kontrol 2:
     *   (a) SOD imzasını, içindeki Document Signer sertifikasıyla doğrula
     *   (b) O sertifikayı CSCA kök sertifikalarına kadar zincirle
     * Zincir tutarsa r.matchedRootCN hangi köke (= hangi sürüme) bağlı olduğunu söyler.
     */
    private void verifyDocumentSigner(SODFile sod, Result r) {
        X509Certificate ds;
        try {
            ds = sod.getDocSigningCertificate();
        } catch (Exception e) {
            r.checks.add(new Check("Document Signer sertifikası", false, "SOD'dan alınamadı: " + e.getMessage()));
            return;
        }
        if (ds == null) {
            r.checks.add(new Check("Document Signer sertifikası", false, "SOD'da yok"));
            return;
        }
        r.checks.add(new Check("Document Signer sertifikası bulundu", true,
                cn(ds.getSubjectX500Principal().getName())));

        // (a) SOD imzasını DS public key ile doğrula
        try {
            PublicKey dsKey = ds.getPublicKey();
            String sigAlg = deriveSigAlg(sod, dsKey);
            Signature sig = getSignature(sigAlg);
            sig.initVerify(dsKey);
            sig.update(sod.getEContent());
            r.sodSignatureValid = sig.verify(sod.getEncryptedDigest());
            r.checks.add(new Check("SOD imzası", r.sodSignatureValid,
                    r.sodSignatureValid ? "geçerli (" + sigAlg + ")" : "GEÇERSİZ (" + sigAlg + ")"));
        } catch (Exception e) {
            r.checks.add(new Check("SOD imzası", false, "doğrulanamadı: " + e.getMessage()));
        }

        // (b) DS sertifikasını kök sertifikalara kadar zincirle
        if (trustAnchors.isEmpty()) {
            r.checks.add(new Check("Sertifika zinciri", false, "yüklü kök sertifika yok"));
            return;
        }
        try {
            CertificateFactory cf = CertificateFactory.getInstance("X.509");
            CertPath cp = cf.generateCertPath(Collections.singletonList(ds));
            PKIXParameters params = new PKIXParameters(trustAnchors);
            params.setRevocationEnabled(false);   // CRL/OCSP yok, çevrimdışı doğrulama
            CertPathValidator cpv = CertPathValidator.getInstance("PKIX");
            PKIXCertPathValidatorResult res = (PKIXCertPathValidatorResult) cpv.validate(cp, params);
            r.chainValid = true;
            r.matchedRootCN = cn(res.getTrustAnchor().getTrustedCert().getSubjectX500Principal().getName());
            r.checks.add(new Check("Sertifika zinciri", true, "köke ulaştı: " + r.matchedRootCN));
        } catch (Exception e) {
            r.checks.add(new Check("Sertifika zinciri", false,
                    "hiçbir köke bağlanamadı: " + e.getMessage()));
        }
    }

    /** SOD'un imza algoritmasını JCA adına çevir (örn. SHA256withRSA). */
    private static String deriveSigAlg(SODFile sod, PublicKey dsKey) {
        // Önce SOD'un verdiği adı dene
        try {
            String a = sod.getDigestEncryptionAlgorithm();
            if (a != null && a.toLowerCase().contains("with")) return a;
        } catch (Exception ignore) {}
        // Aksi halde: özet algoritması + anahtar tipinden kur
        String digest = "SHA-256";
        try {
            String d = sod.getSignerInfoDigestAlgorithm();
            if (d != null && !d.isBlank()) digest = d;
        } catch (Exception ignore) {}
        String d = digest.replace("-", "").toUpperCase();   // SHA-256 -> SHA256
        String keyAlg = dsKey.getAlgorithm();               // RSA / EC / ...
        String with = "EC".equalsIgnoreCase(keyAlg) ? "ECDSA" : keyAlg;
        return d + "with" + with;
    }

    private static Signature getSignature(String alg) throws Exception {
        try {
            return Signature.getInstance(alg);
        } catch (Exception e) {
            return Signature.getInstance(alg, BouncyCastleProvider.PROVIDER_NAME);
        }
    }

    private static String cn(String dn) {
        return dn.replaceAll(".*?CN=([^,]*).*", "$1");
    }

    private static String hex(byte[] b, int max) {
        StringBuilder sb = new StringBuilder();
        int n = Math.min(max, b.length);
        for (int i = 0; i < n; i++) sb.append(String.format("%02x", b[i]));
        if (b.length > n) sb.append("…");
        return sb.toString();
    }
}
