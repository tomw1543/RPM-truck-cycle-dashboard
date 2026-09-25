import type { HotspotRow, HotspotsData, ProfileCell } from '../api/types'
import { fmtNumber } from '../lib/format'

interface QueueHotspotsProps {
  data: HotspotsData
}

const HOURS = Array.from({ length: 24 }, (_, i) => i)

function hourLabel(h: number): string {
  return `${h.toString().padStart(2, '0')}:00`
}

function formatDateHour(iso: string): string {
  const match = /^(\d{4})-(\d{2})-(\d{2})[T ](\d{2}):(\d{2})/.exec(iso)
  if (!match) return iso
  const [, , month, day, hour] = match
  return `${day}/${month} ${hour}:00`
}

// Single-hue sequential scale (light -> dark cyan) so the heatmap reads correctly for colourblind
// viewers and in greyscale printouts - value is also always printed in the cell, never colour-only.
function cellStyle(value: number, max: number): { background: string; dark: boolean } {
  if (max <= 0 || value <= 0) return { background: 'transparent', dark: false }
  const t = value / max
  const lightness = 88 - t * 68 // 88% (near-white) down to 20% (near-black)
  return { background: `hsl(189, 75%, ${lightness}%)`, dark: lightness > 52 }
}

export function QueueHotspots({ data }: QueueHotspotsProps) {
  const loaders = [...new Set(data.profile.map((p) => p.loaderName))].sort()
  const cellByKey = new Map<string, ProfileCell>(data.profile.map((c) => [`${c.loaderName}|${c.hourOfDay}`, c]))
  const maxExcess = Math.max(0, ...data.profile.map((c) => c.excessMin))

  return (
    <div className="rounded-lg border border-slate-800 bg-slate-900/60 p-4">
      <h2 className="text-sm font-semibold text-slate-200">Queue hotspots</h2>
      <p className="mt-1 text-xs text-slate-400">
        Excess queue minutes above each loader&apos;s own all-time 25th-percentile queue time.
      </p>

      <TopHotspotsList rows={data.top} />

      <h3 className="mt-5 text-xs font-semibold uppercase tracking-wide text-slate-400">Loader x hour of day, excess queue minutes</h3>
      <div className="mt-2 overflow-x-auto" role="img" aria-label="Heatmap of excess queue minutes by loader and hour of day">
        <table className="w-full min-w-[900px] border-separate border-spacing-0.5 text-xs">
          <thead>
            <tr>
              <th className="px-1 py-1 text-left font-medium text-slate-400">Loader</th>
              {HOURS.map((h) => (
                <th key={h} className="px-0.5 py-1 text-center font-medium text-slate-500">
                  {h % 3 === 0 ? hourLabel(h) : ''}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {loaders.map((loader) => (
              <tr key={loader}>
                <th scope="row" className="px-1 py-0.5 text-left font-medium text-slate-300">
                  {loader}
                </th>
                {HOURS.map((h) => {
                  const cell = cellByKey.get(`${loader}|${h}`)
                  const value = cell?.excessMin ?? 0
                  const { background, dark } = cellStyle(value, maxExcess)
                  return (
                    <td
                      key={h}
                      className={`h-7 w-8 rounded-sm text-center tabular-nums ${dark ? 'text-slate-900' : 'text-slate-200'}`}
                      style={{ background }}
                      aria-label={`${loader}, ${hourLabel(h)}, ${value > 0 ? `${fmtNumber(value, 0)} excess queue minutes` : 'no excess queue minutes'}`}
                    >
                      {value > 0 ? Math.round(value) : <span aria-hidden="true">&middot;</span>}
                    </td>
                  )
                })}
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <ProfileScreenReaderTable loaders={loaders} cellByKey={cellByKey} />
    </div>
  )
}

function TopHotspotsList({ rows }: { rows: HotspotRow[] }) {
  if (rows.length === 0) {
    return <p className="mt-3 text-sm text-slate-400">No queue hotspots in this window.</p>
  }

  return (
    <ol className="mt-3 flex flex-col gap-1 text-sm">
      {rows.map((r, i) => (
        <li key={`${r.loaderName}-${r.dateHour}`} className="flex items-baseline gap-2 text-slate-300">
          <span className="w-5 text-right tabular-nums text-slate-500">{i + 1}.</span>
          <span className="font-medium text-slate-200">{r.loaderName}</span>
          <span className="tabular-nums text-slate-400">{formatDateHour(r.dateHour)}</span>
          <span className="tabular-nums">{fmtNumber(r.excessMin)} excess min</span>
          <span className="text-xs text-slate-500">
            ({r.cycles} cycles, avg queue {fmtNumber(r.averageQueueMin)} min)
          </span>
        </li>
      ))}
    </ol>
  )
}

// Screen-reader-only equivalent of the heatmap above: same (loader, hour, excess, cycles) data as
// an ordinary table, so a screen reader user gets the numbers without relying on the coloured grid.
function ProfileScreenReaderTable({ loaders, cellByKey }: { loaders: string[]; cellByKey: Map<string, ProfileCell> }) {
  return (
    <table className="sr-only">
      <caption>Excess queue minutes by loader and hour of day</caption>
      <thead>
        <tr>
          <th>Loader</th>
          <th>Hour</th>
          <th>Excess queue minutes</th>
          <th>Cycles</th>
        </tr>
      </thead>
      <tbody>
        {loaders.flatMap((loader) =>
          HOURS.map((h) => {
            const cell = cellByKey.get(`${loader}|${h}`)
            return (
              <tr key={`${loader}-${h}`}>
                <td>{loader}</td>
                <td>{hourLabel(h)}</td>
                <td>{fmtNumber(cell?.excessMin ?? 0)}</td>
                <td>{cell?.cycles ?? 0}</td>
              </tr>
            )
          }),
        )}
      </tbody>
    </table>
  )
}
