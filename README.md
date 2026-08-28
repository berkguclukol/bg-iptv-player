# BG IPTV Player

Windows için hızlı, yerel ve sade bir M3U/IPTV oynatıcısı.

## Özellikler

- M3U ve M3U8 listelerini URL veya dosyadan ekleme
- URL listelerini yerel önbellekte saklama ve isteğe bağlı yenileme
- Birden fazla oynatma listesi ve aktif liste seçimi
- Canlı TV, film ve dizi ayrımı
- Grup ve kanal arama
- Uygulama içinde LibVLC tabanlı video oynatma
- Film ve videolarda ileri–geri sarma
- Ses, duraklatma ve fare hareketinde görünen tam ekran kontrolleri
- Kanal logoları ve logosuz kanallar için otomatik simge

## Kurulum

En güncel Windows paketini [Releases](../../releases) sayfasından indirin. ZIP dosyasını bir klasöre çıkarın ve `BG-IPTV-Player.exe` dosyasını çalıştırın.

Windows 10/11 x64 desteklenir. Paket kendi .NET çalışma ortamını ve video motorunu içerir; ayrıca VLC kurmanız gerekmez.

## Kaynaktan çalıştırma

.NET 8 SDK ve Visual Studio C++ masaüstü bileşenleri gereklidir.

```powershell
cd native/BgIptvPlayer.Native
dotnet run
```

## Gizlilik

Oynatma listeleri ve indirilen liste önbelleği yalnızca bilgisayarınızdaki `%LOCALAPPDATA%\BgIptvPlayer` klasöründe tutulur. Projeye veya herhangi bir sunucuya gönderilmez.

## Yasal kullanım

Uygulama yalnızca erişim hakkınız bulunan yayın ve medya listeleriyle kullanılmalıdır. Proje herhangi bir kanal veya oynatma listesi sağlamaz.

## Lisans

MIT

