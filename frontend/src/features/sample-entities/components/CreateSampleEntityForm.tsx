"use client";

import { useForm } from 'react-hook-form';
import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Input';
import type { CreateSampleEntityRequest } from '../types/sample-entity.types';

interface CreateSampleEntityFormProps {
  onSubmit: (data: CreateSampleEntityRequest) => void | Promise<void>;
  isLoading?: boolean;
  onCancel?: () => void;
  error?: string;
}

export function CreateSampleEntityForm({
  onSubmit,
  isLoading,
  onCancel,
  error,
}: CreateSampleEntityFormProps) {
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<CreateSampleEntityRequest>({
    defaultValues: {
      name: '',
      description: '',
      priority: 5,
    },
  });

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="space-y-4">
      {error && (
        <div className="rounded-md bg-red-50 p-3 text-sm text-red-700">
          {error}
        </div>
      )}

      <Input
        label="Name"
        placeholder="Enter entity name"
        error={errors.name?.message}
        {...register('name', { required: 'Name is required' })}
      />

      <Input
        label="Description"
        placeholder="Enter description (optional)"
        {...register('description')}
      />

      <Input
        label="Priority"
        type="number"
        placeholder="1-10"
        error={errors.priority?.message}
        {...register('priority', {
          valueAsNumber: true,
          min: { value: 1, message: 'Minimum priority is 1' },
          max: { value: 10, message: 'Maximum priority is 10' },
        })}
      />

      <div className="flex justify-end gap-3 pt-4">
        {onCancel && (
          <Button type="button" variant="outline" onClick={onCancel}>
            Cancel
          </Button>
        )}
        <Button type="submit" isLoading={isLoading}>
          Create Entity
        </Button>
      </div>
    </form>
  );
}
