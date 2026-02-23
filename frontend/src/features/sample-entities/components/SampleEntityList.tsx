import { Spinner } from '@/components/ui/Spinner';
import { SampleEntityCard } from './SampleEntityCard';
import type { SampleEntity } from '../types/sample-entity.types';

interface SampleEntityListProps {
  entities: SampleEntity[];
  isLoading?: boolean;
  onSelect?: (entity: SampleEntity) => void;
}

export function SampleEntityList({ entities, isLoading, onSelect }: SampleEntityListProps) {
  if (isLoading) {
    return <Spinner className="py-8" />;
  }

  if (entities.length === 0) {
    return (
      <div className="text-center py-12">
        <svg
          className="mx-auto h-12 w-12 text-gray-400"
          fill="none"
          viewBox="0 0 24 24"
          stroke="currentColor"
        >
          <path
            strokeLinecap="round"
            strokeLinejoin="round"
            strokeWidth={2}
            d="M20 13V6a2 2 0 00-2-2H6a2 2 0 00-2 2v7m16 0v5a2 2 0 01-2 2H6a2 2 0 01-2-2v-5m16 0h-2.586a1 1 0 00-.707.293l-2.414 2.414a1 1 0 01-.707.293h-3.172a1 1 0 01-.707-.293l-2.414-2.414A1 1 0 006.586 13H4"
          />
        </svg>
        <h3 className="mt-2 text-sm font-medium text-gray-900">No entities found</h3>
        <p className="mt-1 text-sm text-gray-500">
          Get started by creating a new sample entity.
        </p>
      </div>
    );
  }

  return (
    <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
      {entities.map((entity) => (
        <SampleEntityCard
          key={entity.id}
          entity={entity}
          onClick={() => onSelect?.(entity)}
        />
      ))}
    </div>
  );
}
