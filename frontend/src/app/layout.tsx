import type { Metadata } from 'next';
import { QueryProvider } from '@/providers/QueryProvider';
import { Header } from '@/components/layout/Header';
import './globals.css';

export const metadata: Metadata = {
  title: 'RithmTemplate',
  description: 'RithmXO Template Service Frontend',
};

export default function RootLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <html lang="en">
      <body>
        <QueryProvider>
          <div className="min-h-screen bg-gray-50">
            <Header />
            <main className="mx-auto max-w-7xl px-4 py-8 sm:px-6 lg:px-8">
              {children}
            </main>
          </div>
        </QueryProvider>
      </body>
    </html>
  );
}
