# ZeroDivert

<div align="center">

![ZeroDivert Logo](https://img.shields.io/badge/ZeroDivert-Discord%20DPI%20Bypass-7289DA?style=for-the-badge&logo=discord&logoColor=white)

**Discord DPI bypass aracı — VPN olmadan, hız kaybı olmadan**

[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](https://www.gnu.org/licenses/gpl-3.0.en.html)
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

[Releases](../../releases) sayfasından son sürümü indirin. İki seçenek var:

| Dosya | Ne zaman? |
|---|---|
| `ZeroDivert-vX.Y.Z-setup.exe` | **Önerilen.** Program Files'a kurulur, Başlat menüsüne eklenir, Program Ekle/Kaldır'dan kaldırılabilir. Kaldırırken otomatik başlatma görevini de temizler. |
| `ZeroDivert-vX.Y.Z-win-x64.zip` | **Taşınabilir.** Kurulum yapmadan, bir klasöre çıkartıp çalıştırmak için. Program dosyaları (`ZeroDivert.Console.exe`, `WinDivert.dll`, `WinDivert64.sys`) aynı klasörde durmalı; yanlarında lisans dosyaları da var. |

**Gereksinimler**
- Windows 10 veya 11, 64-bit
- Yönetici yetkisi (uygulama açılırken ister)
- **.NET kurulumu gerekmez**, runtime uygulamanın içinde gelir.
- Windows 11'de **Akıllı Uygulama Denetimi** açıksa ZeroDivert çalışmaz. Kapatmanızı önermiyoruz; nedenini aşağıda okuyun.

### Akıllı Uygulama Denetimi açıksa

**Akıllı Uygulama Denetimi** (Smart App Control), Windows 11'in bir güvenlik özelliğidir. Bir uygulama açılmadan önce onun dijital olarak imzalı olup olmadığına ve Microsoft'un bulut tabanlı güvenilirlik verilerine bakar; imzasız ya da tanınmayan uygulamaları açılmadan engeller. Antivirüsün henüz tanımadığı yeni zararlı yazılımlara (ör. internetten inen sahte bir kurulum dosyası, fidye yazılımının ilk adımı) karşı ek bir koruma katmanıdır.

ZeroDivert dijital olarak imzalı değildir, bu yüzden bu özellik **açık** olan bilgisayarlarda engellenir. Bu engel için "Yine de çalıştır" seçeneği ya da uygulama bazında istisna yoktur.

- **Durumunu nereden görürüm?** Windows Güvenliği → Uygulama ve tarayıcı denetimi → Akıllı Uygulama Denetimi. "Kapalı" yazıyorsa ZeroDivert'i sorunsuz kullanabilirsiniz. "Değerlendirme" yazıyorsa şimdilik çalışır, ama Windows ileride özelliği kendiliğinden açarsa engellenmeye başlar. Özellik yalnızca temiz kurulmuş Windows 11'lerde bulunur; birçok bilgisayarda zaten kapalıdır.
- **"Açık" yazıyorsa ne yapmalıyım?** Bu ayarı sadece ZeroDivert'i çalıştırmak için kapatmanızı **önermiyoruz**. Karar tamamen size aittir; kapatmadan önce şunları bilin:
  - Kapattığınızda bu koruma katmanı **bütün uygulamalar için** devre dışı kalır, sadece ZeroDivert için değil. Windows Defender antivirüsü çalışmaya devam eder, ama imzasız ve henüz tanınmayan programlar artık bu ek kontrolden geçmeden açılabilir.
  - Kapatılan özellik kendiliğinden tekrar açılmaz. Windows sürümünüze bağlı olarak tekrar açmak için **Windows'u sıfırlamanız ya da yeniden kurmanız gerekebilir**. Kapatmadan önce ayar ekranındaki açıklamayı mutlaka okuyun.
  - Bilgisayar başkasınınsa ya da ailece kullanılıyorsa, bu ayarı sahibine sormadan değiştirmeyin.
- ZeroDivert ücretsiz ve açık kaynaklı bir projedir; ücretli kod imzalama sertifikası kullanılması **planlanmamaktadır**. Uygulamanın ne yaptığından emin olmak isterseniz kaynak kodunu inceleyebilir ve [kaynak koddan](#-kaynak-koddan-derleme) kendiniz derleyebilirsiniz.

**İlk açılışta SmartScreen uyarısı:** İmzasız uygulamalarda Windows ilk açılışta "Windows bilgisayarınızı korudu" diyebilir. **Ek bilgi → Yine de çalıştır** ile devam edebilirsiniz.

**Dosyayı doğrulamak için:** her release'de dosyaların yanında `.sha256` dosyaları var. PowerShell'de `Get-FileHash <dosya> -Algorithm SHA256` çıktısını onunla karşılaştırabilirsiniz.

<details>
<summary>Önceki sürümler (v1.1.0, v1.0.0)</summary>

**v1.1.0** — [.NET 10 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/10.0) kurulu olmalıdır; kurulu değilse uygulama hiçbir mesaj göstermeden açılıp kapanır. ZIP içindeki dosyaların hepsi aynı klasörde olmalıdır:

```
ZeroDivert-v1.1.0-win-x64/
    ├── ZeroDivert.Console.exe
    ├── ZeroDivert.Console.dll
    ├── ZeroDivert.Console.deps.json
    ├── ZeroDivert.Console.runtimeconfig.json
    ├── ZeroDivert.Core.dll
    ├── ZeroDivert.Driver.dll
    ├── Spectre.Console.dll
    ├── Spectre.Console.Ansi.dll
    ├── WinDivert.dll
    └── WinDivert64.sys
```

```
ZeroDivert-v1.1.0-win-x64.zip  SHA-256:
2C2422B1079A1E4418E359291013E9B0F060C8C4575CBE1E1B534392EBF8C14B
```

**v1.0.0** — ZIP içinde 3 dosya bulunur: `ZeroDivert.Console.exe`, `WinDivert.dll`, `WinDivert64.sys`.

```
ZeroDivert-v1.0.0-win-x64.zip  SHA-256:
E2D5D528E5B00F120F3EC12FC174233142441CA4DCCFF3E0BD4AAE1B661312B4
```

</details>

---

## 🚀 Kullanım

Kurulum dosyasıyla kurduysanız Başlat menüsünden **ZeroDivert**'i açın. Taşınabilir zip kullanıyorsanız `ZeroDivert.Console.exe`'yi çalıştırın. Her iki durumda da Windows yönetici izni ister; WinDivert sürücüsü bu izin olmadan çalışamaz.

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
- **Hakkında** — sürüm, lisans, GitHub bağlantısı ve güncelleme durumu
- **Çıkış**

### Güncelleme Kontrolü

ZeroDivert açılırken GitHub'daki son sürüme bir kez bakar. Daha yeni bir sürüm varsa Ana Menü'de ve sistem tepsisinde haber verir; tıklayınca indirme sayfası açılır. İndirme ve kurulumu her zaman siz yaparsınız, uygulama kendini kendiliğinden güncellemez.

Bu kontrol, GitHub API'sine (`api.github.com`) yapılan anonim tek bir istekten ibarettir; sizinle veya bilgisayarınızla ilgili hiçbir bilgi gönderilmez. İsterseniz **Ayarlar → Güncelleme kontrolü** ile kapatabilirsiniz. Bunun dışında ZeroDivert hiçbir sunucuya veri göndermez.

### Sistem Tepsisi ve Otomatik Başlatma

ZeroDivert nasıl başlatılırsa başlatılsın (Ana Menü'den "Başlat" veya doğrudan bir komutla) **her zaman bir sistem tepsisi simgesi** gösterir. Konsol penceresini **X ile kapatmak uygulamayı kapatmaz** — pencere gizlenir, izleme arka planda çalışmaya devam eder ve masaüstünüz/görev çubuğunuz kalabalıklaşmaz. Simgeye sağ tıklayarak **Başlat / Durdur**, **Konsolu Göster/Gizle** ve **Çıkış** yapabilirsiniz. Uygulamayı gerçekten durdurup kapatmak için ya çalışırken **Ctrl+C** basın ya da tepsi menüsünden **Çıkış**'ı seçin.

Aynı anda yalnızca bir ZeroDivert çalışabilir. Uygulama tepsideyken Başlat menüsünden tekrar açarsanız, ikinci bir kopya başlamaz; çalışan kopyanın penceresi öne gelir.

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

Bu derleme, çalıştığı bilgisayarda .NET 10 Desktop Runtime'ın kurulu olmasını bekler. Başka bilgisayarlara dağıtmak için runtime'ı içine gömen **son kullanıcı paketini** kullanın:

```bash
dotnet publish src/ZeroDivert.Console -p:PublishProfile=win-x64
```

Çıktı `src/ZeroDivert.Console/bin/publish/win-x64/` klasörüne düşer: `ZeroDivert.Console.exe` (runtime gömülü), `WinDivert.dll` ve `WinDivert64.sys` aynı klasörde olmalıdır. Yanlarında lisans dosyaları da bulunur: `LICENSE.txt`, `THIRD-PARTY-NOTICES.txt` ve `LICENSE-WinDivert.txt`. Visual Studio'da aynı işlem: `ZeroDivert.Console` → sağ tık → **Publish** → `win-x64`.

**Kurulum dosyası (isteğe bağlı):** [Inno Setup 6](https://jrsoftware.org/isinfo.php) kuruluysa, yukarıdaki publish komutundan sonra:

```powershell
# Inno Setup "herkes için" kurulduysa:
& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" /DAppVersion=1.2.0 installer\ZeroDivert.iss
# Sadece kendi kullanıcınız için kurulduysa (ör. winget --scope user):
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" /DAppVersion=1.2.0 installer\ZeroDivert.iss
```

Kurulum dosyası `installer\Output\` klasörüne düşer.

**Yeni sürüm yayınlamak:** üç csproj'daki `<Version>`, `<AssemblyVersion>` ve `<FileVersion>` değerlerini güncelleyin (uygulama sürümünü banner'da ve Hakkında ekranında buradan okur), CHANGELOG'a sürüm başlığını ekleyin, sonra `v` ile başlayan bir tag pushlayın (`git tag v1.2.0 && git push origin v1.2.0`). [Release iş akışı](.github/workflows/release.yml) tag'in sürümle uyuştuğunu kontrol eder; kurulum dosyasını ve taşınabilir zip'i derler, SHA-256'larını hesaplar ve taslak (draft) bir GitHub Release oluşturur. Notları düzenleyip **Publish**'e basmak size kalır. `v1.2.0-rc.1` gibi ekli tag'ler pre-release olarak açılır.

> `WinDivert.dll` ve `WinDivert64.sys` dosyaları `src/ZeroDivert.Driver/` içinde mevcuttur ve derleme sırasında çıktı klasörüne otomatik kopyalanır. Ayrıca indirmen gerekmez.

---

## ❓ SSS

**Yasal mı?**
Ağ paketlerini düzenleyen açık kaynaklı bir yazılımdır. Kişisel kullanım için uygundur. Kullanım tamamen kullanıcının sorumluluğundadır.

**Antivirüs uyarı veriyor?**
WinDivert bazı antivirüsler tarafından yanlışlıkla "riskli" olarak işaretlenebilir. Bu bir **false positive**'dir. WinDivert açık kaynaklıdır, kaynak kodu [GitHub'da](https://github.com/basil00/WinDivert) incelenebilir. Kaspersky'nin WinDivert'i engellediği bilinmektedir; Windows Defender'a geçmeniz önerilir.

**"Akıllı Uygulama Denetimi, güvenli olmayabilecek bir uygulamayı engelledi" uyarısı çıkıyor?**
Bu bir virüs tespiti değildir. Windows 11'deki **Akıllı Uygulama Denetimi** imzasız uygulamaları engeller ve ZeroDivert imzasız dağıtılır. Ne olduğunu, kapatmanın sonuçlarını ve neden önermediğimizi [İndir bölümünde](#akıllı-uygulama-denetimi-açıksa) anlattık.

Bir not: Bu özellik ilk kurulumdan sonra bir süre **"Değerlendirme"** modunda çalışır ve hiçbir şeyi engellemez, sonra Windows onu kendiliğinden **"Açık"** ya da **"Kapalı"** konuma geçirir. Bu yüzden daha önce sorunsuz açılan bir sürüm bir gün aniden engellenmeye başlayabilir.

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

Copyright (C) 2025-2026 Umit Korkmaz

Bu proje **GNU General Public License v3.0 veya sonraki bir sürümü** (`GPL-3.0-or-later`) ile lisanslanmıştır: Free Software Foundation tarafından yayımlanan GPL'in 3. sürümü ya da (tercihinize bağlı olarak) daha sonraki herhangi bir sürümü kapsamında yeniden dağıtabilir ve/veya değiştirebilirsiniz. Bu program faydalı olması umuduyla dağıtılmaktadır, ancak **hiçbir garanti verilmez**; ayrıntılar için lisans metnine bakın.

> ZeroDivert, [WinDivert](https://github.com/basil00/WinDivert) kullanmaktadır. WinDivert, LGPL v3 / GPL v2 çift lisanslıdır; ZeroDivert onu **LGPL v3** koşulları altında kullanır (LGPL v3, GPL v3 ile uyumludur).

> **Önceki sürümler:** v1.0.0 ve v1.1.0, **GPL v2** ile yayımlanmıştır ve o sürümlerin kopyaları GPL v2 altında kalmaya devam eder. GPL v3'e geçiş bir sonraki sürümden itibaren geçerlidir.

Tam lisans metni için [LICENSE](LICENSE) dosyasına bakın. ZeroDivert'in birlikte dağıttığı üçüncü taraf bileşenler (WinDivert, Spectre.Console, .NET runtime) ve lisansları [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) dosyasında listelenmiştir. Bu dosyalar indirdiğiniz paketin içinde de bulunur.

---

## ⚠️ Yasal Uyarı

> Bu yazılım eğitim ve araştırma amaçlı geliştirilmiştir. Kullanımdan doğan her türlü yasal sorumluluk kullanıcıya aittir.

---

<div align="center">

**⭐ Beğendiyseniz yıldız vermeyi unutmayın!**

*Geliştirici: [Umit Korkmaz](https://github.com/umitkrkmz)*

</div>
