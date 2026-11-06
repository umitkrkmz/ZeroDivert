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
- Bazı İSS'ler Discord'u **DNS seviyesinde de** engeller (çoğu zaman modemin IPv6 DNS'i üzerinden). ZeroDivert çalışırken bilgisayardaki bütün uygulamaların DNS sorgularını (IPv4 ve IPv6) şifreli **DNS-over-HTTPS (DoH)** ile cevaplar; Windows'un DNS ayarlarına dokunmaz. Bu **DNS koruması** varsayılan olarak açıktır
- Sunucu tarafında hiçbir değişiklik olmaz, bağlantı normal şekilde kurulur
- Bypass teknikleri yalnızca Discord bağlantılarına (`discord.com`, `discordapp.com`, vb.) uygulanır; diğer bağlantılara dokunulmaz

### Ne Yapmaz?

- IP adresinizi gizlemez
- Oyunlarda veya genel internet kullanımında hız değişikliğine neden olmaz
- IP tabanlı engellemeleri aşamaz
- Discord dışındaki sitelerin bağlantılarını değiştirmez (DNS koruması açıkken yalnızca DNS sorguları şifreli olarak cevaplanır)
- UDP trafiğine (sesli görüşme, oyunlar) dokunmaz

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
- **Ayarlar** — çalışma seçenekleri (`-v`/`--no-log`/`--no-status`), **Bypass tekniği** (otomatik ya da elle seçilen bir teknik), **DNS koruması (DoH)** aç/kapa, **DNS Ayarları** (Windows'un DNS sunucusunu Cloudflare/Google/özel bir sunucuya çevirme), **Başlangıçta Otomatik Başlat** ve **Güncelleme kontrolü**
- **Log ve Profil Bilgisi** — log/profil dosyalarının yeri ve kayıtlı profilin içeriği (salt okunur)
- **Hakkında** — sürüm, lisans, GitHub bağlantısı ve güncelleme durumu
- **Çıkış**

### Güncelleme Kontrolü

ZeroDivert açılırken GitHub'daki son sürüme bir kez bakar. Daha yeni bir sürüm varsa Ana Menü'de ve sistem tepsisinde haber verir; tıklayınca indirme sayfası açılır. İndirme ve kurulumu her zaman siz yaparsınız, uygulama kendini kendiliğinden güncellemez.

Bu kontrol, GitHub API'sine (`api.github.com`) yapılan anonim tek bir istekten ibarettir; sizinle veya bilgisayarınızla ilgili hiçbir bilgi gönderilmez. İsterseniz **Ayarlar → Güncelleme kontrolü** ile kapatabilirsiniz.

### DNS Koruması (DoH)

ZeroDivert çalışırken, bilgisayardaki bütün uygulamaların düz DNS sorgularını (UDP 53, IPv4 ve IPv6) yakalar ve Cloudflare'in şifreli DNS sunucusuna (`https://1.1.1.1/dns-query`) **DNS-over-HTTPS** ile sorar; cevabı uygulamaya, sorduğu DNS sunucusundan gelmiş gibi iletir. Böylece İSS'nin DNS'i değiştirmesi (ör. `discord.com` için engel sayfasının adresini döndürmesi) etkisiz kalır.

- Windows'un DNS ayarları değişmez; ZeroDivert durunca her şey eski haline döner.
- Modemin IPv6 DNS'i gibi, Windows'taki "şifreli DNS" ayarının kapsamadığı yollar da korunur.
- DoH sunucusuna ulaşılamazsa sorgu değiştirilmeden normal yolundan gönderilir; DNS çalışmaya devam eder (korumasız olarak).
- Canlı panelin **DNS (DoH / düz)** sütunu, DoH ile cevaplanan ve düz DNS'e dönen sorgu sayısını gösterir.
- **Gizlilik:** DNS koruması açıkken DNS sorgularınız (ziyaret ettiğiniz alan adları) Cloudflare'e gider. İstemezseniz **Ayarlar → DNS koruması (DoH)** ile kapatabilirsiniz. Güncelleme kontrolü ve DNS koruması dışında ZeroDivert hiçbir sunucuya veri göndermez.

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

        ── Discord DPI Bypass Tool v1.3.0 ──

Ana Menü (ok tuşları ile gezin, Enter ile seçin)
> ▶  Başlat (Otomatik - önerilen)
  ⚙  Ayarlar
  ℹ  Log ve Profil Bilgisi
  ?  Hakkında
  ✕  Çıkış
```

"Başlat" seçildikten sonra canlı durum paneli:

```
╭──────────────────┬───────┬─────────┬──────────────┬────────┬───────────┬─────────────────╮
│ Teknik           │ Paket │ Discord │ Değiştirilen │ Veri   │ Hız       │ DNS (DoH / düz) │
├──────────────────┼───────┼─────────┼──────────────┼────────┼───────────┼─────────────────┤
│ TcpFragmentation │ 1.2K  │ 45      │ 45           │ 2.1 MB │ 150 pkt/s │ 312 / 0         │
╰──────────────────┴───────┴─────────┴──────────────┴────────┴───────────┴─────────────────╯
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

Teknikler yalnızca Discord'a giden TLS bağlantılarının ilk paketine (ClientHello) uygulanır. **Otomatik** modda ZeroDivert aşağıdaki sırayla dener:

| Sıra | Teknik | Ne yapar? |
|------|--------|-----------|
| 1 | **TCP Fragmentasyon** | ClientHello'nun ilk 50 baytını (TLS kayıt ve el sıkışma başlıkları) 2 baytlık TCP parçalarına böler, gerisini tek parça gönderir; parçaları birleştirmeyen DPI paketi ClientHello olarak tanıyamaz ve SNI'a bakamaz |
| 2 | **TCP Desync** | ClientHello'yu ilk birkaç bayttan ikiye böler |
| 3 | **Fake TTL** | Gerçek ClientHello'dan önce, TTL'i 5 olan sahte bir ClientHello gönderir: İSS'nin DPI cihazına ulaşır ama sunucuya ulaşmadan ölür |
| 4 | **GoodbyeDPI uyumlu** | GoodbyeDPI-Turkey'nin Türkiye'deki ayarının (`-5 --set-ttl 5`) aynısı: önce TTL 5'li sahte ClientHello, sonra gerçek ClientHello ilk 2 bayttan ikiye bölünüp **ters sırada** gönderilir. GoodbyeDPI bunu bütün sitelere uygular; ZeroDivert yalnızca Discord'a |

**Hangisinin çalıştığını nasıl anlar?** ClientHello'dan sonra bağlantı ilerliyorsa (sunucu cevap vermiş demektir) başarılı; DPI bağlantıyı RST ile keserse ya da 6 saniye içinde hiçbir ilerleme olmazsa (sessiz engelleme) başarısız sayılır. Art arda 3 başarısızlıkta sıradaki tekniğe geçilir; 5 ardışık başarıdan sonra çalışan teknik `profile.json`'a kaydedilir ve sonraki açılışlarda doğrudan kullanılır.

**Elle seçmek:** **Ayarlar → Bypass tekniği**'nden belirli bir tekniği seçerseniz otomatik geçiş yapılmaz. Farklı teknikleri kendi hattınızda karşılaştırmak için kullanışlıdır.

> Türk Telekom'da yaşanan vakada asıl sorunun DNS olduğu görüldü: DNS koruması açıkken ilk teknik (TCP Fragmentasyon) Discord'u açmaya yetti. GoodbyeDPI uyumlu tekniğin sıradaki yeri, farklı hatlarda karşılaştırmalı testlerle yeniden değerlendirilecek.

---

## 📁 Kaynak Koddan Derleme

**Gereksinimler:** Windows 10/11 64-bit, [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), Yönetici yetkisi

```bash
git clone https://github.com/umitkrkmz/ZeroDivert.git
cd ZeroDivert
dotnet build -c Release
```

Testleri çalıştırmak için (yönetici yetkisi gerekmez; sürücü yüklenmez):
```bash
dotnet test tests/ZeroDivert.Tests
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
& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" /DAppVersion=1.3.0 installer\ZeroDivert.iss
# Sadece kendi kullanıcınız için kurulduysa (ör. winget --scope user):
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" /DAppVersion=1.3.0 installer\ZeroDivert.iss
```

Kurulum dosyası `installer\Output\` klasörüne düşer.

**Yeni sürüm yayınlamak:** üç csproj'daki `<Version>`, `<AssemblyVersion>` ve `<FileVersion>` değerlerini güncelleyin (uygulama sürümünü banner'da ve Hakkında ekranında buradan okur), CHANGELOG'a sürüm başlığını ekleyin, sonra `v` ile başlayan bir tag pushlayın (`git tag v1.3.0 && git push origin v1.3.0`). [Release iş akışı](.github/workflows/release.yml) tag'in sürümle uyuştuğunu kontrol eder; kurulum dosyasını ve taşınabilir zip'i derler, SHA-256'larını hesaplar ve taslak (draft) bir GitHub Release oluşturur. Notları düzenleyip **Publish**'e basmak size kalır. `v1.3.0-rc.1` gibi ekli tag'ler pre-release olarak açılır.

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
4. **Ayarlar → DNS koruması (DoH)**'nun açık olduğundan emin olun — bkz. aşağıdaki madde
5. **Ayarlar → Bypass tekniği**'nden teknikleri tek tek deneyin
6. `-v` parametresiyle çalıştırıp çıktıyı paylaşın

**ZeroDivert çalışıyor ama Discord "Checking for updates" / "Update failed" ekranında takılı kalıyor?**
Bazı İSS'ler Discord'u yalnızca TLS/SNI seviyesinde değil, **DNS seviyesinde de** engelliyor: `discord.com` için gerçek adres yerine engel sayfasının adresi döner (Türk Telekom'da `195.175.254.2`). Bu durumda SNI'ı gizlemek tek başına yetmez, çünkü bağlantı zaten yanlış adrese gidiyordur.

**DNS koruması (DoH)** bunun için var ve varsayılan olarak açık (bkz. [DNS Koruması](#dns-koruması-doh)). ZeroDivert çalışırken `nslookup discord.com` ile kontrol edebilirsiniz: engel sayfası yerine gerçek bir adres dönmeli.

Windows'un kendi DNS ayarını değiştirmek de bir seçenek (**Ayarlar → DNS Ayarları**), ama iki tuzak var ve DNS koruması ikisinden de etkilenmez:
- **IPv6:** Windows'ta yalnızca IPv4 için `1.1.1.1 (Şifreli)` ayarlamak yetmez; sorgular modemin IPv6 DNS'ine (`fe80::...`) gidip yine engel sayfasını alabilir. Uygulama içindeki DNS Ayarları bu yüzden IPv4 ve IPv6'yı birlikte ayarlar ve Windows 11'de şifrelemeyi açar.
- **"Tüm Wi-Fi ağlarının DNS ayarları":** Windows 11'de bu ayar tanımlıysa, ağ başına yaptığınız DNS ayarından önce o kullanılır. DNS Ayarları ekranı, değişiklikten sonra Windows'un gerçekte ne kullandığını test edip gösterir.

Ayrıca bazı hatlarda `1.1.1.1:53`'e giden **düz** DNS de engelli; şifrelemesiz bir DNS ayarı orada DNS'i tamamen durdurabilir (Windows 10'da şifreli DNS yoktur).

**Limbo ile aynı anda çalıştırabilir miyim?**
Hayır. İkisi de Discord'un TLS el sıkışmasını değiştirir; aynı anda çalışırlarsa Discord bağlantıları bozulabilir ve kalibrasyon sonuçları karışır. Birini ya da diğerini kullanın. Limbo, ZeroDivert'in çalıştığını görünce uyarı verir. Aynı şey GoodbyeDPI için de geçerlidir.

**Hangi Windows sürümlerinde çalışır?**
Windows 10 ve Windows 11 (64-bit). 32-bit sistemler desteklenmez.

**Neden VPN kullanmıyorum?**
VPN tüm trafiğinizi yavaşlatır, oyunda ping artar ve ücretsiz VPN'ler güvenlik riski oluşturur. ZeroDivert sadece Discord trafiğini işler, diğer hiçbir şeye dokunmaz.

---

## 🔗 Limbo ile İlişki

ZeroDivert, kardeş proje **[Limbo](https://github.com/umitkrkmz/Limbo)**'nun Discord'a odaklanmış küçük kardeşidir. İkisi aynı yazarın, ikisi de GPL-3.0-or-later lisanslı ve birbirinden kod alıp verirler (ör. ZeroDivert'in DNS koruması Limbo'dan taşındı).

Limbo aynı yaklaşımı bütün sitelere genişletir:
- **Alan adı başına profiller:** her site için ayrı teknik
- **Radar:** İSS'nin engellediği alan adlarını kendisi fark edip profil oluşturur
- **GoodbyeDPI tarzı genel mod:** profili olmayan bütün HTTPS/HTTP bağlantılarına uygulanır
- **DNS koruması:** bütün uygulamaların DNS sorgularını (IPv4 + IPv6) DoH ile cevaplar
- **Ağ analizi:** İSS'yi tanır; DNS değiştirme, RST enjeksiyonu ve sessiz engellemeyi raporlar
- **Etkileşimli + betiklenebilir konsol** (GUI yok) ve kurulum dosyası

Sadece Discord'u açmak istiyorsanız ZeroDivert yeterli; daha fazlasına ihtiyaç duyarsanız **[Limbo'ya göz atın →](https://github.com/umitkrkmz/Limbo)**. İkisini aynı anda çalıştırmayın (bkz. SSS).

---

## 🤝 Katkıda Bulunun

Pull request'ler memnuniyetle karşılanır. Detaylar için [CONTRIBUTING.md](CONTRIBUTING.md) dosyasına bakın.

- **Soru ve fikirler** → [GitHub Discussions](https://github.com/umitkrkmz/ZeroDivert/discussions)
- **Hata bildirimi** → [GitHub Issues](https://github.com/umitkrkmz/ZeroDivert/issues)

---

## 🙏 Teşekkürler

- **[basil00/WinDivert](https://github.com/basil00/WinDivert)** — Windows kernel seviyesi paket yakalama kütüphanesi. ZeroDivert'in temel bağımlılığıdır. (LGPL v3 / GPL v2)
- **[ValdikSS/GoodbyeDPI](https://github.com/ValdikSS/GoodbyeDPI)** — DPI bypass tekniklerinin öncüsü. "GoodbyeDPI uyumlu" teknik onun kaynak koduna göre yazıldı ve sahte ClientHello'su GoodbyeDPI'ınkinin aynısıdır (Apache-2.0, bkz. `THIRD-PARTY-NOTICES.txt`).
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
