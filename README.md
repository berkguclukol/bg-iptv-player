<div align="center">
  <img src="native/BgIptvPlayer.Native/Assets/app-icon.png" width="128" alt="BG IPTV Player ikonu">
  <h1>BG IPTV Player</h1>
  <p>Windows için hızlı, yerel ve modern M3U/IPTV oynatıcısı.</p>

  [![Latest Release](https://img.shields.io/github/v/release/berkguclukol/bg-iptv-player?color=7c6cff&label=release)](https://github.com/berkguclukol/bg-iptv-player/releases/latest)
  [![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-168bff)](https://github.com/berkguclukol/bg-iptv-player/releases/latest)
  [![License](https://img.shields.io/github/license/berkguclukol/bg-iptv-player)](LICENSE)
</div>

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
- Uygulama açılışında yeni GitHub Release sürümü kontrolü

## Kurulum

1. [En güncel Release'i indirin](https://github.com/berkguclukol/bg-iptv-player/releases/latest).
2. Adında `Setup-x64.exe` geçen önerilen kurulum dosyasını çalıştırın.
3. Alternatif olarak taşınabilir ZIP paketini bir klasöre çıkarıp `BG-IPTV-Player.exe` dosyasını açın.

Windows 10/11 x64 desteklenir. Paket kendi .NET çalışma ortamını ve video motorunu içerir; ayrıca .NET veya VLC kurmanız gerekmez.

## Playlist ekleme

**Ayarlar → Oynatma Listeleri** bölümüne girin. Playlist URL'sini yapıştırıp ekleyebilir veya **Dosyadan ekle** seçeneğini kullanabilirsiniz. URL listesinin güncel kopyasını almak için yanındaki **Yenile** düğmesine basın.

## Kaynaktan çalıştırma

.NET 8 SDK ve Visual Studio C++ masaüstü bileşenleri gereklidir.

```powershell
cd native/BgIptvPlayer.Native
dotnet run
```

Teknolojiler: C# · .NET 8 · Avalonia UI · LibVLCSharp

## Gizlilik

Oynatma listeleri ve indirilen liste önbelleği yalnızca bilgisayarınızdaki `%LOCALAPPDATA%\BgIptvPlayer` klasöründe tutulur. Projeye veya herhangi bir sunucuya gönderilmez.

## Yasal kullanım

BG IPTV Player herhangi bir kanal, yayın veya oynatma listesi sağlamaz. Uygulamayı yalnızca erişim ve izleme hakkınız bulunan içeriklerle kullanın.

## Lisans

[MIT Lisansı](LICENSE)
