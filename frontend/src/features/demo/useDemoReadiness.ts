import { useEffect, useState } from 'react'

import { ApiReadinessError, waitForApiReadiness } from './readiness'

type Readiness = {
  status: 'checking' | 'ready' | 'unavailable'
  attempt: number
  retryAt: number
}

const checking: Readiness = { status: 'checking', attempt: 1, retryAt: 0 }

export function useDemoReadiness() {
  const [state, setState] = useState<Readiness>(checking)
  const [run, setRun] = useState(0)
  const [now, setNow] = useState(Date.now)

  useEffect(() => {
    const controller = new AbortController()
    void waitForApiReadiness(controller.signal, (attempt) => {
      setState({ status: 'checking', attempt, retryAt: 0 })
    }).then(() => {
      if (!controller.signal.aborted) setState({ status: 'ready', attempt: 0, retryAt: 0 })
    }).catch((error: unknown) => {
      if (!controller.signal.aborted) {
        setState({
          status: 'unavailable',
          attempt: 0,
          retryAt: error instanceof ApiReadinessError ? error.retryAt : 0,
        })
      }
    })
    return () => controller.abort()
  }, [run])

  useEffect(() => {
    if (state.retryAt <= now) return
    const delay = Math.min(Math.max(state.retryAt - Date.now(), 0), 2_147_483_647)
    const timer = window.setTimeout(() => setNow(Date.now()), delay)
    return () => window.clearTimeout(timer)
  }, [state.retryAt, now])

  const coolingDown = state.retryAt > now
  function retry() {
    if (state.status === 'checking' || state.retryAt > Date.now()) return
    setState(checking)
    setRun((value) => value + 1)
  }

  return { ...state, coolingDown, retry }
}
