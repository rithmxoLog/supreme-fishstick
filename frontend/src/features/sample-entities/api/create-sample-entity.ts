import { api } from '@/lib/axios';
import type { CreateSampleEntityRequest } from '../types/sample-entity.types';

export interface CreateSampleEntityResponse {
  success: boolean;
  message: string;
  data: {
    id: string;
  };
}

export async function createSampleEntity(request: CreateSampleEntityRequest): Promise<CreateSampleEntityResponse> {
  const { data } = await api.post<CreateSampleEntityResponse>('/sample-entities', request);
  return data;
}
