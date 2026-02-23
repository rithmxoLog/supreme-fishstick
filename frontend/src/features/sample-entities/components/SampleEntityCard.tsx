import { Card, CardHeader, CardTitle, CardDescription, CardContent, CardFooter } from '@/components/ui/Card';
import type { SampleEntity } from '../types/sample-entity.types';

interface SampleEntityCardProps {
  entity: SampleEntity;
  onClick?: () => void;
}

const statusColors: Record<string, string> = {
  Draft: 'bg-gray-100 text-gray-700',
  Active: 'bg-green-100 text-green-700',
  Inactive: 'bg-yellow-100 text-yellow-700',
  Archived: 'bg-red-100 text-red-700',
};

export function SampleEntityCard({ entity, onClick }: SampleEntityCardProps) {
  return (
    <Card
      className="cursor-pointer transition-shadow hover:shadow-md"
      onClick={onClick}
    >
      <CardHeader>
        <div className="flex items-center justify-between">
          <CardTitle>{entity.name}</CardTitle>
          <span
            className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ${statusColors[entity.status] || 'bg-gray-100 text-gray-700'}`}
          >
            {entity.status}
          </span>
        </div>
        {entity.description && (
          <CardDescription>{entity.description}</CardDescription>
        )}
      </CardHeader>
      <CardContent>
        <div className="grid grid-cols-2 gap-4 text-sm">
          <div>
            <span className="text-gray-500">Priority:</span>
            <span className="ml-2 font-medium">{entity.priority}</span>
          </div>
          <div>
            <span className="text-gray-500">Items:</span>
            <span className="ml-2 font-medium">{entity.itemCount}</span>
          </div>
        </div>
      </CardContent>
      <CardFooter className="text-xs text-gray-500">
        Created: {new Date(entity.createdAt).toLocaleDateString()}
      </CardFooter>
    </Card>
  );
}
