using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
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
        ["ARŞİV"] = "ARCHIVE",
        ["ARŞİVDEN İZLE"] = "WATCH FROM ARCHIVE",
        ["Bu program arşivde bulunamadı."] = "This programme is not in the archive.",
        ["Arşiv adresi yeniden deneniyor..."] = "Retrying the archive address...",
        ["hazır"] = "is ready",
        ["Xtream sunucusu"] = "Xtream server",
        ["Güvenli güncelleme"] = "Safe updates",
        ["Varsayılan"] = "Default",
        ["VERİ KLASÖRÜ"] = "DATA FOLDER",
        ["GELİŞTİREN"] = "DEVELOPER",
        ["KULLANILAN TEKNOLOJİLER"] = "BUILT WITH",
        ["Yayın açılamadı, yeniden deneniyor"] = "Stream failed, retrying",
        ["Oynatma seçenekleri"] = "Playback options",
        ["OYNATMA HIZI"] = "PLAYBACK SPEED",
        ["GÖRÜNTÜ ORANI"] = "ASPECT RATIO",
        ["Otomatik"] = "Automatic",
        ["Ekran görüntüsü al"] = "Take screenshot",
        ["Ekran görüntüsü kaydedildi"] = "Screenshot saved",
        ["Ekran görüntüsü alınamadı."] = "The screenshot could not be taken.",
        ["Başlangıç"] = "Startup",
        ["Son izlenen kanalı aç"] = "Open the last watched channel",
        ["Uygulama açılınca liste yüklendikten sonra en son izlediğin yayın başlar."] = "After the playlist loads on launch, your last stream starts playing.",
        ["Klavye kısayolları"] = "Keyboard shortcuts",
        ["Boşluk"] = "Space",
        ["Oynat / duraklat"] = "Play / pause",
        ["10 saniye geri / ileri"] = "Back / forward 10 seconds",
        ["Sesi artır / azalt"] = "Volume up / down",
        ["Sessize al"] = "Mute",
        ["Tam ekrandan çık"] = "Exit fullscreen",
        ["Önceki / sonraki kanal"] = "Previous / next channel",
        ["Son kanala dön"] = "Back to last channel",
        ["Sorun bildirimi"] = "Reporting a problem",
        ["Bir hata ile karşılaşırsan günlük dosyasını GitHub üzerinden paylaşabilirsin."] = "If you hit a bug you can share the log file through GitHub.",
        ["Günlük dosyasını aç"] = "Open the log file",
        ["Sorun bildir"] = "Report an issue",
        ["Grubu gizle"] = "Hide group",
        ["Gizli gruplar"] = "Hidden groups",
        ["Bir gruba sağ tıklayıp gizleyebilirsin; gizlenen gruplar listede görünmez."] = "Right-click a group to hide it; hidden groups no longer appear in the list.",
        ["Tümünü göster"] = "Show all",
        ["Gizlenmiş grup yok."] = "No hidden groups.",
        ["Göster"] = "Show",
        ["Bu kanal"] = "This channel",
        ["Tüm kanallar"] = "All channels",
        ["Vurgu rengi"] = "Accent colour",
        ["Düğmeler, rozetler ve seçili öğeler bu renkle çizilir."] = "Buttons, badges and selected items are drawn in this colour.",
        ["Turuncu"] = "Orange",
        ["Mavi"] = "Blue",
        ["Mor"] = "Purple",
        ["Yeşil"] = "Green",
        ["Kırmızı"] = "Red",
        ["Ses ve altyazı"] = "Audio and subtitles",
        ["SES İZİ"] = "AUDIO TRACK",
        ["ALTYAZI"] = "SUBTITLES",
        ["ALTYAZI GECİKMESİ"] = "SUBTITLE DELAY",
        ["ALTYAZI BOYUTU"] = "SUBTITLE SIZE",
        ["Ses izi bulunamadı."] = "No audio track found.",
        ["Kapalı"] = "Off",
        ["Dosyadan altyazı yükle..."] = "Load subtitle from file...",
        ["Altyazı dosyası seç"] = "Select a subtitle file",
        ["Altyazı yüklendi"] = "Subtitle loaded",
        ["Altyazı yüklenemedi."] = "The subtitle could not be loaded.",
        ["Küçük"] = "Small",
        ["Normal"] = "Normal",
        ["Büyük"] = "Large",
        ["Boyut değişikliği sonraki oynatmada uygulanır."] = "The size change applies the next time playback starts.",
        ["Otomatik davranışlar"] = "Automatic behaviour",
        ["Sonraki bölüme geç"] = "Play the next episode",
        ["Bir bölüm bitince aynı dizinin sıradaki bölümü kendiliğinden başlar."] = "When an episode ends, the next episode of the same series starts on its own.",
        ["Listeyi arka planda yenile"] = "Refresh the playlist in the background",
        ["Uzak listeler altı saatte bir sessizce güncellenir, açılışı bekletmez."] = "Remote playlists are updated quietly every six hours without delaying startup.",
        ["Sonraki bölüm"] = "Next episode",
        ["Liste arka planda güncellendi"] = "Playlist updated in the background",
        ["Sırala ve filtrele"] = "Sort and filter",
        ["SIRALAMA"] = "SORTING",
        ["FİLTRE"] = "FILTER",
        ["Liste sırası"] = "Playlist order",
        ["Yeni eklenenler"] = "Recently added",
        ["En çok izlenen"] = "Most watched",
        ["Tümü"] = "All",
        ["✚ Son Eklenenler"] = "✚ Recently Added",
        ["SON EKLENENLER"] = "RECENTLY ADDED",
        ["Son izlendi"] = "Last watched",
        ["Eklendi"] = "Added",
        ["İstatistikler"] = "Statistics",
        ["İzleme süreleri yalnızca bu cihazda tutulur."] = "Watch times are kept on this device only.",
        ["TOPLAM İZLEME"] = "TOTAL WATCHED",
        ["İZLENEN İÇERİK"] = "ITEMS WATCHED",
        ["AÇILIŞ SAYISI"] = "TIMES OPENED",
        ["Türlere göre"] = "By type",
        ["En çok izlenenler"] = "Most watched",
        ["İstatistikleri sıfırla"] = "Reset statistics",
        ["Henüz izleme kaydı yok."] = "No watch history yet.",
        ["Listede yok"] = "Not in the playlist",
        ["Grubu kilitle"] = "Lock group",
        ["Ebeveyn kilidi"] = "Parental lock",
        ["Bir gruba sağ tıklayıp kilitleyebilirsin. Kilitli gruplar PIN girilene kadar listede görünmez."] = "Right-click a group to lock it. Locked groups stay hidden until the PIN is entered.",
        ["4-8 haneli PIN"] = "4-8 digit PIN",
        ["PIN belirle"] = "Set PIN",
        ["PIN\'i değiştir"] = "Change PIN",
        ["Kilidi aç"] = "Unlock",
        ["PIN\'i kaldır"] = "Remove PIN",
        ["Kilitli grup yok."] = "No locked groups.",
        ["Kilidi kaldır"] = "Remove lock",
        ["Önce bir PIN belirleyin."] = "Set a PIN first.",
        ["PIN 4-8 haneli sayı olmalı."] = "The PIN must be 4-8 digits.",
        ["PIN kaydedildi."] = "PIN saved.",
        ["PIN hatalı."] = "Wrong PIN.",
        ["Kilit bu oturum için açıldı."] = "Unlocked for this session.",
        ["PIN kaldırıldı, kilitler açıldı."] = "PIN removed, locks cleared.",
        ["Koleksiyonlar"] = "Collections",
        ["Koleksiyon"] = "Collection",
        ["KOLEKSİYON"] = "COLLECTION",
        ["Koleksiyona ekle"] = "Add to collection",
        ["Koleksiyona eklendi"] = "Added to collection",
        ["Yeni koleksiyon oluştur"] = "Create a new collection",
        ["Yeni koleksiyon"] = "New collection",
        ["Bir içeriğe sağ tıklayıp koleksiyona ekleyebilirsin; koleksiyonlar grup listesinde görünür."] = "Right-click an item to add it to a collection; collections appear in the group list.",
        ["Henüz koleksiyon yok."] = "No collections yet.",
        ["Sil"] = "Delete",
        ["Görünüm"] = "Appearance",
        ["Koyu"] = "Dark",
        ["Açık"] = "Light",
        ["Gündüz"] = "Daytime",
        ["Varsayılan"] = "Default",
        ["Mini oynatıcı"] = "Mini player",
        ["Mini oynatıcıdan çık"] = "Leave mini player",
        ["Detayları göster"] = "Show details",
        ["İÇERİK BİLGİSİ"] = "TITLE INFO",
        ["İçerik bilgisi (TMDB)"] = "Title info (TMDB)",
        ["Film ve dizilerde afiş, puan ve özet göstermek için ücretsiz bir TMDB API anahtarı gerekir. Anahtar yalnızca bu cihazda saklanır."] = "A free TMDB API key is needed to show posters, ratings and summaries for movies and series. The key is stored on this device only.",
        ["TMDB API anahtarı"] = "TMDB API key",
        ["Kaydet"] = "Save",
        ["Anahtar al"] = "Get a key",
        ["Anahtar kaydedildi."] = "Key saved.",
        ["Anahtar kaldırıldı."] = "Key removed.",
        ["İçerik bilgisi için ayarlardan TMDB anahtarı girin."] = "Enter a TMDB key in settings to see title info.",
        ["Bilgi alınıyor..."] = "Fetching info...",
        ["Bu içerik için bilgi bulunamadı."] = "No info found for this title.",
        ["Günaydın"] = "Good morning",
        ["İyi günler"] = "Good afternoon",
        ["İyi akşamlar"] = "Good evening",
        ["İyi geceler"] = "Good night",
        ["KİTAPLIĞIN"] = "YOUR LIBRARY",
        ["KİTAPLIK"] = "LIBRARY",
        ["CANLI YAYIN"] = "LIVE BROADCAST",
        ["grup"] = "groups",
        ["yeni"] = "new",
        ["bölümde kaldın"] = "episodes in progress",
        ["Görünen ad"] = "Display name",
        ["Ana ekrandaki selamlamada kullanılır. Boş bırakırsan Windows hesap adın kullanılır."] = "Used in the greeting on the home screen. Leave it empty to use your Windows account name.",
        ["Adın"] = "Your name",
        ["YENİLE"] = "REFRESH",
        ["HAKKINDA"] = "ABOUT",
        ["AYARLAR"] = "SETTINGS",
        ["Performans"] = "Performance",
        ["Zayıf bilgisayarlarda ve yavaş bağlantılarda yayınların daha akıcı açılması için."] = "For smoother playback on slower computers and connections.",
        ["Donanım hızlandırma"] = "Hardware acceleration",
        ["Görüntü çözme işini ekran kartına devreder; işlemcisi zayıf bilgisayarlarda en çok işe yarayan ayar budur. Görüntü bozulursa kapat."] = "Moves video decoding to the graphics card; this helps the most on slower processors. Turn it off if the picture breaks up.",
        ["DirectX 11"] = "DirectX 11",
        ["Ağ tamponu"] = "Network buffer",
        ["Yayından ne kadar veri önden indirileceğini belirler. Bağlantı yavaşsa yüksek değer takılmayı azaltır, kanal açılışı biraz gecikir."] = "How much of the stream is downloaded ahead. On a slow connection a larger buffer reduces stutter but channels take longer to start.",
        ["1 sn"] = "1 s",
        ["1,8 sn"] = "1.8 s",
        ["3 sn"] = "3 s",
        ["6 sn"] = "6 s",
        ["Yayın kalitesi"] = "Stream quality",
        ["Çok kaliteli yayınlarda (HLS) daha düşük çözünürlüklü sürümü seçer. Yavaş bağlantıda en çok fark yaratan ikinci ayar."] = "Picks a lower resolution variant on adaptive (HLS) streams. The second biggest win on a slow connection.",
        ["En fazla 720p"] = "Up to 720p",
        ["En fazla 480p"] = "Up to 480p",
        ["En düşük"] = "Lowest",
        ["Hafifletme"] = "Lighten the load",
        ["Düşük güç modu"] = "Low power mode",
        ["Görüntü filtrelerini kapatır, arka plan görselini kaldırır ve çözmeyi hafifletir. Eski bilgisayarlarda akıcılığı artırır."] = "Turns off video filters, removes the background image and lightens decoding. Helps on older computers.",
        ["Kanal logolarını indirme"] = "Do not download channel logos",
        ["Logolar internetten indirilmez, yerine çizilmiş yer tutucu kullanılır. Yavaş bağlantıda yayına daha çok bant genişliği kalır."] = "Logos are not downloaded; the drawn placeholder is used instead, leaving more bandwidth for the stream.",
        ["Değişiklikler bir sonraki kanal açılışında geçerli olur."] = "Changes take effect the next time a channel starts.",
        ["Favorilere ekle"] = "Add to favourites",
        ["ilk"] = "first",
    };

    private static readonly Dictionary<string, string> EnglishToTurkish =
        TurkishToEnglish.GroupBy(pair => pair.Value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Key, StringComparer.Ordinal);

    private static readonly ConditionalWeakTable<Control, TextSnapshot> Applied = new();

    public static string Language { get; private set; } = Turkish;

    public static event Action? LanguageChanged;

    public static void Initialize() => Language = Preferences.Current.Language == English ? English : Turkish;

    public static void SetLanguage(string language)
    {
        language = language == English ? English : Turkish;
        if (language == Language) return;
        Language = language;
        Preferences.Current.Language = language;
        Preferences.Save();
        LanguageChanged?.Invoke();
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
}
