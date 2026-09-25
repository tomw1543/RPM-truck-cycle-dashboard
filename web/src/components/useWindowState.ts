import { useSearchParams } from 'react-router'
import type { Shift } from '../api/types'

export interface WindowState {
  from?: string
  to?: string
  shift?: Shift
}

/** Reads/writes the window (from/to/shift) as URL search params, so the current view is
 * shareable and survives a refresh. No from/to in the URL = the server's rolling-7-day
 * default (RequestWindow.TryResolve). */
export function useWindowState(): [WindowState, (next: WindowState) => void] {
  const [searchParams, setSearchParams] = useSearchParams()

  const state: WindowState = {
    from: searchParams.get('from') ?? undefined,
    to: searchParams.get('to') ?? undefined,
    shift: (searchParams.get('shift') as Shift | null) ?? undefined,
  }

  const setState = (next: WindowState) => {
    const params = new URLSearchParams()
    if (next.from) params.set('from', next.from)
    if (next.to) params.set('to', next.to)
    if (next.shift) params.set('shift', next.shift)
    setSearchParams(params)
  }

  return [state, setState]
}
