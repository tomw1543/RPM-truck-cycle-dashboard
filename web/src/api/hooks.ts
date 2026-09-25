import { useQuery } from '@tanstack/react-query'
import { ApiError, apiGet } from './client'
import type { Envelope, FleetSummary, FleetSummaryParams, Meta, RoutesListData, TruckDetailData, TrucksListData, TruckWindowParams } from './types'

/** Matches the server's 30s output-cache policy on /api/meta and /api/fleet/summary
 * (DataEndpoints in Program.cs) - polling faster wouldn't see fresher data anyway. */
const LIVE_REFETCH_MS = 30_000

export function useMeta(live: boolean) {
  return useQuery({
    queryKey: ['meta'],
    queryFn: () => apiGet<Envelope<Meta>>('/api/meta'),
    refetchInterval: live ? LIVE_REFETCH_MS : false,
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
