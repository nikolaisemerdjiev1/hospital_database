import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { expect, it, vi } from 'vitest'

import { ApiProblemError } from '../../api/client'
import { PharmacyErrorNotice } from './PharmacyFeedback'

it('presents a traceable pharmacist error and an accessible recovery action', async () => {
  const user = userEvent.setup()
  const onRetry = vi.fn<() => void>()
  const error = new ApiProblemError(
    409,
    'fulfillment_changed',
    'trace-pharmacy-review',
    'This fulfillment changed. Refresh it before trying again.',
  )

  render(
    <PharmacyErrorNotice
      error={error}
      title="The fulfillment action could not be completed."
      actionLabel="Refresh order"
      onAction={onRetry}
    />,
  )

  const alert = screen.getByRole('alert')
  expect(alert).toHaveTextContent('The fulfillment action could not be completed.')
  expect(alert).toHaveTextContent('This fulfillment changed. Refresh it before trying again.')
  expect(alert).toHaveTextContent('Support reference: trace-pharmacy-review')

  await user.click(screen.getByRole('button', { name: 'Refresh order' }))

  expect(onRetry).toHaveBeenCalledOnce()
})
