import { useState } from 'react'
import { Route, Routes } from 'react-router'
import { useMeta } from './api/hooks'
import { Header } from './components/Header'
import { NotAvailable } from './pages/NotAvailable'
import { Overview } from './pages/Overview'

function App() {
  const [live, setLive] = useState(false)
  const meta = useMeta(live)

  return (
    <div className="min-h-screen bg-slate-950">
      <Header asOf={meta.data?.asOf} live={live} onLiveChange={setLive} />
      <Routes>
        <Route path="/" element={<Overview live={live} />} />
        <Route path="/trucks/:id" element={<NotAvailable title="Truck detail" />} />
        <Route path="/losses" element={<NotAvailable title="Losses" />} />
      </Routes>
    </div>
  )
}

export default App
