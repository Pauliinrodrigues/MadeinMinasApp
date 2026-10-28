import { expect, test } from '@playwright/test';

test('privacidade e atendimento são acessíveis sem sessão e sem API', async ({ page }) => {
  let requests = 0;
  await page.route('**/api/**', async (route) => {
    requests++;
    await route.abort();
  });
  await page.goto('/privacidade');
  await expect(
    page.getByRole('heading', { name: 'Privacidade e atendimento', exact: true }),
  ).toBeVisible();
  await expect(page.getByRole('region', { name: 'Uso dos dados no atendimento' })).toContainText(
    'Corinto–MG',
  );
  await expect(
    page.getByRole('link', { name: 'Falar com atendente', exact: true }),
  ).toHaveAttribute('href', '/pedido/atendimento');
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= document.documentElement.clientWidth,
    ),
  ).toBe(true);
  expect(requests).toBe(0);
});
