# ZeroDivert

<div align="center">

![ZeroDivert Logo](https://img.shields.io/badge/ZeroDivert-Discord%20DPI%20Bypass-7289DA?style=for-the-badge&logo=discord&logoColor=white)

**Discord DPI bypass aracı — VPN olmadan, hız kaybı olmadan**

[![License: GPL v2](https://img.shields.io/badge/License-GPL%20v2-blue.svg)](https://www.gnu.org/licenses/old-licenses/gpl-2.0.en.html)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Windows](https://img.shields.io/badge/Windows-10%2F11-0078D6?logo=windows)](https://www.microsoft.com/windows)

</div>

---

## Nedir?

ZeroDivert, İSS'lerin (İnternet Servis Sağlayıcıları) uyguladığı DPI (Deep Packet Inspection) engellerini atlatarak Discord'a erişim sağlayan açık kaynaklı bir araçtır.

**VPN değildir.** İnternet hızınızı etkilemez, trafiğinizi başka bir sunucudan yönlendirmez. Yalnızca Discord'a giden TLS bağlantı paketlerini düzenleyerek DPI cihazının SNI alanını okuyamamasını sağlar.

### Ne Yapar?

- İSS DPI cihazları TLS bağlantılarındaki **SNI** (Server Name Indication) alanını okuyarak Discord'u tespit eder ve bağlantıyı keser
- ZeroDivert bu paketleri parçalayarak veya sırasını değiştirerek DPI'ın SNI'ı okuyamamasını sağlar; bir teknik işe yaramazsa (RST veya sessiz paket düşürme tespit edilirse) otomatik olarak bir sonrakini dener
- Bazı İSS'ler Discord'u DNS seviyesinde de engeller — ZeroDivert bunun için de uygulama içinden (Ana Menü → `Ayarlar → DNS Ayarları`) DNS sunucunuzu değiştirmenizi sağlar
- Sunucu tarafında hiçbir değişiklik olmaz, bağlantı normal şekilde kurulur
- Yalnızca Discord trafiğini (`discord.com`, `discordapp.com`, vb.) işler, geri kalan her şeye dokunmaz

### Ne Yapmaz?

- IP adresinizi gizlemez
- Oyunlarda veya genel internet kullanımında hız değişikliğine neden olmaz
- IP tabanlı engellemeleri aşamaz
- Discord dışındaki sitelere müdahale etmez

---

## 📥 İndir

[Releases](../../releases) sayfasından `ZeroDivert-v1.1.0-win-x64.zip` dosyasını indir.

ZIP içinde aşağıdaki dosyalar bulunur — **hepsi aynı klasörde olmalıdır:**

```
ZeroDivert-v1.1.0-win-x64/
    ├── ZeroDivert.Console.exe            ← Ana uygulama
    ├── ZeroDivert.Console.dll
    ├── ZeroDivert.Console.deps.json
    ├── ZeroDivert.Console.runtimeconfig.json
    ├── ZeroDivert.Core.dll
    ├── ZeroDivert.Driver.dll
    ├── Spectre.Console.dll               ← Konsol arayüzü
    ├── Spectre.Console.Ansi.dll
    ├── WinDivert.dll                     ← Ağ kütüphanesi
    └── WinDivert64.sys                   ← Kernel driver
```

**SHA-256:**
```
ZeroDivert-v1.1.0-win-x64.zip:
2C2422B1079A1E4418E359291013E9B0F060C8C4575CBE1E1B534392EBF8C14B
```

<details>
<summary>Önceki sürüm (v1.0.0)</summary>

ZIP içinde 3 dosya bulunur:

```
ZeroDivert-v1.0.0-win-x64/
    ├── ZeroDivert.Console.exe   ← Ana uygulama
    ├── WinDivert.dll            ← Ağ kütüphanesi
    └── WinDivert64.sys          ← Kernel driver
```

**SHA-256:**
```
ZeroDivert-v1.0.0-win-x64.zip:
E2D5D528E5B00F120F3EC12FC174233142441CA4DCCFF3E0BD4AAE1B661312B4
```

</details>

---

## 🚀 Kullanım

`ZeroDivert.Console.exe` dosyasına sağ tıklayıp **Yönetici olarak çalıştır**.

Argüman vermeden çalıştırıp bir terminalde açarsan **Ana Menü** açılır. Argüman geçersen (ör. bir script'ten) menü atlanır ve doğrudan o argümanlarla çalışır:

```powershell
# Normal kullanım (Ana Menü açılır)
ZeroDivert.Console.exe

# Detaylı çıktı
ZeroDivert.Console.exe -v

# Log kaydı olmadan
ZeroDivert.Console.exe --no-log

# Canlı durum panelini kapat
ZeroDivert.Console.exe --no-status

# Arka planda, tepsi ikonuyla başlat (menü göstermez, bkz. aşağıdaki bölüm)
ZeroDivert.Console.exe --tray
```

İlk çalıştırmada **otomatik kalibrasyon** başlar — tüm bypass teknikleri sırayla denenir, çalışan ayar `profile.json`'a kaydedilir. Sonraki çalıştırmalarda profil doğrudan yüklenir.

Argümansız çalıştırınca açılan **Ana Menü**'den:
- **Başlat** — izlemeyi başlatır
- **Ayarlar** — çalışma seçenekleri (`-v`/`--no-log`/`--no-status`), **DNS Ayarları** (ağ adaptörünüzün DNS'ini uygulama içinden Cloudflare/Google/özel bir sunucuya çevirin — bazı İSS'ler Discord'u DNS seviyesinde de engellediği için gerekebilir, bkz. SSS) ve **Başlangıçta Otomatik Başlat**
- **Log ve Profil Bilgisi** — log/profil dosyalarının yeri ve kayıtlı profilin içeriği (salt okunur)
- **Çıkış**

### Sistem Tepsisi ve Otomatik Başlatma

ZeroDivert nasıl başlatılırsa başlatılsın (Ana Menü'den "Başlat" veya doğrudan bir komutla) **her zaman bir sistem tepsisi simgesi** gösterir. Konsol penceresini **X ile kapatmak uygulamayı kapatmaz** — pencere gizlenir, izleme arka planda çalışmaya devam eder ve masaüstünüz/görev çubuğunuz kalabalıklaşmaz. Simgeye sağ tıklayarak **Başlat / Durdur**, **Konsolu Göster/Gizle** ve **Çıkış** yapabilirsiniz. Uygulamayı gerçekten durdurup kapatmak için ya çalışırken **Ctrl+C** basın ya da tepsi menüsünden **Çıkış**'ı seçin.

`Ayarlar → Başlangıçta Otomatik Başlat → Kur` seçildiğinde, Windows'a giriş yaptığınızda ZeroDivert konsol penceresi açmadan doğrudan tepsi simgesiyle arka planda otomatik başlar (Görev Zamanlayıcı üzerinden, yükseltilmiş yetkiyle — her girişte UAC istemi çıkmaz) ve en son bıraktığınız durumu (çalışıyor/durduruldu) hatırlayıp aynı duruma döner.

> Bu gerçek bir Windows Service değildir — servisler (Session 0) sistem tepsisinde simge gösteremediği için bilinçli olarak bu yöntem tercih edildi. Kaldırmak için aynı menüden **Kaldır**'ı seçin.

### Örnek Çıktı

Açılış banner'ı ve Ana Menü:

```
  _____              ____  _                _
 |__  /___ _ __ ___ |  _ \(_)_   _____ _ __| |_
   / // _ \ '__/ _ \| | | | \ \ / / _ \ '__| __|
  / /|  __/ | | (_) | |_| | |\ V /  __/ |  | |_
 /____\___|_|  \___/|____/|_| \_/ \___|_|   \__|

        ── Discord DPI Bypass Tool v1.1.0 ──

Ana Menü (ok tuşları ile gezin, Enter ile seçin)
> ▶  Başlat (Otomatik - önerilen)
  ⚙  Ayarlar
  ℹ  Log ve Profil Bilgisi
  ✕  Çıkış
```

"Başlat" seçildikten sonra canlı durum paneli:

```
╭────────────────┬────────┬─────────┬──────────────┬─────────┬─────────╮
│ Teknik          │ Paket  │ Discord │ Değiştirilen │ Veri    │ Hız     │
├────────────────┼────────┼─────────┼──────────────┼─────────┼─────────┤
│ TcpFragmentation│ 1.2K   │ 45      │ 45           │ 2.1 MB  │ 150 pkt/s│
╰────────────────┴────────┴─────────┴──────────────┴─────────┴─────────╯
╭ Durum ─────────────────────────────────────────────────────────────╮
│ TLS -> gateway.discord.gg                                          │
╰──────────────────────────────────────────────────────────────────╯
╭ Son olaylar ───────────────────────────────────────────────────────╮
│ 21:40:47 [+] WinDivert initialized successfully!                   │
│ 21:40:47 [*] Monitoring Discord traffic... Press Ctrl+C to stop.   │
╰──────────────────────────────────────────────────────────────────╯
```

---

## ⚙️ Bypass Teknikleri

**AdaptiveEngine**, aşağıdaki 4 TCP tekniğini sırayla dener; bir teknik art arda başarısız olursa (bağlantı RST ile kesilirse ya da hiç yanıt gelmezse) bir sonrakine geçer, 5 ardışık başarıdan sonra o teknikte "kalibrasyonu tamamlanmış" sayıp `profile.json`'a kaydeder:

| Sıra | Teknik | Açıklama |
|------|--------|----------|
| 1 | **TCP Fragmentasyon (Akıllı)** | SNI'ın bulunduğu ilk ~50 baytı 2 baytlık küçük TCP segmentlerine böler; DPI parçaları birleştiremez |
| 2 | **TCP Desync** | Paket sırasını bozarak DPI'ın TCP state-machine'ini çökertir |
| 3 | **Fake TTL** | Düşük TTL'li sahte paket gönderir; DPI görür ama paket sunucuya ulaşmaz |
| 4 | **Kombine** | Yukarıdaki fragmentasyon + desync tekniklerini birlikte uygular |

Ayrıca hangi TCP tekniği aktifse aktif olsun, Discord'un sesli görüşme trafiği (UDP) için **UDP Fake** tekniği her zaman ek olarak uygulanır.

---

## 📁 Kaynak Koddan Derleme

**Gereksinimler:** Windows 10/11 64-bit, [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), Yönetici yetkisi

```bash
git clone https://github.com/umitkrkmz/ZeroDivert.git
cd ZeroDivert
dotnet build -c Release
```

Yönetici olarak çalıştır:
```
.\src\ZeroDivert.Console\bin\Release\net10.0-windows\ZeroDivert.Console.exe
```

Bu klasörü başka bir yere dağıtacaksanız (ör. ZIP olarak), aşağıdaki dosyaların **hepsi aynı klasörde** olmalı:

```
ZeroDivert.Console.exe
ZeroDivert.Console.dll
ZeroDivert.Console.deps.json
ZeroDivert.Console.runtimeconfig.json
ZeroDivert.Core.dll
ZeroDivert.Driver.dll
Spectre.Console.dll
Spectre.Console.Ansi.dll
WinDivert.dll
WinDivert64.sys
```

> `WinDivert.dll` ve `WinDivert64.sys` dosyaları `src/ZeroDivert.Driver/` içinde mevcuttur ve derleme sırasında çıktı klasörüne otomatik kopyalanır. Ayrıca indirmen gerekmez.

---

## ❓ SSS

**Yasal mı?**
Ağ paketlerini düzenleyen açık kaynaklı bir yazılımdır. Kişisel kullanım için uygundur. Kullanım tamamen kullanıcının sorumluluğundadır.

**Antivirüs uyarı veriyor?**
WinDivert bazı antivirüsler tarafından yanlışlıkla "riskli" olarak işaretlenebilir. Bu bir **false positive**'dir. WinDivert açık kaynaklıdır, kaynak kodu [GitHub'da](https://github.com/basil00/WinDivert) incelenebilir. Kaspersky'nin WinDivert'i engellediği bilinmektedir; Windows Defender'a geçmeniz önerilir.

**Çalışmıyor, ne yapmalıyım?**
1. **Yönetici olarak çalıştırdığınızdan emin olun**
2. Antivirüsünüzde ZeroDivert klasörünü istisna olarak ekleyin
3. Ana Menü'den **Log ve Profil Bilgisi** ekranından profilinizin durumuna bakın; gerekirse `%LOCALAPPDATA%\ZeroDivert\profile.json` dosyasını silin (ya da bu ekrandan not edip elle silin) — yeniden kalibrasyon başlatılır
4. DNS sunucunuzu şifreli bir sağlayıcıya çevirin — bkz. aşağıdaki madde
5. `-v` parametresiyle çalıştırıp çıktıyı paylaşın

**ZeroDivert çalışıyor ama Discord "Checking for updates" / "Update failed" ekranında takılı kalıyor?**
Bazı İSS'ler Discord'u yalnızca TLS/SNI seviyesinde değil, **DNS seviyesinde de** engelliyor (DNS sorgusu zehirleniyor veya yanlış/erişilemez bir IP döndürülüyor). Bu durumda ZeroDivert'in SNI gizleme tekniği tek başına yetmez, çünkü bağlantı zaten yanlış adrese gitmeye çalışıyordur. Çözüm: DNS sunucunuzu şifreli bir genel DNS'e (ör. Cloudflare `1.1.1.1` / `1.0.0.1`, Google `8.8.8.8` / `8.8.4.4`) çevirin. Bunu yapmanın iki yolu var:

**1. Uygulama içinden (önerilen):** Ana Menü'den **Ayarlar → DNS Ayarları**'na girin, adaptörünüzü seçin, Cloudflare veya Google'ı işaretleyin — ZeroDivert bunu sizin için `netsh` ile uygular, PowerShell'e gerek kalmaz.

**2. Elle (PowerShell):**

```powershell
# Yönetici PowerShell'de, aktif ağ adaptörünüzün adını Get-NetAdapter ile bulun
Set-DnsClientServerAddress -InterfaceAlias "Ethernet" -ServerAddresses ("1.1.1.1","1.0.0.1")
ipconfig /flushdns
```

Windows 11'de bu DNS sunucuları otomatik olarak şifreli (DNS-over-HTTPS) modda çalışır, bu da İSS'nin DNS sorgunuzu görüp müdahale etmesini engeller. DNS engeli ile SNI engeli genelde birlikte uygulandığından, kalıcı çözüm için **hem DNS değişikliği hem de ZeroDivert'in aynı anda çalışması** gerekir — biri tek başına yeterli olmayabilir.

**Hangi Windows sürümlerinde çalışır?**
Windows 10 ve Windows 11 (64-bit). 32-bit sistemler desteklenmez.

**Neden VPN kullanmıyorum?**
VPN tüm trafiğinizi yavaşlatır, oyunda ping artar ve ücretsiz VPN'ler güvenlik riski oluşturur. ZeroDivert sadece Discord trafiğini işler, diğer hiçbir şeye dokunmaz.

---

## 🔗 Limbo ile İlişki

ZeroDivert, **[Limbo](https://github.com/umitkrkmz/Limbo)** projesinin ilk versiyonu olarak geliştirilen bir proof-of-concept araçtır.

Limbo, ZeroDivert'in Discord odaklı yaklaşımını çok daha geniş bir platforma taşır:
- Discord yerine **tüm domainler** için per-domain profil sistemi
- **Radar sistemi** — ISP engeli olan domainleri otomatik tespit eder
- **ECH** (Encrypted Client Hello) — tam X25519 HPKE şifrelemesi
- **DNS over HTTPS** — Cloudflare, Google, Quad9 desteği
- **GUI** (WPF, Dark/Light tema) + kapsamlı CLI
- ISP davranış analizi, gerçek zamanlı bant genişliği izleme

ZeroDivert'i Discord için kullanıyorsanız ve daha fazlasına ihtiyaç duyuyorsanız **[Limbo'ya göz atın →](https://github.com/umitkrkmz/Limbo)**

---

## 🤝 Katkıda Bulunun

Pull request'ler memnuniyetle karşılanır. Detaylar için [CONTRIBUTING.md](CONTRIBUTING.md) dosyasına bakın.

- **Soru ve fikirler** → [GitHub Discussions](https://github.com/umitkrkmz/ZeroDivert/discussions)
- **Hata bildirimi** → [GitHub Issues](https://github.com/umitkrkmz/ZeroDivert/issues)

---

## 🙏 Teşekkürler

- **[basil00/WinDivert](https://github.com/basil00/WinDivert)** — Windows kernel seviyesi paket yakalama kütüphanesi. ZeroDivert'in temel bağımlılığıdır. (LGPL v3 / GPL v2)
- **[ValdikSS/GoodbyeDPI](https://github.com/ValdikSS/GoodbyeDPI)** — DPI bypass tekniklerinin öncüsü. TCP fragmentasyon ve fake paket yöntemlerinin ilham kaynağı.
- **[cagritaskn/GoodbyeDPI-Turkey](https://github.com/cagritaskn/GoodbyeDPI-Turkey)** — GoodbyeDPI'ın Türkiye ISS'lerine uyarlanmış versiyonu.
- **[bol-van/zapret](https://github.com/bol-van/zapret)** — En kapsamlı DPI atlatma aracı.

---

## 📄 Lisans

Bu proje **GNU General Public License v2.0** ile lisanslanmıştır.

> ZeroDivert, [WinDivert](https://github.com/basil00/WinDivert) kullanmaktadır (LGPL v3 / GPL v2 dual-license). Lisans uyumluluğu için GPL v2 seçilmiştir.

Tam lisans metni için [LICENSE](LICENSE) dosyasına bakın.

---

## ⚠️ Yasal Uyarı

> Bu yazılım eğitim ve araştırma amaçlı geliştirilmiştir. Kullanımdan doğan her türlü yasal sorumluluk kullanıcıya aittir.

---

<div align="center">

**⭐ Beğendiyseniz yıldız vermeyi unutmayın!**

*Geliştirici: [Umit Korkmaz](https://github.com/umitkrkmz)*

</div>
