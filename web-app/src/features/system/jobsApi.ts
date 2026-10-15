// Datos de «Trabajos programados» (Sistema, Lote 50; permiso jobs.manage, módulo SYSTEM): lista, interruptor y horario, «Ejecutar
// ahora» e historial. Mientras algún trabajo corre o espera su turno la lista se vuelve a pedir cada 5 s para ver el resultado.
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components } from '../../kernel/api/schema'

type Schemas = components['schemas']
export type ScheduledJobDto = Schemas['ScheduledJobDto']
export type ScheduledJobRequest = Schemas['ScheduledJobRequest']
export type ScheduledJobRunDto = Schemas['ScheduledJobRunDto']

export const jobsKeys = {
  all: ['jobs'] as const,
  list: ['jobs', '/api/v1/jobs'] as const,
  runs: (key: string) => ['jobs', '/api/v1/jobs/{key}/runs', key] as const,
}

export function useJobs() {
  return useQuery({
    queryKey: jobsKeys.list,
    queryFn: () => unwrap(api.GET('/api/v1/jobs')),
    refetchInterval: (q) => (q.state.data?.some((j) => j.isRunning || j.manualRequested) ? 5000 : false),
  })
}

export function useJobRuns(key: string | null) {
  return useQuery({
    queryKey: jobsKeys.runs(key ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/jobs/{key}/runs', { params: { path: { key: key! }, query: { take: 50 } } })),
    enabled: key !== null,
  })
}

export function useJobMutations() {
  const qc = useQueryClient()
  const invalidate = () => qc.invalidateQueries({ queryKey: jobsKeys.all })
  return {
    update: useMutation({
      mutationFn: (v: { key: string; body: ScheduledJobRequest }) => unwrap(api.PATCH('/api/v1/jobs/{key}', { params: { path: { key: v.key } }, body: v.body })),
      onSettled: invalidate,
    }),
    runNow: useMutation({
      mutationFn: (key: string) => unwrap(api.POST('/api/v1/jobs/{key}/run', { params: { path: { key } } })),
      onSettled: invalidate,
    }),
  }
}
