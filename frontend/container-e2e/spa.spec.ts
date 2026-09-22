import { AxeBuilder } from '@axe-core/playwright'
import { expect, test } from '@playwright/test'

test.beforeEach(async ({ page }) => {
  await page.route('https://demo-auth.example.invalid/**', route => route.fulfill({
    contentType: 'text/html', body: '<h1>Test sign-in boundary</h1>',
  }))
})

for (const width of [320, 768, 1440]) {
  test(`production shell, CSP, accessibility and layout at ${width}px`, async ({ page }) => {
    const policyErrors: string[] = []
    page.on('console', message => {
      if (/content security policy|violates.*directive/i.test(message.text())) policyErrors.push(message.text())
    })
    await page.setViewportSize({ width, height: 900 })
    await page.emulateMedia({ reducedMotion: 'reduce' })
    const response = await page.goto('/')
    expect(response?.headers()['content-security-policy']).toContain("script-src 'self'")
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(/care moves better/i)
    await expect(page.getByText('The demo is ready.')).toBeVisible({ timeout: 20_000 })
    await expect(page.getByRole('button', { name: 'Check again', exact: true })).toHaveCSS('opacity', '1')
    await expect(page.getByRole('article')).toHaveCount(3)
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
    await page.keyboard.press('Tab')
    await expect(page.getByRole('link', { name: 'Skip to main content' })).toBeFocused()
    // Assert application CSP before axe injects its own data-font/blob-worker probes.
    expect(policyErrors).toEqual([])
    const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()
    expect(results.violations).toEqual([])
  })
}

for (const role of ['patient', 'doctor', 'pharmacist']) {
  test(`production ${role} card reaches real SDK boundary with correct hint`, async ({ page }) => {
    await page.goto('/')
    await expect(page.getByText('The demo is ready.')).toBeVisible()
    await page.getByRole('article').filter({ hasText: `care.relay.demo+${role}@gmail.com` }).getByRole('button').click()
    await expect(page.getByRole('heading', { name: 'Test sign-in boundary' })).toBeVisible()
    expect(new URL(page.url()).searchParams.get('login_hint')).toBe(`care.relay.demo+${role}@gmail.com`)
    expect(new URL(page.url()).searchParams.get('redirect_uri')).toBe('http://127.0.0.1:8086/auth/callback')
  })
}

test('deep link reload preserves signed-out protection and APIs do not return HTML', async ({ page, request }) => {
  await page.goto('/app/pharmacy')
  await expect(page.getByRole('heading', { name: 'Sign in to continue your care journey.' })).toBeVisible()
  await page.reload()
  await expect(page.getByRole('heading', { name: 'Sign in to continue your care journey.' })).toBeVisible()
  expect((await request.get('/api/v1/identity/me')).status()).toBe(401)
  await Promise.all(['/api/missing', '/assets/missing.js', '/health/missing'].map(async path => {
    const response = await request.get(path)
    expect(response.status()).toBe(404)
    expect(response.headers()['content-type']).toContain('application/problem+json')
  }))
  expect((await request.get('/auth/callback')).headers()['content-type']).toContain('text/html')
})

test('database readiness failure leaves landing usable and bounded retry recovers', async ({ page }) => {
  let healthy = false
  let attempts = 0
  await page.route('**/health/ready', route => {
    attempts++
    return route.fulfill({ status: healthy ? 200 : 503, contentType: 'text/plain', body: healthy ? 'Healthy' : 'Unhealthy' })
  })
  await page.goto('/')
  await expect(page.getByRole('heading', { level: 1 })).toHaveText(/care moves better/i)
  await expect(page.getByRole('button', { name: /try again/i })).toBeVisible({ timeout: 25_000 })
  expect(attempts).toBe(4)
  healthy = true
  await page.getByRole('button', { name: /try again/i }).click()
  await expect(page.getByText('The demo is ready.')).toBeVisible()
})
