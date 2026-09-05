using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.LogicalTree;

namespace BgIptvPlayer.Native;

// Uygulama Türkçe yazıldığı için sözlük Türkçe metinden İngilizceye eşlenir.
// Çeviri iki yönlü çalışır; ekranda hangi dil varsa hedef dile çevrilir.
public static class Localization
{
    public const string Turkish = "tr";
    public const string English = "en";

    private static readonly Dictionary<string, string> TurkishToEnglish = new(StringComparer.Ordinal)
    {
        // Genel gezinme
        ["Ana ekran"] = "Home",
        ["Ayarlar"] = "Settings",
        ["Hakkında"] = "About",
        ["Gizlilik"] = "Privacy",
        ["Web Sitesi"] = "Website",
        ["Kapat"] = "Close",
        ["Geri"] = "Back",
        ["Yenile"] = "Refresh",
        ["Listeyi yenile"] = "Refresh playlist",
        ["Kitaplık"] = "Library",
        ["Gruplar"] = "Groups",
        ["Tümünü gör"] = "See all",
        ["Kanal"] = "Channel",
        ["KANALLAR"] = "CHANNELS",

        // Bölümler
        ["Canlı TV"] = "Live TV",
        ["CANLI TV"] = "LIVE TV",
        ["Filmler"] = "Movies",
        ["FİLMLER"] = "MOVIES",
        ["Diziler"] = "Series",
        ["DİZİLER"] = "SERIES",
        ["Favoriler"] = "Favourites",
        ["FAVORİLER"] = "FAVOURITES",
        ["İzlemeye Devam Et"] = "Continue Watching",
        ["İZLEMEYE DEVAM ET"] = "CONTINUE WATCHING",
        ["▶ İzlemeye Devam Et"] = "▶ Continue Watching",
        ["SON İZLENENLER"] = "RECENTLY WATCHED",
        ["◷ Son İzlenenler"] = "◷ Recently Watched",
        ["TÜM İÇERİKLER"] = "ALL CONTENT",
        ["Diğer"] = "Other",
        ["Diğer Bölümler"] = "Other Episodes",
        ["Diğer bölümler"] = "Other episodes",
        ["DİĞER BÖLÜMLER"] = "OTHER EPISODES",

        // Rozetler
        ["CANLI"] = "LIVE",
        ["FİLM"] = "MOVIE",
        ["DİZİ"] = "SERIES",
        ["DİZİ  ›"] = "SERIES  ›",
        ["AÇ  ›"] = "OPEN  ›",
        ["HAZIR"] = "READY",
        ["Hazır"] = "Ready",
        ["İzlemeye hazır"] = "Ready to watch",
        ["✓ AKTİF"] = "✓ ACTIVE",
        ["Etkinleştir"] = "Activate",

        // Arama
        ["Bu bölümde ara"] = "Search in this section",
        ["Tüm içerikte ara"] = "Search everything",
        ["Program ara..."] = "Search programmes...",
        [" sonuçları"] = " results",

        // Oynatıcı
        ["Oynat / duraklat"] = "Play / pause",
        ["Önceki kanal"] = "Previous channel",
        ["Sonraki kanal"] = "Next channel",
        ["Son kanal"] = "Last channel",
        ["Favori"] = "Favourite",
        ["Favorilerden çıkar"] = "Remove from favourites",
        ["Ses"] = "Volume",
        ["Tam ekran"] = "Fullscreen",
        ["Yayın akışı"] = "TV guide",
        ["10 saniye geri"] = "Back 10 seconds",
        ["10 saniye ileri"] = "Forward 10 seconds",
        ["Listeden kaldır"] = "Remove from list",
        ["Geçmişi Temizle"] = "Clear History",
        ["Listeyi Temizle"] = "Clear List",
        ["Bir kanal veya içerik seç"] = "Pick a channel or title",
        ["İzlemek için bir kanal seçin"] = "Select a channel to start watching",
        ["Oynatılıyor"] = "Playing",
        ["Canlı yayın oynatılıyor"] = "Live stream playing",
        ["Yayına bağlanılıyor..."] = "Connecting to stream...",
        ["Yayın açılamadı; kaynak çevrimdışı olabilir."] = "Stream could not be opened; the source may be offline.",
        ["İsimsiz kanal"] = "Untitled channel",

        // EPG
        ["7 GÜNLÜK YAYIN AKIŞI"] = "7 DAY TV GUIDE",
        ["ŞİMDİ YAYINDA"] = "ON NOW",
        ["BUGÜN"] = "TODAY",
        ["Önceki gün"] = "Previous day",
        ["Sonraki gün"] = "Next day",
        ["Bu kanal için yayın akışı bulunamadı."] = "No guide data for this channel.",
        ["Bu gün için yayın akışı bulunamadı."] = "No guide data for this day.",
        ["Aramanızla eşleşen program bulunamadı."] = "No programme matched your search.",
        ["EPG bilgileri yükleniyor..."] = "Loading guide data...",

        // Oynatma listeleri
        ["Oynatma Listeleri"] = "Playlists",
        ["M3U, dosya veya Xtream hesabı ekleyin."] = "Add an M3U link, a file or an Xtream account.",
        ["Dosyadan ekle"] = "Add from file",
        ["M3U URL EKLE"] = "ADD M3U URL",
        ["XTREAM HESABI EKLE"] = "ADD XTREAM ACCOUNT",
        ["KAYITLI LİSTELER"] = "SAVED PLAYLISTS",
        ["Liste adı"] = "Playlist name",
        ["Hesap adı"] = "Account name",
        ["Kullanıcı adı"] = "Username",
        ["Şifre"] = "Password",
        ["Ekle"] = "Add",
        ["Xtream Ekle"] = "Add Xtream",
        ["Kaldır"] = "Remove",
        ["Geçerli bir http veya https adresi girin"] = "Enter a valid http or https address",
        ["Sunucu, kullanıcı adı ve şifreyi kontrol edin"] = "Check the server, username and password",
        ["M3U oynatma listesi seç"] = "Select an M3U playlist",
        ["Oynatma listesi dosyası bulunamadı."] = "Playlist file not found.",
        ["Oynatma listesi hazırlanıyor..."] = "Preparing playlist...",
        ["Liste yükleniyor..."] = "Loading playlist...",
        ["Liste yüklenemedi"] = "Playlist could not be loaded",
        ["Sunucu geçerli bir M3U listesi döndürmedi."] = "The server did not return a valid M3U playlist.",
        ["Sunucu geçerli XMLTV verisi döndürmedi."] = "The server did not return valid XMLTV data.",
        ["Xtream hesabı doğrulanıyor..."] = "Verifying Xtream account...",
        ["Xtream hesabı doğrulanamadı."] = "The Xtream account could not be verified.",
        ["Xtream hesap bilgileri okunamadı."] = "Xtream account details could not be read.",
        ["Xtream kategorileri ve içerikleri alınıyor..."] = "Fetching Xtream categories and content...",
        ["Xtream API yanıt vermedi · M3U deneniyor..."] = "Xtream API did not respond, trying M3U...",
        ["Kategori yanıtı geçersiz."] = "The category response was invalid.",
        ["İçerik yanıtı geçersiz."] = "The content response was invalid.",
        ["kullanıcı bilgileri gizli"] = "credentials hidden",

        // Güncelleme
        ["Güncelle"] = "Update",
        ["Güncelleme"] = "Updates",
        ["Yeni sürüm hazır"] = "A new version is ready",
        ["Notlar"] = "Notes",
        ["İndirilip kurulur, ardından uygulama yeniden başlar."] = "It downloads, installs and the app restarts.",
        ["İndirmek için sürüm sayfasını açın."] = "Open the release page to download it.",
        ["İndiriliyor..."] = "Downloading...",
        ["Kurulum başlatılıyor..."] = "Starting the installer...",
        ["İndirilen dosya eksik."] = "The downloaded file is incomplete.",
        ["Güncellemeleri denetle"] = "Check for updates",
        ["Denetleniyor..."] = "Checking...",
        ["Uygulamanın en güncel sürümünü kullanıyorsunuz."] = "You are running the latest version.",
        ["Güncelleme denetlenemedi. İnternet bağlantınızı kontrol edin."] = "Could not check for updates. Check your internet connection.",
        ["Sürüm notlarını aç"] = "Open release notes",
        ["Şimdi güncelle"] = "Update now",
        ["Yüklü sürüm"] = "Installed version",
        ["Uygulama her açılışta güncellemeleri kendiliğinden denetler."] = "The app checks for updates automatically on every launch.",
        ["Güncellemeler yalnızca projenin resmî GitHub sürüm sayfasından indirilir."] = "Updates are downloaded only from the official GitHub releases page of the project.",

        // Dil
        ["Genel"] = "General",
        ["Dil"] = "Language",
        ["Uygulama dilini seçin. Değişiklik anında uygulanır."] = "Choose the app language. The change is applied instantly.",
        ["Arayüz dili"] = "Interface language",
        ["Kanal, film ve dizi adları oynatma listenizden geldiği için çevrilmez."] = "Channel, movie and series names come from your playlist, so they are not translated.",

        // Gizlilik
        ["Verileriniz cihazınızda kalır"] = "Your data stays on your device",
        ["Uygulama hesap açmanızı istemez, kullanım verisi toplamaz ve içeriklerinizi hiçbir sunucuya göndermez."] = "The app does not ask you to sign up, collects no usage data and never sends your content to any server.",
        ["Cihazda saklananlar"] = "Stored on this device",
        ["Oynatma listesi adresleri ve Xtream hesap bilgileri"] = "Playlist addresses and Xtream account details",
        ["İzleme geçmişi, favoriler ve kaldığınız konum"] = "Watch history, favourites and resume positions",
        ["Liste ve yayın akışı önbelleği"] = "Playlist and TV guide cache",
        ["Veri klasörü"] = "Data folder",
        ["Klasörü aç"] = "Open folder",
        ["İzleme geçmişini temizle"] = "Clear watch history",
        ["Önbelleği temizle"] = "Clear cache",
        ["İzleme geçmişi, favoriler ve kaldığınız konumlar silindi."] = "Watch history, favourites and resume positions were deleted.",
        ["Önbellek temizlendi. Listeler bir sonraki açılışta yeniden indirilir."] = "Cache cleared. Playlists will be downloaded again on the next launch.",
        ["Gizlilik metnini aç"] = "Open privacy notice",
        ["Ağ bağlantıları"] = "Network connections",
        ["Yalnızca sizin eklediğiniz liste sunucularına, yayın kaynaklarına ve sürüm denetimi için GitHub sayfasına bağlanılır."] = "Connections are made only to the playlist servers you added, to your stream sources and to the GitHub page for update checks.",

        // Hakkında
        ["UYGULAMA"] = "APPLICATION",
        ["M3U ve IPTV listelerini uygulama içinde oynatmak için geliştirilmiş Windows masaüstü uygulaması."] = "A Windows desktop application built for playing M3U and IPTV playlists.",
        ["Geliştiren: Berk Güçlükol"] = "Developed by Berk Güçlükol",
        ["Kullanılan teknolojiler"] = "Built with",
        ["Sürüm"] = "Version",
        ["Lisans ve kaynak kodu GitHub sayfasındadır."] = "Licence and source code are on the GitHub page.",

        // Zaman ve sayılar
        ["az önce"] = "just now",
        ["dk önce"] = "min ago",
        ["sa önce"] = "h ago",
        ["gün önce"] = "d ago",
        ["Kaldığın yer"] = "Stopped at",
        ["Kaldığınız yerden devam ediyor"] = "Resuming where you left off",
        ["Sırada"] = "Next",
        ["Şimdi"] = "Now",
        ["Sezon"] = "Season",
        ["BÖLÜM"] = "EPISODE",
        ["bölüm"] = "episodes",
        ["içerik"] = "items",
        ["kanal"] = "channels",
        ["film"] = "movies",
        ["dizi"] = "series",
        ["canlı"] = "live",
        ["sonuç"] = "results",
        ["EPG hazır"] = "guide ready",
        ["EPG alınamadı"] = "guide unavailable",
        ["Liste hazır"] = "Playlist ready",
        ["Liste açılamadı"] = "Playlist could not be opened",
        ["hazırlanıyor..."] = "loading...",
        ["Yükleniyor"] = "Loading",
        ["İndiriliyor"] = "Downloading",
        ["Güncelleme yapılamadı"] = "Update failed",
        ["Arama"] = "Search",
        ["Bölüm"] = "Episode",
        ["SEZON"] = "SEASON",
        ["OYNAT"] = "PLAY",
        ["Favorilere ekle"] = "Add to favourites",
        ["hazır"] = "is ready",
        ["Xtream sunucusu"] = "Xtream server",
        ["Güvenli güncelleme"] = "Safe updates",
        ["Varsayılan"] = "Default",
        ["VERİ KLASÖRÜ"] = "DATA FOLDER",
        ["GELİŞTİREN"] = "DEVELOPER",
        ["KULLANILAN TEKNOLOJİLER"] = "BUILT WITH",
        ["ilk"] = "first",
    };

