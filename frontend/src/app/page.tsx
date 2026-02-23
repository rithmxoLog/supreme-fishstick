import Link from 'next/link';
import { Card, CardHeader, CardTitle, CardDescription, CardContent } from '@/components/ui/Card';

export default function HomePage() {
  return (
    <div className="space-y-8">
      <div className="text-center">
        <h1 className="text-4xl font-bold tracking-tight text-gray-900 sm:text-5xl">
          RithmTemplate
        </h1>
        <p className="mt-4 text-lg text-gray-600">
          A modern Next.js frontend template with TanStack Query, Tailwind CSS, and TypeScript.
        </p>
      </div>

      <div className="grid gap-6 md:grid-cols-2 lg:grid-cols-3">
        <Card>
          <CardHeader>
            <CardTitle>TanStack Query</CardTitle>
            <CardDescription>
              Powerful server state management with automatic caching and updates.
            </CardDescription>
          </CardHeader>
          <CardContent>
            <ul className="list-disc list-inside text-sm text-gray-600 space-y-1">
              <li>Automatic background refetching</li>
              <li>Built-in caching</li>
              <li>Optimistic updates</li>
              <li>DevTools included</li>
            </ul>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Vertical Slices</CardTitle>
            <CardDescription>
              Feature-based architecture that mirrors the backend structure.
            </CardDescription>
          </CardHeader>
          <CardContent>
            <ul className="list-disc list-inside text-sm text-gray-600 space-y-1">
              <li>Self-contained features</li>
              <li>API, hooks, and components together</li>
              <li>Easy to maintain and scale</li>
              <li>Clear public API via index.ts</li>
            </ul>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Smart + Dumb</CardTitle>
            <CardDescription>
              Clean separation between logic (hooks) and presentation (components).
            </CardDescription>
          </CardHeader>
          <CardContent>
            <ul className="list-disc list-inside text-sm text-gray-600 space-y-1">
              <li>Hooks contain business logic</li>
              <li>Components are pure UI</li>
              <li>Easy to test</li>
              <li>Highly reusable</li>
            </ul>
          </CardContent>
        </Card>
      </div>

      <div className="flex justify-center">
        <Link
          href="/sample-entities"
          className="inline-flex items-center justify-center rounded-md font-medium transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-offset-2 bg-primary-600 text-white hover:bg-primary-700 focus-visible:ring-primary-500 h-12 px-6 text-base"
        >
          View Sample Entities
        </Link>
      </div>
    </div>
  );
}
