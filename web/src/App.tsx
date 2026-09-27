import { useState } from 'react'
import { Route, Routes } from 'react-router'
import { useMeta } from './api/hooks'
import { FullPageLoading } from './components/FullPageLoading'
import { Header } from './components/Header'
import { Losses } from './pages/Losses'
import { Optimiser } from './pages/Optimiser'
import { Overview } from './pages/Overview'
import { Schedule } from './pages/Schedule'
import { TruckDetail } from './pages/TruckDetail'
import { Trucks } from './pages/Trucks'

function App() {
  const [live, setLive] = useState(false)
  const meta = useMeta(live)

  // The database auto-pauses when idle, so the very first request of a session can take
  // close to a minute to come back. Show a full-page loading state for that one wait
  // rather than the small per-section banners the rest of the app uses.
  if (meta.isLoading) {
    return <FullPageLoading />
  }

  return (
    <div className="min-h-screen bg-slate-950">
      <Header asOf={meta.data?.asOf} live={live} onLiveChange={setLive} />
      <Routes>
        <Route path="/" element={<Overview live={live} />} />
        <Route path="/trucks" element={<Trucks live={live} />} />
        <Route path="/trucks/:name" element={<TruckDetail live={live} />} />
        <Route path="/losses" element={<Losses live={live} />} />
        <Route path="/schedule" element={<Schedule live={live} />} />
        <Route path="/optimiser" element={<Optimiser live={live} />} />
      </Routes>
    </div>
  )
}

export default App
