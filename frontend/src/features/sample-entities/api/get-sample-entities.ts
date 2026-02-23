import { api } from '@/lib/axios';
import type { SampleEntity, PaginatedResponse, PaginationParams } from '../types/sample-entity.types';

export async function getSampleEntities(params: PaginationParams = {}): Promise<PaginatedResponse<SampleEntity>> {
  const { data } = await api.get<PaginatedResponse<SampleEntity>>('/sample-entities', {
    params: {
      page: params.page ?? 1,
      pageSize: params.pageSize ?? 10,
      sortBy: params.sortBy,
      sortDirection: params.sortDirection,
    },
  });
  return data;
}
