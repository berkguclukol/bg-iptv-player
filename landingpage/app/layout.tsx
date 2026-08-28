import type { Metadata } from 'next';
import { Geist, Geist_Mono } from 'next/font/google';
import './globals.css';

const geistSans = Geist({
  variable: '--font-geist-sans',
  subsets: ['latin'],
});

const geistMono = Geist_Mono({
  variable: '--font-geist-mono',
  subsets: ['latin'],
});

export const metadata: Metadata = {
  title: 'BG IPTV Player | Modern Windows IPTV Oynatıcısı',
  description: 'M3U ve IPTV listelerini hızlı, düzenli ve uygulama içinde oynatmak için geliştirilmiş modern Windows masaüstü uygulaması.',
  icons: { icon: '/app-icon.png' },
  openGraph: {
    title: 'BG IPTV Player',
    description: 'Listeni ekle. Yayınını özgürce izle.',
    type: 'website',
  },
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="tr">
      <body
        className={`${geistSans.variable} ${geistMono.variable} antialiased`}
      >
        {children}
      </body>
    </html>
  );
}
