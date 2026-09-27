export function FullPageLoading() {
  return (
    <div className="flex min-h-screen flex-col items-center justify-center gap-4 bg-slate-950 text-slate-400">
      <div className="h-10 w-10 animate-spin rounded-full border-2 border-slate-700 border-t-cyan-400" />
      <p className="text-sm">Loading…</p>
    </div>
  )
}
