import {
  createContext,
  useContext,
  useEffect,
  useMemo,
  useState,
  type PropsWithChildren,
} from 'react'
import { useBlocker } from 'react-router-dom'

type GuardRegistration = {
  message: string
}

type NavigationGuardContextValue = {
  setGuard: (guard: GuardRegistration | null) => void
}

const NavigationGuardContext = createContext<NavigationGuardContextValue | null>(null)

export function NavigationGuardProvider({ children }: PropsWithChildren) {
  const [guard, setGuard] = useState<GuardRegistration | null>(null)
  const blocker = useBlocker(
    ({ currentLocation, nextLocation }) =>
      guard !== null &&
      `${currentLocation.pathname}${currentLocation.search}${currentLocation.hash}` !==
        `${nextLocation.pathname}${nextLocation.search}${nextLocation.hash}`,
  )

  useEffect(() => {
    if (blocker.state !== 'blocked') return

    if (window.confirm(guard?.message ?? 'Leave this page without saving?')) {
      blocker.proceed()
    } else {
      blocker.reset()
    }
  }, [blocker, guard])

  useEffect(() => {
    if (!guard) return

    function warnBeforeUnload(event: BeforeUnloadEvent) {
      event.preventDefault()
      event.returnValue = true
    }

    window.addEventListener('beforeunload', warnBeforeUnload)
    return () => window.removeEventListener('beforeunload', warnBeforeUnload)
  }, [guard])

  const value = useMemo(() => ({ setGuard }), [])

  return (
    <NavigationGuardContext.Provider value={value}>
      {children}
    </NavigationGuardContext.Provider>
  )
}

export function useNavigationGuard(isActive: boolean, message: string) {
  const context = useContext(NavigationGuardContext)

  if (!context) {
    throw new Error('useNavigationGuard must be used inside NavigationGuardProvider.')
  }

  useEffect(() => {
    context.setGuard(isActive ? { message } : null)

    return () => context.setGuard(null)
  }, [context, isActive, message])
}
