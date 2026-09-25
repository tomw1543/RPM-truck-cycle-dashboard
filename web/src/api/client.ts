// Fetch wrapper. Base URL is empty in dev (relative paths hit the Vite proxy defined in
// vite.config.ts) and comes from VITE_API_BASE_URL in production.

const BASE_URL = import.meta.env.VITE_API_BASE_URL ?? ''

/** application/problem+json shape ASP.NET Core's TypedResults.Problem returns on 4xx/5xx. */
interface ProblemDetails {
  title?: string
  detail?: string
  status?: number
}

export class ApiError extends Error {
  readonly status: number
  readonly title: string
  readonly detail?: string

  constructor(status: number, title: string, detail?: string) {
    super(detail ? `${title}: ${detail}` : title)
    this.name = 'ApiError'
    this.status = status
    this.title = title
    this.detail = detail
  }
}

export async function apiGet<T>(path: string, params?: Record<string, string | undefined>): Promise<T> {
  const url = new URL(path, BASE_URL || window.location.origin)
  if (params) {
    for (const [key, value] of Object.entries(params)) {
      if (value !== undefined) url.searchParams.set(key, value)
    }
  }

  // When BASE_URL is empty, request the relative path + search so the Vite proxy handles it.
  const requestUrl = BASE_URL ? url.toString() : `${url.pathname}${url.search}`

  const response = await fetch(requestUrl)

  if (!response.ok) {
    let problem: ProblemDetails = {}
    try {
      problem = await response.json()
    } catch {
      // Non-JSON error body - fall through to the generic message below.
    }
    throw new ApiError(
      response.status,
      problem.title ?? `Request failed (${response.status})`,
      problem.detail,
    )
  }

  return response.json() as Promise<T>
}
