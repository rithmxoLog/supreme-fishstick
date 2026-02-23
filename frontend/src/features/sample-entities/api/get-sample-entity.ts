import { api } from '@/lib/axios';
import type { SampleEntity } from '../types/sample-entity.types';

export async function getSampleEntity(id: string): Promise<SampleEntity> {
  const { data } = await api.get<SampleEntity>(`/sample-entities/${id}`);
  return data;
}
