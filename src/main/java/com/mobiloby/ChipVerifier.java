package com.mobiloby;

import org.bouncycastle.cert.X509CertificateHolder;
import org.bouncycastle.cert.jcajce.JcaX509CertificateHolder;
import org.bouncycastle.cms.CMSSignedData;
import org.bouncycastle.cms.SignerInformation;
import org.bouncycastle.cms.jcajce.JcaSimpleSignerInfoVerifierBuilder;
import org.bouncycastle.crypto.Digest;
import org.bouncycastle.crypto.digests.SHA1Digest;
import org.bouncycastle.crypto.digests.SHA224Digest;
import org.bouncycastle.crypto.digests.SHA256Digest;
import org.bouncycastle.crypto.digests.SHA384Digest;
import org.bouncycastle.crypto.digests.SHA512Digest;
import org.bouncycastle.crypto.engines.RSAEngine;
import org.bouncycastle.crypto.params.RSAKeyParameters;
import org.bouncycastle.crypto.signers.ISO9796d2Signer;
import org.bouncycastle.jce.provider.BouncyCastleProvider;
import org.jmrtd.lds.SODFile;

import java.io.ByteArrayInputStream;
import java.io.InputStream;
import java.nio.file.Files;
import java.nio.file.Path;
import java.security.MessageDigest;
import java.security.PublicKey;
import java.security.Security;
import java.security.Signature;
import java.security.interfaces.RSAPublicKey;
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

        public boolean aaAttempted = false;        // Active Authentication denendi mi (DG15 var mı)
        public boolean aaValid = false;            // AA challenge-response doğrulandı mı

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
            if (aaAttempted) {
                out.add(aaValid
                        ? "  [AA] Active Authentication GEÇTİ — çip gerçek (klon değil)."
                        : "  [AA] Active Authentication BAŞARISIZ — çip klonlanmış olabilir!");
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
     * Passive Authentication.
     *   Kontrol 1: DG hash karşılaştırma (veri tahrif edilmiş mi)
     *   Kontrol 2: SOD imzası + sertifika zinciri (imza gerçek mi, hangi sürüm)
     *
     * @param rawSod   çipten okunan EF.SOD'un HAM byte'ları (0x77 sarmalı dahil)
     * @param rawDgs   DG numarası → çipten okunan HAM byte (parse edilmemiş)
     */
    public Result verifyPassiveAuth(byte[] rawSod, Map<Integer, byte[]> rawDgs) {
        Result r = new Result();
        r.trustAnchorCount = trustAnchors.size();

        if (rawSod == null) {
            r.checks.add(new Check("SOD okunamadı", false, "doğrulama yapılamıyor"));
            return r;
        }
        SODFile sod;
        try {
            sod = new SODFile(new ByteArrayInputStream(rawSod));
        } catch (Exception e) {
            r.checks.add(new Check("SOD çözümlenemedi", false, e.getMessage()));
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
        verifyDocumentSigner(sod, rawSod, r);
        return r;
    }

    /**
     * Passive Authentication — Kontrol 2:
     *   (a) SOD imzasını CMS motoruyla doğrula (signed attributes + PSS/PKCS1 otomatik)
     *   (b) İçindeki Document Signer sertifikasını CSCA köklerine kadar zincirle
     * Zincir tutarsa r.matchedRootCN hangi köke (= hangi sürüme) bağlı olduğunu söyler.
     */
    private void verifyDocumentSigner(SODFile sod, byte[] rawSod, Result r) {
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

        // (a) SOD imzasını CMS ile doğrula. EF.SOD = 0x77 sarmalı içinde ContentInfo (CMS).
        try {
            byte[] cms = stripApplicationTag(rawSod);
            CMSSignedData signedData = new CMSSignedData(cms);
            X509CertificateHolder holder = new JcaX509CertificateHolder(ds);
            boolean verified = false;
            for (SignerInformation signer : signedData.getSignerInfos().getSigners()) {
                verified = signer.verify(new JcaSimpleSignerInfoVerifierBuilder()
                        .setProvider(BouncyCastleProvider.PROVIDER_NAME).build(holder));
                if (verified) break;
            }
            r.sodSignatureValid = verified;
            r.checks.add(new Check("SOD imzası", verified,
                    verified ? "geçerli (CMS: signed attributes doğrulandı)" : "GEÇERSİZ"));
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

    /**
     * Active Authentication doğrulaması (klon çip tespiti).
     * Çipe gönderilen challenge, çipin DG15'teki public key'e karşılık gelen
     * özel anahtarıyla imzalanmış olmalı. DG15'in gerçekliği PA ile kanıtlanmış
     * olmalıdır (hash kontrolü) — aksi halde bu kontrol anlamsızdır.
     *
     * @return raporlanacak tek satır (çağıran loglar)
     */
    public static String verifyActiveAuth(PublicKey aaKey, byte[] challenge, byte[] response, Result r) {
        r.aaAttempted = true;
        String keyAlg = aaKey.getAlgorithm();
        boolean ok = false;
        String detail;
        try {
            if ("RSA".equalsIgnoreCase(keyAlg)) {
                ok = verifyAaRsa((RSAPublicKey) aaKey, challenge, response);
                detail = ok ? "RSA / ISO9796-2 challenge-response doğrulandı" : "RSA imza tutmadı";
            } else if ("EC".equalsIgnoreCase(keyAlg) || "ECDSA".equalsIgnoreCase(keyAlg)) {
                ok = verifyAaEc(aaKey, challenge, response);
                detail = ok ? "ECDSA challenge-response doğrulandı" : "ECDSA imza tutmadı";
            } else {
                detail = "bilinmeyen AA anahtar tipi: " + keyAlg;
            }
        } catch (Exception e) {
            detail = "hata: " + e.getMessage();
        }
        r.aaValid = ok;
        r.checks.add(new Check("Active Authentication", ok, detail));
        return (ok ? "  [AA ✓] " : "  [AA ✗] ") + "Active Authentication — " + detail;
    }

    /** RSA AA: ISO/IEC 9796-2 scheme 1, mesaj kurtarmalı. Yaygın özet varyantlarını dener. */
    private static boolean verifyAaRsa(RSAPublicKey key, byte[] challenge, byte[] response) {
        RSAKeyParameters params = new RSAKeyParameters(false, key.getModulus(), key.getPublicExponent());
        // {digest, implicit-trailer?} kombinasyonları — kart hangisini kullandıysa tutar
        Object[][] combos = {
            { new SHA1Digest(),   Boolean.TRUE  },
            { new SHA256Digest(), Boolean.FALSE },
            { new SHA1Digest(),   Boolean.FALSE },
            { new SHA224Digest(), Boolean.FALSE },
            { new SHA384Digest(), Boolean.FALSE },
            { new SHA512Digest(), Boolean.FALSE },
        };
        for (Object[] c : combos) {
            try {
                ISO9796d2Signer signer = new ISO9796d2Signer(
                        new RSAEngine(), (Digest) c[0], (Boolean) c[1]);
                signer.init(false, params);
                signer.update(challenge, 0, challenge.length);
                if (signer.verifySignature(response)) return true;
            } catch (Exception ignore) {
                // bu kombinasyon değil, sıradakini dene
            }
        }
        return false;
    }

    /** EC AA: ECDSA-Plain (r||s bitişik). Yaygın özet varyantlarını dener. */
    private static boolean verifyAaEc(PublicKey key, byte[] challenge, byte[] response) {
        String[] algs = {
            "SHA256withPLAIN-ECDSA", "SHA1withPLAIN-ECDSA", "SHA224withPLAIN-ECDSA",
            "SHA384withPLAIN-ECDSA", "SHA512withPLAIN-ECDSA"
        };
        for (String alg : algs) {
            try {
                Signature s = Signature.getInstance(alg, BouncyCastleProvider.PROVIDER_NAME);
                s.initVerify(key);
                s.update(challenge);
                if (s.verify(response)) return true;
            } catch (Exception ignore) {
                // sıradaki
            }
        }
        return false;
    }

    /**
     * EF.SOD dış sarmalını (ASN.1 APPLICATION 23, 0x77) soyup içindeki
     * ContentInfo (CMS) DER byte'larını döndür. Sarmal yoksa girdiyi aynen verir.
     */
    private static byte[] stripApplicationTag(byte[] ef) {
        if (ef == null || ef.length < 2 || (ef[0] & 0xFF) != 0x77) return ef;
        int idx = 1;
        int first = ef[idx++] & 0xFF;
        int len;
        if (first < 0x80) {
            len = first;
        } else {
            int n = first & 0x7F;
            if (n == 0 || idx + n > ef.length) return ef;   // tanımsız uzunluk vb. — dokunma
            len = 0;
            for (int i = 0; i < n; i++) len = (len << 8) | (ef[idx++] & 0xFF);
        }
        if (idx + len > ef.length) return ef;
        return Arrays.copyOfRange(ef, idx, idx + len);
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
