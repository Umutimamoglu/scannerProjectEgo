package com.mobiloby;

import com.sun.jna.Library;
import com.sun.jna.Native;
import com.sun.jna.Structure;

import java.io.File;
import java.util.Arrays;
import java.util.List;

/**
 * JNA wrapper for Creator (China) Tech CRT-603-7005 scanner DLL (IDSIF.dll).
 *
 * Bağımlılıklar (sırayla yüklenmeli, IDSIF.dll'den önce):
 *   msvcr100d.dll, msvcp100d.dll  → VC++ 2010 runtime
 *   libusb0.dll                    → USB iletişim
 *   libimgprocess.dll              → resim işleme
 *   Wltrs.dll, sysntfy.dll, ieshims.dll → yardımcı
 *
 * Fonksiyon imzaları header dosyası olmadığı için "en muhtemel" pattern'lerle
 * yazıldı. Stdcall (WINAPI) Windows DLL'lerin standardı; çoğu C API böyle.
 */
public interface IDSIF extends Library {

    String NATIVE_DIR = new File("native").getAbsolutePath();

    /** DLL'leri doğru sırada yükle, sonra Native.load ile IDSIF'i bağla. */
    static IDSIF load() {
        String[] depsOrder = {
                "msvcr100d", "msvcp100d", "ieshims", "libusb0",
                "Wltrs", "sysntfy", "libimgprocess"
        };
        for (String dep : depsOrder) {
            File dll = new File(NATIVE_DIR, dep + ".dll");
            if (dll.exists()) {
                try {
                    System.load(dll.getAbsolutePath());
                    System.out.println("  loaded: " + dep);
                } catch (UnsatisfiedLinkError e) {
                    System.err.println("  yüklenemedi (devam): " + dep + " → " + e.getMessage());
                }
            }
        }
        System.setProperty("jna.library.path", NATIVE_DIR);
        return Native.load("IDSIF", IDSIF.class);
    }

    /** Device descriptor — log'dan: ulPid, ulVid, iFileDesc alanları. */
    class DevDesc extends Structure {
        public int ulPid;      // demo log: 0x102 (sentinel görünüyor)
        public int ulVid;      // demo log: 0x5e3 (sentinel görünüyor)
        public int iFileDesc;  // 0

        public DevDesc() { super(); }

        @Override
        protected List<String> getFieldOrder() {
            return Arrays.asList("ulPid", "ulVid", "iFileDesc");
        }

        public static class ByReference extends DevDesc implements Structure.ByReference {}
    }

    /** Dll_IDS_Entry → ST_IDS_FUN_TAB* (function table pointer). */
    com.sun.jna.Pointer Dll_IDS_Entry();

    // Init parametreli — cdecl, string + struct pointer
    int Init(String pcFileSavePath, DevDesc.ByReference pstDevDesc);
    int Uninit();
    int OpenDev(int index);
    int GetDevInfo();
    int Scan();
    // Move(pos): 0=holding, 1=IC, 2=RF/NFC, 3=errorBin, 4=gate (dışarı)
    int Move(int pos);
    int ReadPic(int side, byte[] buffer, com.sun.jna.ptr.IntByReference length);
    int CardStatus();
    int GetStatus();
    int GetScanAbility();
    int WaitCardIn(int timeoutMs);
    int WaitCardOut(int timeoutMs);
    int SetBuzzer(int onOff);
    int SetDpi(int dpi);
    int SetImageDpi(int dpi);
    int ChangeScanType(int type);
    int GetScanType();
    int GetCardScanStatus();
    int SetCardScanStatus(int status);
    int CtrlShutter(int open);
    int Test();
}
