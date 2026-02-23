import { useQuery } from '@tanstack/react-query';
import { getSampleEntity } from '../api/get-sample-entity';
import { sampleEntitiesKeys } from './useSampleEntityList';

export function useSampleEntity(id: string) {
  return useQuery({
    queryKey: sampleEntitiesKeys.detail(id),
    queryFn: () => getSampleEntity(id),
    enabled: !!id,
  });
}
