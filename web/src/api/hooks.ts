import { useQuery } from '@tanstack/react-query'
import { apiGet } from './client'
import type { Envelope, FleetSummary, FleetSummaryParams, Meta } from './types'

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
