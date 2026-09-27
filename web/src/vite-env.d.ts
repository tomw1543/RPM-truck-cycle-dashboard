/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_API_BASE_URL: string
  /** Exactly "true" hides the live-data toggle in the header. Unset (default) shows it. */
  readonly VITE_HIDE_LIVE_TOGGLE?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
