# DURUM

**Sürüm:** v1.0.0 — 2026-11-19
**Repo:** https://github.com/avsessisozx/EkranIsikKontrol · yerel klasör `E:\cloude\EkranIsikKontrol`

## Doğrulananlar (gerçek bilgisayarda: ASUS VG249, GTX 1050 Ti, Win10 19045)
- DDC/CI: parlaklık/kontrast/renk ön ayarı/ses/giriş okunuyor; parlaklık yazma ve geri alma çalışıyor
- Çözünürlük/Hz: 120 Hz geçici uygulandı, onaysız 15 sn sonra 144 Hz'e döndü
- Gece ışığı: aç/kapat ve sıcaklık kayıt defterine doğru yazılıyor, Windows değeri kabul ediyor
- Fare: OpenRGB sunucusu gizli başlıyor, Rival 3 (4 LED) bulunuyor, renk gönderiliyor
- Hızlı panel (tepsi tıklaması), ana pencere, profil uygulama
- 15 birim testi + GitHub Actions derlemesi geçiyor

## Doğrulanmayanlar
- Gece ışığı sıcaklık değişiminin ekranda gözle görülen etkisi (ekran görüntüsünde görünmez; kullanıcı onayı bekleniyor)
- Fare renginin farede gözle görülmesi ve SteelSeries GG ile çakışma
- Giriş kaynağı değiştirme (bilerek denenmedi: ekran kararır)
- Uykudan dönüş, Windows ile otomatik başlama

## Yapılacaklar / fikirler
- İleride K55Kontrol ile birleştirme (aynı temel: WPF + .NET 4.8, aynı Tema.xaml)
- OpenRGB'yi sürüm ZIP'ine eklemek (GPL kaynak bildirimiyle)

## Bilinen hatalar
- Yok
