import {
  ArrowRight,
  CheckCircle2,
  Code2,
  Download,
  Film,
  Layers3,
  ListVideo,
  Maximize2,
  ShieldCheck,
  Volume2,
} from 'lucide-react';
import { buttonVariants } from '@/components/ui/button';
import { cn } from '@/lib/utils';

const releaseUrl = 'https://github.com/berkguclukol/bg-iptv-player/releases/latest';
const repositoryUrl = 'https://github.com/berkguclukol/bg-iptv-player';

const features = [
  { icon: ListVideo, title: 'Birden fazla liste', text: 'M3U ve M3U8 listelerini URL veya dosyadan ekle, aktif listeyi dilediğin an değiştir.' },
  { icon: Layers3, title: 'Düzenli medya arşivi', text: 'İçeriklerini Canlı TV, Filmler ve Diziler olarak otomatik ayrılmış biçimde keşfet.' },
  { icon: Film, title: 'Uygulama içinde oynatma', text: 'LibVLC tabanlı güçlü video motoruyla yayınları başka bir pencere açmadan izle.' },
  { icon: Maximize2, title: 'Akıcı tam ekran', text: 'Çift tıkla tam ekrana geç; kontroller fare hareketinde görünür, sonra sessizce kaybolur.' },
  { icon: Volume2, title: 'Eksiksiz kontroller', text: 'Ses, duraklatma ve ileri–geri sarma kontrolleri sade ve erişilebilir biçimde yanında.' },
  { icon: ShieldCheck, title: 'Yerel ve gizli', text: 'Listelerin ve ayarların bilgisayarında kalır. Reklam, analiz ve kullanıcı takibi yoktur.' },
];

