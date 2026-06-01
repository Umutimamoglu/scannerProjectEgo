package com.mobiloby;

import javax.smartcardio.*;
import java.util.List;

public class Main {
    public static void main(String[] args) {
        try {
            TerminalFactory factory = TerminalFactory.getDefault();
            List<CardTerminal> terminals = factory.terminals().list();

            if (terminals.isEmpty()) {
                System.out.println("Okuyucu bulunamadı!");
                return;
            }

            CardTerminal okuyucu = terminals.get(0);
            System.out.println("Aktif Okuyucu: " + okuyucu.getName());
            System.out.println("\nLütfen kimliği cihazın içine (RF okuma alanına) yerleştirin...");

            if (okuyucu.isCardPresent()) {
                System.out.println("Kimlik Algılandı! Çipe bağlanılıyor...");
                Card kart = okuyucu.connect("*");
                System.out.println("BAŞARILI: Çip ile bağlantı kuruldu!");

                ATR atr = kart.getATR();
                byte[] atrBytes = atr.getBytes();

                System.out.print("Çip ATR Kodu (Hex): ");
                for (byte b : atrBytes) {
                    System.out.printf("%02X ", b);
                }
                System.out.println("\n");

                kart.disconnect(false);
            } else {
                System.out.println("HATA: Cihazın içinde kimlik bulunamadı. Lütfen kimliği yerleştirip tekrar deneyin.");
            }

        } catch (Exception e) {
            System.err.println("Bağlantı Hatası: " + e.getMessage());
            e.printStackTrace();
        }
    }
}
