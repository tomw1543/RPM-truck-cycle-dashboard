import type { OptimiserPlanType } from '../api/types'

interface OptimiserPlanSelectorProps {
  value: OptimiserPlanType
  onChange: (value: OptimiserPlanType) => void
}

const OPTIONS: { value: OptimiserPlanType; label: string }[] = [
  { value: 'Original', label: 'Original' },
  { value: 'MoreOutput', label: 'More output' },
  { value: 'Leaner', label: 'Leaner' },
]

/** Native radio inputs, so arrow-key navigation between the three options works without any
 * extra keyboard handling. The selected option also gets a text checkmark, not just a colour
 * change, matching the rest of the app's "text markers not colour alone" convention. */
export function OptimiserPlanSelector({ value, onChange }: OptimiserPlanSelectorProps) {
  return (
    <fieldset className="flex flex-wrap items-center gap-2">
      <legend className="mb-1 w-full text-xs font-semibold uppercase tracking-wide text-slate-400">Plan</legend>
      {OPTIONS.map((opt) => {
        const checked = value === opt.value
        return (
          <label
            key={opt.value}
            className={`flex cursor-pointer items-center gap-1.5 rounded-md border px-3 py-1.5 text-sm ${
              checked ? 'border-cyan-600 bg-cyan-950/40 text-cyan-200' : 'border-slate-700 bg-slate-900 text-slate-300 hover:bg-slate-800'
            }`}
          >
            <input
              type="radio"
              name="optimiser-plan"
              value={opt.value}
              checked={checked}
              onChange={() => onChange(opt.value)}
              className="sr-only"
            />
            {checked && <span aria-hidden="true">✓</span>}
            {opt.label}
          </label>
        )
      })}
    </fieldset>
  )
}
