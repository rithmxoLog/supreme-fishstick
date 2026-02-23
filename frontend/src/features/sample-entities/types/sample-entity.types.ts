export interface SampleEntity {
  id: string;
  rithmId?: string;
  organizationId: string;
  name: string;
  description?: string;
  status: SampleStatus;
  configurationJson?: string;
  priority: number;
  itemCount: number;
  createdAt: string;
  createdBy?: string;
  updatedAt?: string;
  updatedBy?: string;
}

export type SampleStatus = 'Draft' | 'Active' | 'Inactive' | 'Archived';

export interface SampleItem {
  id: string;
  sampleEntityId: string;
  value: string;
  order: number;
}

export interface CreateSampleEntityRequest {
  name: string;
  description?: string;
  priority?: number;
}

export interface UpdateSampleEntityRequest {
  name?: string;
  description?: string;
  status?: SampleStatus;
  priority?: number;
}

export interface PaginatedResponse<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface PaginationParams {
  page?: number;
  pageSize?: number;
  sortBy?: string;
  sortDirection?: 'asc' | 'desc';
}
