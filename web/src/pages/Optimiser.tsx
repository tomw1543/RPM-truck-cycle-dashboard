import { useEffect, useState } from 'react'
import { useSearchParams } from 'react-router'
import { ApiError } from '../api/client'
import { useMeta, useOptimiserShift, useOptimiserSummary } from '../api/hooks'
import type { OptimiserPlan, OptimiserPlanType } from '../api/types'
import { OptimiserActualComparison } from '../components/OptimiserActualComparison'
import { OptimiserHeadlineTiles } from '../components/OptimiserHeadlineTiles'
import { OptimiserLoaderComparisonTable } from '../components/OptimiserLoaderComparisonTable'
import { OptimiserMovesList } from '../components/OptimiserMovesList'
import { OptimiserOutcomeComparison } from '../components/OptimiserOutcomeComparison'
import { OptimiserPlanSelector } from '../components/OptimiserPlanSelector'
import { OptimiserShiftPicker } from '../components/OptimiserShiftPicker'
import { StatusBanner } from '../components/StatusBanner'
import { WindowBar } from '../components/WindowBar'
import { useWindowState } from '../components/useWindowState'

interface OptimiserProps {
  live: boolean
}

const PLAN_LABELS: Record<OptimiserPlanType, string> = {
  Original: 'Original',
  MoreOutput: 'More output',
  Leaner: 'Leaner',
}

function planFor(planType: OptimiserPlanType, data: { original: OptimiserPlan; moreOutput: OptimiserPlan; leaner: OptimiserPlan }): OptimiserPlan {
  if (planType === 'MoreOutput') return data.moreOutput
  if (planType === 'Leaner') return data.leaner
  return data.original
}

