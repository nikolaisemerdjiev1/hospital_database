import { AxeBuilder } from '@axe-core/playwright'
import { expect, test, type Page, type Route } from '@playwright/test'

async function healthResponse(route: Route, status = 200, retryAfter?: string) {
  await route.fulfill({
    status,
    contentType: 'text/plain',
    body: status === 200 ? 'Healthy' : 'Unhealthy',
    headers: {
      'Access-Control-Allow-Origin': '*',
      'Access-Control-Expose-Headers': 'Retry-After',
      ...(retryAfter ? { 'Retry-After': retryAfter } : {}),
    },
  })
}

async function ready(page: Page) {
  await page.route('http://127.0.0.1:5050/health/ready', (route) => healthResponse(route))
  await page.goto('/')
  await expect(page.getByText('The demo is ready.')).toBeVisible()
}

test.beforeEach(async ({ page }) => {
  // Exercise the real SDK redirect boundary without contacting a live Auth0 tenant.
  await page.route('https://demo-auth.example.invalid/**', (route) => route.fulfill({
    contentType: 'text/html', body: '<h1>Test sign-in boundary</h1>',
  }))
})

for (const width of [320, 768, 1440]) {
  test(`landing reflows and passes axe at ${width}px`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width, height: 900 })
    await ready(page)
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(/care moves better/i)
    await expect(page.getByRole('article')).toHaveCount(3)
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true)
    const accessibility = await new AxeBuilder({ page })
      .withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()
    expect(accessibility.violations).toEqual([])
    await page.screenshot({ path: testInfo.outputPath(`landing-${width}.png`), fullPage: true })
  })
}

test('keyboard navigation, visible focus, and reduced motion work', async ({ page }) => {
  const navigationWarnings: string[] = []
  page.on('console', (message) => {
    if (message.text().includes('blocker on a POP navigation')) navigationWarnings.push(message.text())
  })
  await page.emulateMedia({ reducedMotion: 'reduce' })
  await ready(page)
  await page.keyboard.press('Tab')
  await expect(page.getByRole('link', { name: 'Skip to main content' })).toBeFocused()
  await page.keyboard.press('Tab')
  await expect(page.getByRole('link', { name: 'Harbor Care home' })).toBeFocused()
  await page.keyboard.press('Tab')
  const chooseRole = page.getByRole('link', { name: 'Choose your role' })
  await expect(chooseRole).toBeFocused()
  await expect(chooseRole).toHaveCSS('outline-style', 'solid')
  await expect(chooseRole).toHaveCSS('outline-width', '3px')
  await expect(chooseRole).toHaveCSS('transition-duration', '1e-05s')
  await expect(page.locator('html')).toHaveCSS('scroll-behavior', 'auto')
  await page.keyboard.press('Enter')
  await expect(page).toHaveURL(/#demo-roles$/)
  await expect(page.getByRole('heading', { name: 'Choose a part in the care journey.' })).toBeFocused()
  const reveal = page.getByRole('button', { name: 'Show demo password' })
  await reveal.focus()
  await page.keyboard.press('Enter')
  await expect(page.getByText('test-only-demo-value', { exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Hide demo password' })).toHaveAttribute('aria-expanded', 'true')
  await page.goBack()
  await expect(page).toHaveURL('http://127.0.0.1:4174/')
  expect(navigationWarnings).toEqual([])
})

for (const role of ['patient', 'doctor', 'pharmacist']) {
  test(`${role} card requests the suggested account without assigning permissions`, async ({ page }) => {
    await ready(page)
    await page.getByRole('button', { name: `Sign in as ${role}` }).click()
    await expect(page.getByRole('heading', { name: 'Test sign-in boundary' })).toBeVisible()
    const authorization = new URL(page.url())
    expect(authorization.hostname).toBe('demo-auth.example.invalid')
    expect(authorization.pathname).toBe('/authorize')
    expect(authorization.searchParams.get('login_hint')).toBe(`care.relay.demo+${role}@gmail.com`)
    expect(authorization.searchParams.get('prompt')).toBe('login')
    expect(authorization.searchParams.get('redirect_uri')).toBe('http://127.0.0.1:4174/auth/callback')
    expect(authorization.searchParams.has('role')).toBe(false)
    expect(authorization.searchParams.has('app_role')).toBe(false)
  })
}

test('a reload recovery hint starts one SDK redirect without granting access or looping', async ({ page }) => {
  await ready(page)
  await page.evaluate(() => sessionStorage.setItem('harbor-care:session-recovery', '1'))
  await page.goto('/app/doctor')
  await expect(page.getByRole('heading', { name: 'Test sign-in boundary' })).toBeVisible()
  const authorization = new URL(page.url())
  expect(authorization.pathname).toBe('/authorize')
  expect(authorization.searchParams.has('role')).toBe(false)
  expect(authorization.searchParams.has('login_hint')).toBe(false)
  expect(authorization.searchParams.get('code_challenge_method')).toBe('S256')

  // No real authentication happened. A second visit must stop at normal sign-in.
  await page.goto('/app/doctor')
  await expect(page.getByRole('heading', { name: 'Sign in to continue your care journey.' })).toBeVisible()
})

test('cold-start failure leaves the page usable and an explicit retry recovers', async ({ page }) => {
  let attempts = 0
  let healthy = false
  await page.route('http://127.0.0.1:5050/health/ready', (route) => {
    attempts += 1
    return healthResponse(route, healthy ? 200 : 503)
  })
  await page.goto('/')
  await expect(page.getByText('Waking the demo workspace…')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Sign in as patient' })).toBeDisabled()
  await expect(page.getByRole('link', { name: 'Choose your role' })).toBeEnabled()
  await expect(page.getByText('The demo is taking longer than expected.')).toBeVisible({ timeout: 20_000 })
  // Development StrictMode may send one cancelled probe before the four-attempt run.
  expect(attempts).toBeGreaterThanOrEqual(4)
  expect(attempts).toBeLessThanOrEqual(5)
  const attemptsBeforeRetry = attempts
  const accessibility = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()
  expect(accessibility.violations).toEqual([])
  healthy = true
  await page.getByRole('button', { name: 'Try again' }).click()
  await expect(page.getByText('The demo is ready.')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Sign in as patient' })).toBeEnabled()
  expect(attempts).toBe(attemptsBeforeRetry + 1)
})

test('a cross-origin rate-limit response enforces its cooldown', async ({ page }) => {
  await page.clock.install()
  await page.route('http://127.0.0.1:5050/health/ready', (route) => healthResponse(route, 429, '30'))
  await page.goto('/')
  await expect(page.getByText(/the service asked us to wait/i)).toBeVisible()
  const retry = page.getByRole('button', { name: 'Try again' })
  await expect(retry).toBeDisabled()
  await page.clock.fastForward('00:29')
  await expect(retry).toBeDisabled()
  await page.clock.fastForward('00:01')
  await expect(retry).toBeEnabled()
})
