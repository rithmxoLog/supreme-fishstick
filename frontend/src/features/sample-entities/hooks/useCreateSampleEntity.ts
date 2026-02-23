import { useMutation, useQueryClient } from '@tanstack/react-query';
import { createSampleEntity } from '../api/create-sample-entity';
import { sampleEntitiesKeys } from './useSampleEntityList';
import type { CreateSampleEntityRequest } from '../types/sample-entity.types';

export function useCreateSampleEntity() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (request: CreateSampleEntityRequest) => createSampleEntity(request),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: sampleEntitiesKeys.lists() });
    },
  });
}
