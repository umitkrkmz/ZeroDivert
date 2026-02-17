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
- ZeroDivert bu paketleri parçalayarak veya sırasını değiştirerek DPI'ın SNI'ı okuyamamasını sağlar
- Sunucu tarafında hiçbir değişiklik olmaz, bağlantı normal şekilde kurulur
- Yalnızca Discord trafiğini (`discord.com`, `discordapp.com`, vb.) işler, geri kalan her şeye dokunmaz

### Ne Yapmaz?

- IP adresinizi gizlemez
- Oyunlarda veya genel internet kullanımında hız değişikliğine neden olmaz
- IP tabanlı engellemeleri aşamaz
- Discord dışındaki sitelere müdahale etmez

---

## 📥 İndir

[Releases](../../releases) sayfasından `ZeroDivert-v1.0.0-win-x64.zip` dosyasını indir.

ZIP içinde 3 dosya bulunur — **hepsi aynı klasörde olmalıdır:**

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

---

## 🚀 Kullanım

`ZeroDivert.Console.exe` dosyasına sağ tıklayıp **Yönetici olarak çalıştır**.

```powershell
# Normal kullanım (önerilen)
ZeroDivert.Console.exe

# Detaylı çıktı
ZeroDivert.Console.exe -v

# Log kaydı olmadan
ZeroDivert.Console.exe --no-log
```

İlk çalıştırmada **otomatik kalibrasyon** başlar — tüm bypass teknikleri sırayla denenir, çalışan ayar `profile.json`'a kaydedilir. Sonraki çalıştırmalarda profil doğrudan yüklenir.

### Örnek Çıktı

```
  _____              ____  _                _
 |__  /___ _ __ ___ |  _ \(_)_   _____ _ __| |_
   / // _ \ '__/ _ \| | | | \ \ / / _ \ '__| __|
  / /|  __/ | | (_) | |_| | |\ V /  __/ |  | |_
 /____\___|_|  \___/|____/|_| \_/ \___|_|   \__|

        Discord DPI Bypass Tool v1.0

[+] Yönetici yetkisiyle çalışıyor
[*] Profil: C:\Users\...\AppData\Local\ZeroDivert\profile.json
[*] Kalibrasyon başlıyor...
[+] WinDivert başarıyla başlatıldı!
[*] Discord trafiği izleniyor... Durdurmak için Ctrl+C

[TcpFragmentation] Pkts: 1.2K | Discord: 45 | Modified: 45 | 2.1 MB | 150 pkt/s
```

---

## ⚙️ Bypass Teknikleri

ZeroDivert 5 farklı bypass tekniği içerir. **AdaptiveEngine** bunları otomatik olarak dener ve ISS'inize göre en uygun olanı seçer.

| Teknik | Açıklama |
|--------|----------|
| **TCP Fragmentasyon** | ClientHello'yu küçük TCP segmentlerine böler; DPI parçaları birleştiremez |
| **Akıllı Fragmentasyon** | SNI offset'ini hesaplayarak tam ortadan böler; daha az parça, daha yüksek başarı |
| **TCP Desync** | Paket sırasını bozarak DPI'ın TCP state-machine'ini çökertir |
| **Fake TTL** | Düşük TTL'li sahte paket gönderir; DPI görür ama paket sunucuya ulaşmaz |
| **UDP Fake** | Discord sesli görüşme (UDP) trafiği için sahte paket tekniği uygular |

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
3. `%LOCALAPPDATA%\ZeroDivert\profile.json` dosyasını silin — yeniden kalibrasyon başlatılır
4. `-v` parametresiyle çalıştırıp çıktıyı paylaşın

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
