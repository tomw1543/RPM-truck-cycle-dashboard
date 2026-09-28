import { useQuery } from '@tanstack/react-query'
import { ApiError, apiGet } from './client'
import type { BottlenecksData, Envelope, FleetSummary, FleetSummaryParams, LoadersData, LoaderShiftQueueData, Meta, OptimiserShiftDetailData, OptimiserSummaryData, RoutesListData, ScheduleComplianceData, TruckDetailData, TrucksListData, TruckWindowParams } from './types'

/** Matches the server's 30s output-cache policy on /api/meta and /api/fleet/summary
 * (DataEndpoints in Program.cs) - polling faster wouldn't see fresher data anyway. */
const LIVE_REFETCH_MS = 30_000

/** A paused Azure SQL database can take about a minute to resume, and requests fail while
 * it does. Meta is the first request of a session, so it keeps retrying for ~2 minutes
 * behind the full-page loading screen. 4xx responses won't fix themselves, so they don't retry. */
const META_MAX_RETRIES = 12
const META_RETRY_DELAY_MS = 10_000

export function useMeta(live: boolean) {
  return useQuery({
    queryKey: ['meta'],
    queryFn: () => apiGet<Envelope<Meta>>('/api/meta'),
    refetchInterval: live ? LIVE_REFETCH_MS : false,
    retry: (failureCount, error) =>
      !(error instanceof ApiError && error.status >= 400 && error.status < 500) && failureCount < META_MAX_RETRIES,
    retryDelay: META_RETRY_DELAY_MS,
  })
}

export function useFleetSummary(params: FleetSummaryParams, live: boolean) {
  return useQuery({
    queryKey: ['fleet-summary', params],
    queryFn: () =>
      apiGet<Envelope<FleetSummary>>('/api/fleet/summary', {
        from: params.from,
        to: params.to,
        shift: params.shift,
      }),
    refetchInterval: live ? LIVE_REFETCH_MS : false,
  })
}

export function useTrucks(params: TruckWindowParams, live: boolean) {
  return useQuery({
    queryKey: ['trucks', params],
    queryFn: () =>
      apiGet<Envelope<TrucksListData>>('/api/trucks', {
        from: params.from,
        to: params.to,
        shift: params.shift,
      }),
    refetchInterval: live ? LIVE_REFETCH_MS : false,
  })
}

export function useRoutes(params: TruckWindowParams, live: boolean) {
  return useQuery({
    queryKey: ['routes', params],
    queryFn: () =>
      apiGet<Envelope<RoutesListData>>('/api/routes', {
        from: params.from,
        to: params.to,
        shift: params.shift,
      }),
    refetchInterval: live ? LIVE_REFETCH_MS : false,
  })
}

export function useBottlenecks(params: TruckWindowParams, live: boolean) {
  return useQuery({
    queryKey: ['bottlenecks', params],
    queryFn: () =>
      apiGet<Envelope<BottlenecksData>>('/api/bottlenecks', {
        from: params.from,
        to: params.to,
        shift: params.shift,
      }),
    refetchInterval: live ? LIVE_REFETCH_MS : false,
  })
}

export function useScheduleCompliance(params: TruckWindowParams, live: boolean) {
  return useQuery({
    queryKey: ['schedule-compliance', params],
    queryFn: () =>
      apiGet<Envelope<ScheduleComplianceData>>('/api/schedule/compliance', {
        from: params.from,
        to: params.to,
        shift: params.shift,
      }),
    refetchInterval: live ? LIVE_REFETCH_MS : false,
  })
}

export function useLoaders(params: TruckWindowParams, live: boolean) {
  return useQuery({
    queryKey: ['loaders', params],
    queryFn: () =>
      apiGet<Envelope<LoadersData>>('/api/loaders', {
        from: params.from,
        to: params.to,
        shift: params.shift,
      }),
    refetchInterval: live ? LIVE_REFETCH_MS : false,
  })
}

export function useOptimiserSummary(params: TruckWindowParams, live: boolean) {
  return useQuery({
    queryKey: ['optimiser-summary', params],
    queryFn: () =>
      apiGet<Envelope<OptimiserSummaryData>>('/api/optimiser/summary', {
        from: params.from,
        to: params.to,
        shift: params.shift,
      }),
    refetchInterval: live ? LIVE_REFETCH_MS : false,
  })
}

/** Disabled until both a shift date and name are provided. A 404 means no cycles were
 * recorded for that shift. Same retry convention as useOptimiserShift. */
export function useLoaderShiftQueue(shiftDate: string | undefined, shiftName: string | undefined, live: boolean) {
  return useQuery({
    queryKey: ['loaderShiftQueue', shiftDate, shiftName, live],
    queryFn: () => apiGet<LoaderShiftQueueData>(`/api/loaders/shifts/${shiftDate}/${shiftName}`),
    enabled: Boolean(shiftDate && shiftName),
    refetchInterval: live ? LIVE_REFETCH_MS : false,
    retry: (failureCount, error) => !(error instanceof ApiError && error.status === 404) && failureCount < 3,
  })
}

/** Disabled until both a shift date and name are picked - the shift picker/preselection sets
 * these once the page has something to select. A 404 (not optimised yet) won't resolve on
 * retry, same convention as useTruckDetail's unknown-truck-name case. */
export function useOptimiserShift(shiftDate: string | undefined, shiftName: string | undefined, live: boolean) {
  return useQuery({
    queryKey: ['optimiser-shift', shiftDate, shiftName],
    queryFn: () => apiGet<Envelope<OptimiserShiftDetailData>>(`/api/optimiser/shifts/${shiftDate}/${shiftName}`),
    enabled: Boolean(shiftDate && shiftName),
    refetchInterval: live ? LIVE_REFETCH_MS : false,
    retry: (failureCount, error) => !(error instanceof ApiError && error.status === 404) && failureCount < 3,
  })
}

export function useTruckDetail(name: string, params: TruckWindowParams, live: boolean) {
  return useQuery({
    queryKey: ['truck-detail', name, params],
    queryFn: () =>
      apiGet<Envelope<TruckDetailData>>(`/api/trucks/${encodeURIComponent(name)}`, {
        from: params.from,
        to: params.to,
        shift: params.shift,
      }),
    refetchInterval: live ? LIVE_REFETCH_MS : false,
    // A 404 (unknown truck name) won't resolve on retry - don't burn requests on it.
    retry: (failureCount, error) => !(error instanceof ApiError && error.status === 404) && failureCount < 3,
  })
}
