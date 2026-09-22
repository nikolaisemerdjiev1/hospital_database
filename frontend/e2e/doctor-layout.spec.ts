import { expect, test } from '@playwright/test'

for (const width of [320, 768, 1440]) {
  test(`doctor row columns align across completed and cancelled visits at ${width}px`, async ({ page }) => {
    await page.route('http://127.0.0.1:5050/health/ready', (route) => route.fulfill({
      contentType: 'text/plain', body: 'Healthy', headers: { 'Access-Control-Allow-Origin': '*' },
    }))
    await page.setViewportSize({ width, height: 900 })
    await page.goto('/')
    await expect(page.getByText('The demo is ready.')).toBeVisible()
    await page.addStyleTag({ url: '/src/features/doctor/doctor.css' })
    // Layout-only fixture using the production stylesheet and row structure.
    // No authenticated browser session, application-data request, or workflow mutation.
    await page.locator('#main-content').evaluate((main) => {
      main.setAttribute('class', 'doctor-workspace')
      main.innerHTML = `<section class="worklist-board"><div class="worklist-rows">
        <article class="worklist-row">
          <time class="worklist-row__time"><strong>Fri, Sep 11</strong><span>9:00 AM PDT</span></time>
          <div class="worklist-row__patient"><span class="status-pill status-pill--completed">Completed</span><h3>Avery Brooks</h3><p>Medication follow-up</p></div>
          <a class="secondary-button clinical-action" href="#">Review consultation</a>
        </article>
        <article class="worklist-row">
          <time class="worklist-row__time"><strong>Sat, Sep 12</strong><span>10:00 AM PDT</span></time>
          <div class="worklist-row__patient"><span class="status-pill status-pill--cancelled">Cancelled</span><h3>Jamie Lopez</h3><p>Joint pain</p></div>
          <span class="worklist-row__closed">No action needed</span>
        </article>
        <article class="worklist-row">
          <time class="worklist-row__time"><strong>Sun, Sep 13</strong><span>11:00 AM PDT</span></time>
          <div class="worklist-row__patient"><span class="status-pill status-pill--scheduled">Scheduled</span><h3>Jordan Lee</h3><p>Follow-up visit</p></div>
          <button class="primary-button clinical-action" type="button">Start consultation</button>
        </article>
      </div></section>`
    })
    await page.evaluate(async () => { await document.fonts.ready })
    const leftEdges = await page.locator('.worklist-row__patient').evaluateAll((items) =>
      items.map((item) => item.getBoundingClientRect().left))
    expect(Math.max(...leftEdges) - Math.min(...leftEdges)).toBeLessThanOrEqual(1)
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true)
  })
}
