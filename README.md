# ZapretWinTurkiye

Bu program, Türk kullanıcılar için DPI (Deep Packet Inspection / Derin Paket İncelemesi) tabanlı internet sansürlerini ve kısıtlamalarını atlatmak amacıyla geliştirilmiş olan [zapret-win-bundle](https://github.com/bol-van/zapret-win-bundle) projesinin kullanımını kolaylaştırmayı amaçlar.

## Programın Özellikleri

- **DNS Kontrolü:** ISS tarafından DNS'inize müdahale ediliyorsa tespiti yapılır. DNS'i sağlama almak için YogaDNS kurmanızı ve Google DoH ayarlamanızı öneririm.
- **Dnscrypt-proxy servisi:** YogaDNS kullanmanızı daha çok tavsiye etsem de Dnscrypt-proxy kullanarak da şifreli DNS servisi kullanabilirsiniz. Her şey tek programda olsun diyenler tercih edebilir.
- **Blockcheck:** ISS'niz için çalışan stratejiyi bulmak için blockcheck yapabilirsiniz.
- **Hazır Stratejiler:** Bulabildiğim bazı hazır stratejileri programa ekledim, blockcheck yapmaya gerek kalmadan deneyebilirsiniz.
- **Çoklu Motor Desteği (Multi-Engine):** Hem eski klasik Zapret motorunu hem de yeni nesil LUA tabanlı gelişmiş Zapret2 motorunu entegre olarak barındırır.
- **Manuel Kullanım:** "Zapret'i Başlat" seçeneği ile program içinden anlık kullanım sağlar. Program kapatıldığında tüm süreçler temizlenir.
- **Servis Desteği:** "Servis Olarak Yükle" butonu ile Windows Servisi olarak kurma imkanı sunar. Bilgisayar her açıldığında otomatik başlar. Bu programın açılmasına gerek kalmaz.
- **Hostlist Desteği:** Sadece sansürlü siteleri listeye ekleyerek (otomatik veya manuel) filtreleme yapar; normal internet trafiğinizi kesinlikle yormaz.
- **Excludelist Desteği:** Zapret'in aktif olmasını istemediğiniz domain'leri excludelist.txt dosyasına yazabilirsiniz. Varsayılan olarak com.tr ve gov.tr uzantılı siteler eklenmiştir.
- **Ağdaki Cihazlarla Paylaş:** `go-pcap2socks` entegrasyonu sayesinde, bilgisayarınızda çalışan Zapret motorunu yerel ağdaki diğer cihazlarınızla (PlayStation, Xbox, Nintendo Switch, Akıllı TV vb.) paylaşmanızı sağlar. Konsollarda Discord ve Roblox gibi erişim engellerini aşmanın en kararlı yoludur.
- **Tema Desteği:** Arayüz temaları arasında geçiş yapabilirsiniz.

## Ekran Görüntüsü

<img width="530" height="825" alt="Screenshot 2026-09-27 202052" src="https://github.com/user-attachments/assets/efd7bf0a-e46b-4c6b-9221-e6461342b9fc" />



## Gereksinimler

- Windows 10/11
- [.NET Desktop Runtime](https://dotnet.microsoft.com/download/dotnet) (self-contained derleme kullanılıyorsa gerekmez)

## Kurulum

### Hazır sürümü indirme

[Releases](../../releases) sayfasından son sürümü indirip ZapretWinTurkiye.exe dosyasından çalıştırabilirsin.

### Kaynak koddan derleme

```bash
git clone https://github.com/sisa5755/ZapretWinTurkiye.git
cd ZapretWinTurkiye
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish
```

Derleme tamamlandığında `./publish` klasöründe `ZapretGuiWpf.exe` dosyasını bulabilirsin.

## Yerel Ağ Paylaşımı (Konsol / Diğer Cihazlar) Kurulumu

> [!CAUTION]
> Bu özellik Wi-Fi ile kullanılırsa önemli ölçüde hız düşüşü yaşanabilir.
>
> Hız düşüşü yaşarsanız bilgisayarı modem/router'a kablo ile bağlayın.

"Ağdaki Cihazlarla Paylaş" özelliğini kullanabilmek için bilgisayarınızda **[Npcap](https://npcap.com/)** sürücüsünün kurulu olması gerekmektedir.

Özelliği aktifleştirdikten sonra, ağdaki diğer cihazınızın (Örn: PlayStation/Xbox) **Manuel Ağ Ayarları** kısmına girerek aşağıdaki yapılandırmayı uygulamanız yeterlidir:

- **IP Adresi:** `172.24.2.10` ile `172.24.2.255` arasında boş bir IP (Örn: `172.24.2.50`)
- **Alt Ağ Maskesi (Subnet Mask):** `255.255.0.0`
- **Varsayılan Ağ Geçidi (Gateway):** `172.24.2.1`
- **Birincil DNS (Primary DNS):** `1.1.1.1`
- **İkincil DNS (Secondary DNS):** `8.8.8.8`

## Çakışma Önleme

Program, arka planda çalışabilecek diğer DPI atlatma araçlarıyla (`GoodbyeDPI` vb.) veya eski `WinDivert` sürücü kalıntılarıyla çakışmaları otomatik olarak tespit eder, temizler ve güvenli bir açılış sağlar.

## Proje Yapısı

```
├── Helpers/          # Yardımcı sınıflar
├── Models/           # Veri modelleri
├── Themes/           # Arayüz temaları
├── MainWindow.xaml   # Ana pencere arayüzü
├── MainWindow.xaml.cs
├── App.xaml
└── App.xaml.cs
```

## Teşekkürler

- **Zapret** projesinin ana motoru için [@bol-van](https://github.com/bol-van)'a,
- Otomatik blockcheck mantığı ve ilhamı için [splitwire-turkey](https://github.com/cagritaskn/splitwire-turkey) geliştiricisi [@cagritaskn](https://github.com/cagritaskn)'a,
- [go-pcap2socks](https://github.com/DaniilSokolyuk/go-pcap2socks) projesinin geliştiricisi [@DaniilSokolyuk](https://github.com/DaniilSokolyuk)'a,
- [dnscrypt-proxy](https://github.com/dnscrypt/dnscrypt-proxy) projesinin geliştiricisi [@jedisct1](https://github.com/jedisct1)'e teşekkürler.

## Lisans

Bu proje [MIT Lisansı](LICENSE) ile lisanslanmıştır.

## Sorumluluk Reddi

Bu araç, kısıtlanmış içeriklere erişimi kolaylaştırmak amacıyla teknik bir çözüm sunar. Kullanımı, bulunduğunuz ülkenin yasal düzenlemelerine tabidir; kullanımdan doğabilecek sorumluluk kullanıcıya aittir.