    private static readonly Dictionary<string, string> EnglishToTurkish =
        TurkishToEnglish.GroupBy(pair => pair.Value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Key, StringComparer.Ordinal);

    private static readonly ConditionalWeakTable<Control, TextSnapshot> Applied = new();
    private static readonly string PreferencesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BgIptvPlayer", "preferences.json");

    public static string Language { get; private set; } = Turkish;

    public static event Action? LanguageChanged;

    public static void Initialize()
    {
        try
        {
            if (!File.Exists(PreferencesPath)) return;
            var preferences = JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(PreferencesPath));
            if (preferences?.Language == English) Language = English;
        }
        catch
        {
            // Tercih dosyası okunamazsa varsayılan dil kullanılır.
        }
    }

    public static void SetLanguage(string language)
    {
        language = language == English ? English : Turkish;
        if (language == Language) return;
        Language = language;
        Save();
        LanguageChanged?.Invoke();
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PreferencesPath)!);
            File.WriteAllText(PreferencesPath,
                JsonSerializer.Serialize(new AppPreferences { Language = Language }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Dil tercihi kaydedilemezse uygulama çalışmaya devam eder.
        }
    }

    // Metni, hangi dilde yazılmış olursa olsun seçili dile çevirir.
    public static string T(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        var map = Language == English ? TurkishToEnglish : EnglishToTurkish;
        return map.TryGetValue(text, out var translated) ? translated : text;
    }

    // XAML içindeki sabit metinleri çevirir; liste öğeleri veriye bağlı olduğu için atlanır.
    public static void Apply(ILogical root)
    {
        foreach (var child in root.LogicalChildren)
        {
            if (child is ItemsControl) continue;
            if (child is Control control) Translate(control);
            Apply(child);
        }
    }

    private static void Translate(Control control)
    {
        var snapshot = Applied.GetValue(control, _ => new TextSnapshot());

        switch (control)
        {
            case TextBox box:
                if (box.Watermark is { Length: > 0 } watermark)
                {
                    if (watermark != snapshot.Text) snapshot.Text = watermark;
                    box.Watermark = snapshot.Text = T(snapshot.Text);
                }
                break;
            case TextBlock block:
                if (block.Text is { Length: > 0 } text)
                {
                    if (text != snapshot.Text) snapshot.Text = text;
                    block.Text = snapshot.Text = T(snapshot.Text);
                }
                break;
            case ContentControl { Content: string content } contentControl when content.Length > 0:
                if (content != snapshot.Text) snapshot.Text = content;
                contentControl.Content = snapshot.Text = T(snapshot.Text);
                break;
        }

        if (ToolTip.GetTip(control) is string tip && tip.Length > 0)
        {
            if (tip != snapshot.Tip) snapshot.Tip = tip;
            ToolTip.SetTip(control, snapshot.Tip = T(snapshot.Tip));
        }
    }

    private sealed class TextSnapshot
    {
        public string Text = "";
        public string Tip = "";
    }

    private sealed class AppPreferences
    {
        public string Language { get; set; } = Turkish;
    }
}