export default function Home() {
  return (
    <main className="min-h-screen overflow-hidden">
      <div className="aurora" aria-hidden="true" />
      <header className="relative z-20 border-b border-white/8 bg-[#080b12]/75 backdrop-blur-xl">
        <div className="mx-auto flex h-18 max-w-6xl items-center justify-between px-5 sm:px-8">
          <a href="#top" className="flex items-center gap-3" aria-label="BG IPTV Player ana sayfa">
            <img src="/app-icon.png" width="42" height="42" alt="" className="rounded-xl" />
            <div><p className="text-sm font-bold tracking-wide text-white">BG IPTV</p><p className="text-[9px] font-semibold tracking-[0.24em] text-violet-300/70">PLAYER · NATIVE</p></div>
          </a>
          <nav className="flex items-center gap-1 sm:gap-3" aria-label="Ana navigasyon">
            <a href="#features" className="hidden rounded-lg px-3 py-2 text-sm text-slate-300 transition hover:bg-white/5 hover:text-white sm:block">Özellikler</a>
            <a href="/privacy.html" className="hidden rounded-lg px-3 py-2 text-sm text-slate-300 transition hover:bg-white/5 hover:text-white sm:block">Gizlilik</a>
            <a href={repositoryUrl} target="_blank" rel="noreferrer" className="rounded-lg border border-white/10 bg-white/5 px-3 py-2 text-sm text-slate-200 transition hover:border-violet-400/40 hover:bg-white/10">GitHub</a>
          </nav>
        </div>
      </header>

      <section id="top" className="relative mx-auto grid max-w-6xl items-center gap-14 px-5 pb-24 pt-20 sm:px-8 lg:grid-cols-[1.08fr_.92fr] lg:pb-30 lg:pt-28">
        <div>
          <div className="mb-7 inline-flex items-center gap-2 rounded-full border border-violet-400/20 bg-violet-400/8 px-3.5 py-2 text-xs font-semibold text-violet-200">
            <span className="relative flex size-2"><span className="absolute inline-flex size-full animate-ping rounded-full bg-emerald-400 opacity-70" /><span className="relative inline-flex size-2 rounded-full bg-emerald-400" /></span>
            Windows 10 ve 11 için hazır
          </div>
          <h1 className="max-w-3xl text-balance text-5xl font-bold leading-[1.03] tracking-[-0.045em] text-white sm:text-6xl lg:text-7xl">Listeni ekle.<br />Yayınını <span className="gradient-text">özgürce izle.</span></h1>
          <p className="mt-7 max-w-xl text-pretty text-base leading-7 text-slate-400 sm:text-lg">BG IPTV Player, M3U ve IPTV listelerini hızlı, düzenli ve tamamen uygulama içinde oynatmak için geliştirilmiş modern bir Windows masaüstü uygulamasıdır.</p>
          <div className="mt-9 flex flex-col gap-3 sm:flex-row">
            <a href={releaseUrl} className={cn(buttonVariants({ size: 'lg' }), 'h-12 rounded-xl bg-violet-600 px-6 text-white shadow-[0_12px_40px_rgba(109,93,252,.3)] hover:bg-violet-500')}><Download className="size-4.5" /> Windows için indir</a>
            <a href={repositoryUrl} target="_blank" rel="noreferrer" className={cn(buttonVariants({ variant: 'outline', size: 'lg' }), 'h-12 rounded-xl border-white/12 bg-white/4 px-6 text-slate-200 hover:bg-white/8')}><Code2 className="size-4.5" /> Kaynak kodu</a>
          </div>
          <div className="mt-7 flex flex-wrap gap-x-5 gap-y-2 text-xs text-slate-500">
            {['Ücretsiz ve açık kaynak', 'Kurulabilir veya taşınabilir', 'VLC ayrıca gerekmez'].map((item) => <span key={item} className="flex items-center gap-1.5"><CheckCircle2 className="size-3.5 text-emerald-400" />{item}</span>)}
          </div>
        </div>

        <div className="relative mx-auto w-full max-w-[480px] lg:mx-0">
          <div className="absolute -inset-8 rounded-full bg-violet-600/15 blur-3xl" aria-hidden="true" />
          <div className="product-card relative overflow-hidden rounded-[28px] border border-white/10 bg-[#0e121c]/90 p-5 shadow-2xl shadow-black/40">
            <div className="flex items-center justify-between border-b border-white/7 pb-4">
              <div className="flex items-center gap-3"><img src="/app-icon.png" width="48" height="48" alt="BG IPTV Player uygulama ikonu" className="rounded-2xl" /><div><p className="font-bold text-white">BG IPTV Player</p><p className="text-[10px] tracking-widest text-slate-500">NATIVE WINDOWS PLAYER</p></div></div>
              <span className="rounded-full bg-emerald-400/10 px-3 py-1.5 text-[10px] font-bold text-emerald-300">v1.0.2</span>
            </div>
            <div className="grid grid-cols-3 gap-2 py-5">
              {['CANLI TV', 'FİLMLER', 'DİZİLER'].map((item, index) => <div key={item} className={cn('rounded-xl border px-2 py-3 text-center text-[10px] font-bold', index === 0 ? 'border-violet-400/30 bg-violet-500/15 text-violet-200' : 'border-white/7 bg-white/[.025] text-slate-500')}>{item}</div>)}
            </div>
            <div className="space-y-2.5">
              {[
                ['TR', 'Kanal grupları', 'Düzenli'],
                ['4K', 'Uygulama içi oynatma', 'Hazır'],
                ['M3U', 'URL ve dosya desteği', 'Aktif'],
              ].map(([badge, title, state]) => <div key={title} className="flex items-center gap-3 rounded-xl border border-white/6 bg-black/15 p-3"><span className="grid size-10 place-items-center rounded-xl bg-gradient-to-br from-violet-500/30 to-blue-500/15 text-[11px] font-bold text-violet-100">{badge}</span><span className="flex-1 text-sm font-medium text-slate-200">{title}</span><span className="text-[10px] font-semibold text-emerald-400">{state}</span></div>)}
            </div>
            <div className="mt-5 flex items-center justify-between rounded-2xl border border-violet-400/15 bg-violet-500/[.07] px-4 py-3.5">
              <div><p className="text-xs font-semibold text-white">Yerel, hızlı ve sade</p><p className="mt-1 text-[10px] text-slate-500">Listelerin yalnızca senin bilgisayarında</p></div>
              <span className="grid size-9 place-items-center rounded-full bg-violet-500 text-white"><ArrowRight className="size-4" /></span>
            </div>
          </div>
        </div>
      </section>

      <section id="features" className="relative border-y border-white/7 bg-white/[.018] py-24">
        <div className="mx-auto max-w-6xl px-5 sm:px-8">
          <div className="max-w-2xl"><p className="eyebrow">HER ŞEY TEK UYGULAMADA</p><h2 className="mt-4 text-3xl font-bold tracking-tight text-white sm:text-4xl">İzlemeye odaklanan bir deneyim.</h2><p className="mt-4 leading-7 text-slate-400">Karmaşık ayarlar ve harici oynatıcılar olmadan listelerini yönet, ara ve izle.</p></div>
          <div className="mt-12 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {features.map(({ icon: Icon, title, text }) => <article key={title} className="feature-card rounded-2xl border border-white/7 bg-[#0d111a]/75 p-6"><span className="mb-5 grid size-11 place-items-center rounded-xl border border-violet-400/15 bg-violet-500/10 text-violet-300"><Icon className="size-5" /></span><h3 className="font-semibold text-white">{title}</h3><p className="mt-2 text-sm leading-6 text-slate-500">{text}</p></article>)}
          </div>
        </div>
      </section>

      <section className="mx-auto max-w-6xl px-5 py-24 sm:px-8">
        <div className="overflow-hidden rounded-[28px] border border-violet-400/15 bg-gradient-to-br from-violet-500/12 via-[#111522] to-blue-500/8 p-8 sm:p-12">
          <div className="flex flex-col justify-between gap-8 md:flex-row md:items-end">
            <div className="max-w-2xl"><p className="eyebrow">GİZLİLİK ÖNCE GELİR</p><h2 className="mt-4 text-3xl font-bold text-white">Yayın listen sana aittir.</h2><p className="mt-4 leading-7 text-slate-400">BG IPTV Player hesap açtırmaz, reklam göstermez ve kullanım verisi toplamaz. Oynatma listelerin ile uygulama ayarların cihazında saklanır.</p></div>
            <a href="/privacy.html" className={cn(buttonVariants({ variant: 'outline', size: 'lg' }), 'h-11 rounded-xl border-white/12 bg-white/5 px-5 text-white hover:bg-white/10')}>Gizlilik politikasını oku <ArrowRight /></a>
          </div>
        </div>
      </section>

      <footer className="border-t border-white/7 py-9">
        <div className="mx-auto flex max-w-6xl flex-col gap-5 px-5 text-sm text-slate-500 sm:flex-row sm:items-center sm:justify-between sm:px-8">
          <div className="flex items-center gap-3"><img src="/app-icon.png" width="30" height="30" alt="" className="rounded-lg" /><span>© 2026 Berk Güçlükol · MIT Lisansı</span></div>
          <div className="flex gap-5"><a href="/privacy.html" className="transition hover:text-white">Gizlilik</a><a href={repositoryUrl} className="transition hover:text-white">GitHub</a><a href={`${repositoryUrl}/issues`} className="transition hover:text-white">Destek</a></div>
        </div>
      </footer>
    </main>
  );
}
