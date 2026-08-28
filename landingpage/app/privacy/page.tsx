import type { Metadata } from 'next';
import { ArrowLeft, ExternalLink, ShieldCheck } from 'lucide-react';

export const metadata: Metadata = {
  title: 'Gizlilik Politikası | BG IPTV Player',
  description: 'BG IPTV Player gizlilik politikası ve yerel veri kullanımı hakkında bilgiler.',
};

export const dynamic = 'force-static';

const sections = [
  { title: 'Bilgisayarınızda saklanan veriler', body: <>Oynatma listelerinin adları, URL’leri, yerel dosya yolları, aktif liste seçimi ve indirilen liste önbelleği yalnızca <code>%LOCALAPPDATA%\BgIptvPlayer</code> klasöründe saklanır. Bu bilgiler geliştiriciye veya bir BG IPTV Player sunucusuna gönderilmez.</> },
  { title: 'Ağ bağlantıları', body: <>Uygulama; sizin eklediğiniz listeleri indirmek veya yenilemek, listedeki yayınları oynatmak, listede belirtilen kanal logolarını göstermek ve GitHub Releases üzerinden yeni sürüm kontrolü yapmak için internete bağlanır.</> },
  { title: 'Üçüncü taraf hizmetleri', body: <>Oynatma listesi sağlayıcıları, yayın ve logo sunucuları, GitHub ve internet servis sağlayıcınız IP adresiniz ile istek bilgileri gibi teknik verileri alabilir. Bu hizmetlerin kendi gizlilik politikaları geçerlidir. BG IPTV Player bu verileri teslim almaz veya kontrol etmez.</> },
  { title: 'Veri paylaşımı', body: <>BG IPTV Player kişisel bilgileri geliştiriciyle veya üçüncü taraflarla paylaşmaz, satmaz ya da kiralamaz. Uygulamada reklam, analiz, telemetri veya kullanıcı takibi bulunmaz.</> },
  { title: 'Verilerin silinmesi', body: <>Oynatma listelerini uygulamanın Ayarlar bölümünden kaldırabilirsiniz. Tüm yerel ayarları ve önbelleği silmek için uygulamayı kapattıktan sonra <code>%LOCALAPPDATA%\BgIptvPlayer</code> klasörünü silebilirsiniz.</> },
  { title: 'Politikadaki değişiklikler', body: <>Bu politikadaki önemli değişiklikler, güncellenmiş yürürlük tarihiyle birlikte bu sayfada ve projenin GitHub deposunda yayımlanacaktır.</> },
];

export default function PrivacyPage() {
  return (
    <main className="min-h-screen">
      <div className="aurora" aria-hidden="true" />
      <header className="relative border-b border-white/8 bg-[#080b12]/75 backdrop-blur-xl">
        <div className="mx-auto flex h-18 max-w-4xl items-center justify-between px-5 sm:px-8">
          <a href="/" className="flex items-center gap-2 text-sm text-slate-400 transition hover:text-white"><ArrowLeft className="size-4" /> Ana sayfa</a>
          <div className="flex items-center gap-2"><img src="/app-icon.png" width="34" height="34" alt="" className="rounded-lg" /><span className="text-sm font-bold text-white">BG IPTV Player</span></div>
        </div>
      </header>

      <article className="relative mx-auto max-w-4xl px-5 py-16 sm:px-8 sm:py-22">
        <div className="mb-12 border-b border-white/8 pb-10">
          <span className="mb-5 grid size-12 place-items-center rounded-2xl border border-violet-400/20 bg-violet-500/10 text-violet-300"><ShieldCheck className="size-6" /></span>
          <p className="eyebrow">YASAL</p>
          <h1 className="mt-3 text-4xl font-bold tracking-tight text-white sm:text-5xl">Gizlilik Politikası</h1>
          <p className="mt-5 max-w-2xl leading-7 text-slate-400">BG IPTV Player, Berk Güçlükol tarafından geliştirilen yerel bir masaüstü medya oynatıcısıdır. Hesap gerektirmez ve kişisel veri toplamaz.</p>
          <p className="mt-4 text-xs font-medium text-slate-600">Yürürlük tarihi: 29 Ağustos 2026</p>
        </div>

        <div className="space-y-9">
          {sections.map((section) => <section key={section.title}><h2 className="text-lg font-semibold text-white">{section.title}</h2><div className="privacy-copy mt-3 text-sm leading-7 text-slate-400">{section.body}</div></section>)}
        </div>

        <section className="mt-12 rounded-2xl border border-violet-400/15 bg-violet-500/[.07] p-6">
          <h2 className="font-semibold text-white">İletişim</h2>
          <p className="mt-2 text-sm leading-6 text-slate-400">Gizlilikle ilgili sorularınız için GitHub Issues üzerinden iletişim kurabilirsiniz.</p>
          <a href="https://github.com/berkguclukol/bg-iptv-player/issues" target="_blank" rel="noreferrer" className="mt-4 inline-flex items-center gap-2 text-sm font-semibold text-violet-300 transition hover:text-violet-200">GitHub Issues <ExternalLink className="size-3.5" /></a>
        </section>
      </article>

      <footer className="border-t border-white/7 py-8"><div className="mx-auto max-w-4xl px-5 text-xs text-slate-600 sm:px-8">© 2026 Berk Güçlükol · BG IPTV Player</div></footer>
    </main>
  );
}
