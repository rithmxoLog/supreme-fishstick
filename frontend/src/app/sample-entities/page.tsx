"use client";

import { useState } from 'react';
import { Button } from '@/components/ui/Button';
import { Card, CardHeader, CardTitle, CardContent } from '@/components/ui/Card';
import {
  useSampleEntityList,
  useCreateSampleEntity,
  SampleEntityList,
  CreateSampleEntityForm,
  type SampleEntity,
  type CreateSampleEntityRequest,
} from '@/features/sample-entities';

export default function SampleEntitiesPage() {
  const [showCreateForm, setShowCreateForm] = useState(false);
  const [page, setPage] = useState(1);
  const pageSize = 9;

  const { data, isLoading, isError, error } = useSampleEntityList({
    page,
    pageSize,
  });

  const createMutation = useCreateSampleEntity();

  const handleSelectEntity = (entity: SampleEntity) => {
    console.log('Selected entity:', entity);
  };

  const handleCreateSubmit = async (formData: CreateSampleEntityRequest) => {
    await createMutation.mutateAsync(formData);
    setShowCreateForm(false);
  };

  if (isError) {
    return (
      <div className="text-center py-12">
        <h2 className="text-lg font-semibold text-red-600">Error loading entities</h2>
        <p className="text-sm text-gray-500 mt-2">
          {error instanceof Error ? error.message : 'An unexpected error occurred'}
        </p>
        <Button className="mt-4" onClick={() => window.location.reload()}>
          Retry
        </Button>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Sample Entities</h1>
          <p className="text-sm text-gray-500 mt-1">
            Manage your sample entities here.
          </p>
        </div>
        <Button onClick={() => setShowCreateForm(!showCreateForm)}>
          {showCreateForm ? 'Cancel' : 'Create New'}
        </Button>
      </div>

      {showCreateForm && (
        <Card>
          <CardHeader>
            <CardTitle>Create New Entity</CardTitle>
          </CardHeader>
          <CardContent>
            <CreateSampleEntityForm
              onSubmit={handleCreateSubmit}
              isLoading={createMutation.isPending}
              error={createMutation.isError ? 'Failed to create entity. Please try again.' : undefined}
              onCancel={() => setShowCreateForm(false)}
            />
          </CardContent>
        </Card>
      )}

      <SampleEntityList
        entities={data?.items ?? []}
        isLoading={isLoading}
        onSelect={handleSelectEntity}
      />

      {data && data.totalPages > 1 && (
        <div className="flex items-center justify-between border-t border-gray-200 pt-4">
          <div className="text-sm text-gray-500">
            Showing {((page - 1) * pageSize) + 1} to {Math.min(page * pageSize, data.totalCount)} of {data.totalCount} results
          </div>
          <div className="flex gap-2">
            <Button
              variant="outline"
              size="sm"
              disabled={page === 1}
              onClick={() => setPage((p) => p - 1)}
            >
              Previous
            </Button>
            <Button
              variant="outline"
              size="sm"
              disabled={page === data.totalPages}
              onClick={() => setPage((p) => p + 1)}
            >
              Next
            </Button>
          </div>
        </div>
      )}
    </div>
  );
}