export function Optimiser({ live }: OptimiserProps) {
  const [windowState, setWindowState] = useWindowState()
  const [searchParams, setSearchParams] = useSearchParams()
  const meta = useMeta(live)
  const summary = useOptimiserSummary(windowState, live)

  // This page defaults to the WHOLE optimised period, not the site-wide rolling 7 days every
  // other page uses (useWindowState's own default, unchanged there) - the headline is only
  // meaningful over the period --optimise actually covered. Once meta resolves, if the URL has
  // no explicit from/to yet, fill them in with the full range; the WindowBar then shows this as
  // an ordinary pre-filled "Custom range", not a new UI mode, so no change to WindowBar itself
  // was needed. This does mean the very first fetch (before meta resolves) briefly uses the
  // site's normal 7-day default and a second fetch follows once the effect runs - a minor,
  // one-time double-fetch, not visible behind App.tsx's full-page loading state for the first
  // page load of a session.
  useEffect(() => {
    if (!windowState.from && !windowState.to && meta.data?.from && meta.data?.to) {
      setWindowState({ from: meta.data.from, to: meta.data.to, shift: windowState.shift })
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [meta.data?.from, meta.data?.to])

  const bounds = meta.data ? { first: meta.data.from, last: meta.data.to } : undefined

  const selectedShiftDate = searchParams.get('shiftDate') ?? undefined
  const selectedShiftName = searchParams.get('shiftName') ?? undefined

  const selectShift = (shiftDate: string, shiftName: string) => {
    const next = new URLSearchParams(searchParams)
    next.set('shiftDate', shiftDate)
    next.set('shiftName', shiftName)
    setSearchParams(next, { replace: true })
  }

  // No shift preselected (e.g. arriving at /optimiser with no query params) - default to the
  // newest optimised shift in the summary's list, so the page never opens on an empty state.
  useEffect(() => {
    if (!selectedShiftDate && !selectedShiftName) {
      const first = summary.data?.data.shifts.find((s) => s.hasResults)
      if (first) selectShift(first.shiftDate, first.shiftName)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [summary.data])

  const [planType, setPlanType] = useState<OptimiserPlanType>('MoreOutput')

  const shiftDetail = useOptimiserShift(selectedShiftDate, selectedShiftName, live)
  const notOptimised = shiftDetail.error instanceof ApiError && shiftDetail.error.status === 404

  return (
    <div className="mx-auto flex max-w-7xl flex-col gap-4 px-4 py-6">
      <WindowBar state={windowState} onChange={setWindowState} bounds={bounds} />

      <p className="text-xs text-slate-500">
        Every shift here has already been optimised offline by <code>--optimise</code>: local search over single-truck
        moves and swaps, judged by replaying each candidate plan, never guessed. This page only reads the results back.
      </p>

      {summary.isLoading && <StatusBanner kind="loading" title="Loading optimiser summary…" />}

      {summary.isError && (
        <StatusBanner
          kind="error"
          title={summary.error instanceof ApiError ? summary.error.title : 'Failed to load optimiser summary'}
          detail={summary.error instanceof ApiError ? summary.error.detail : undefined}
        />
      )}

      {summary.data &&
        (summary.data.asOf === null ? (
          <StatusBanner kind="empty" title="No data in this window." detail="Try a different date range, or run the data generator." />
        ) : (
          <>
            <OptimiserHeadlineTiles
              shiftsOptimised={summary.data.data.shiftsOptimised}
              shiftsNotOptimised={summary.data.data.shiftsNotOptimised}
              moreOutput={summary.data.data.moreOutput}
              leaner={summary.data.data.leaner}
            />

            <OptimiserShiftPicker
              shifts={summary.data.data.shifts}
              selected={selectedShiftDate && selectedShiftName ? { shiftDate: selectedShiftDate, shiftName: selectedShiftName } : undefined}
              onSelect={selectShift}
            />

            {!selectedShiftDate || !selectedShiftName ? (
              <StatusBanner kind="empty" title="No optimised shifts in this window." detail="Run --optimise, or widen the date range." />
            ) : shiftDetail.isLoading ? (
              <StatusBanner kind="loading" title={`Loading ${selectedShiftDate} ${selectedShiftName}…`} />
            ) : notOptimised ? (
              <StatusBanner
                kind="empty"
                title="Not optimised yet (run --optimise)"
                detail={`${selectedShiftDate} ${selectedShiftName} has no optimiser results.`}
              />
            ) : shiftDetail.isError ? (
              <StatusBanner
                kind="error"
                title={shiftDetail.error instanceof ApiError ? shiftDetail.error.title : 'Failed to load this shift'}
                detail={shiftDetail.error instanceof ApiError ? shiftDetail.error.detail : undefined}
              />
            ) : (
              shiftDetail.data && (
                <>
                  <div className="flex flex-wrap items-center justify-between gap-3 rounded-lg border border-slate-800 bg-slate-900/40 px-4 py-3">
                    <h2 className="text-sm font-semibold text-slate-200">
                      {shiftDetail.data.data.shiftDate} {shiftDetail.data.data.shiftName}
                    </h2>
                    <OptimiserPlanSelector value={planType} onChange={setPlanType} />
                  </div>

                  {planFor(planType, shiftDetail.data.data).planSummary && (
                    <p className="rounded-lg border border-slate-800 bg-slate-900/40 px-4 py-3 text-sm text-slate-300">
                      {planFor(planType, shiftDetail.data.data).planSummary}
                    </p>
                  )}

                  <OptimiserMovesList
                    planLabel={PLAN_LABELS[planType]}
                    assignments={planFor(planType, shiftDetail.data.data).assignments}
                  />

                  <OptimiserLoaderComparisonTable
                    planLabel={PLAN_LABELS[planType]}
                    original={shiftDetail.data.data.original.loaderStats}
                    chosen={planFor(planType, shiftDetail.data.data).loaderStats}
                  />

                  <OptimiserOutcomeComparison
                    planLabel={PLAN_LABELS[planType]}
                    original={shiftDetail.data.data.original.outcome}
                    chosen={planFor(planType, shiftDetail.data.data).outcome}
                  />

                  <OptimiserActualComparison
                    actual={shiftDetail.data.data.actual}
                    replayedOriginal={shiftDetail.data.data.original.outcome}
                  />
                </>
              )
            )}
          </>
        ))}
    </div>
  )
}
