import { useQuery } from '@tanstack/react-query';
import { getSampleEntities } from '../api/get-sample-entities';
import type { PaginationParams } from '../types/sample-entity.types';

export const sampleEntitiesKeys = {
  all: ['sample-entities'] as const,
  lists: () => [...sampleEntitiesKeys.all, 'list'] as const,
  list: (params: PaginationParams) => [...sampleEntitiesKeys.lists(), params] as const,
  details: () => [...sampleEntitiesKeys.all, 'detail'] as const,
  detail: (id: string) => [...sampleEntitiesKeys.details(), id] as const,
};

export function useSampleEntityList(params: PaginationParams = {}) {
  return useQuery({
    queryKey: sampleEntitiesKeys.list(params),
    queryFn: () => getSampleEntities(params),
  });
}
