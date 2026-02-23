"use client";

import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { cn } from '@/lib/cn';

export function Header() {
  const pathname = usePathname();

  return (
    <header className="sticky top-0 z-10 border-b border-gray-200 bg-white">
      <div className="flex h-16 items-center justify-between px-4 sm:px-6 lg:px-8">
        <div className="flex items-center gap-4">
          <Link href="/" className="flex items-center gap-2">
            <div className="h-8 w-8 rounded-lg bg-primary-600 flex items-center justify-center">
              <span className="text-white font-bold text-sm">RT</span>
            </div>
            <span className="text-lg font-semibold text-gray-900">RithmTemplate</span>
          </Link>
        </div>

        <nav className="hidden md:flex items-center gap-6">
          <Link
            href="/"
            className={cn(
              "text-sm font-medium transition-colors",
              pathname === '/' ? 'text-gray-900' : 'text-gray-600 hover:text-gray-900'
            )}
          >
            Home
          </Link>
          <Link
            href="/sample-entities"
            className={cn(
              "text-sm font-medium transition-colors",
              pathname === '/sample-entities' ? 'text-gray-900' : 'text-gray-600 hover:text-gray-900'
            )}
          >
            Sample Entities
          </Link>
        </nav>

        <div className="flex items-center gap-4">
          <button className="rounded-full bg-gray-100 p-2 text-gray-600 hover:bg-gray-200 transition-colors">
            <svg
              xmlns="http://www.w3.org/2000/svg"
              fill="none"
              viewBox="0 0 24 24"
              strokeWidth={1.5}
              stroke="currentColor"
              className="h-5 w-5"
            >
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                d="M15.75 6a3.75 3.75 0 11-7.5 0 3.75 3.75 0 017.5 0zM4.501 20.118a7.5 7.5 0 0114.998 0A17.933 17.933 0 0112 21.75c-2.676 0-5.216-.584-7.499-1.632z"
              />
            </svg>
          </button>
        </div>
      </div>
    </header>
  );
}
