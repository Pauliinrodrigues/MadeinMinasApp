import { expect, Page, test } from '@playwright/test';
import type { PublicOrderReceipt } from '../src/app/core/services/public-checkout-api.service';

const key = 'made-in-minas.public-order-history.v1';
const checkoutKey = 'made-in-minas.public-checkout.v1';
const receipt = (number: number): PublicOrderReceipt => ({
  number,
  fulfillment: 'Pickup',
  total: 29.9,
  createdAt: `2026-10-06T14:${String(number % 60).padStart(2, '0')}:00Z`,
  tracking: { token: 'private-' + number, expiresAt: '2026-10-12T14:00:00Z' },
});

async function setup(page: Page, saved: unknown = [receipt(1541), receipt(1542)]) {
  await page.clock.install({ time: new Date('2026-10-06T15:00:00Z') });
  await page.addInitScript(
    ({ key, saved }) => {
      if (!sessionStorage.getItem('history-seeded')) {
        sessionStorage.setItem('history-seeded', 'yes');
        sessionStorage.setItem(key, JSON.stringify(saved));
      }
    },
    { key, saved },
  );
  const state = {
    reads: [] as string[],
    writes: 0,
    gate: null as Promise<void> | null,
    gatedToken: '',
    status: 'New',
  };
  await page.route('**/api/**', async (route) => {
    const request = route.request();
    if (request.method() !== 'GET') {
      state.writes++;
    }
    const path = new URL(request.url()).pathname;
    if (path === '/api/public-orders/tracking') {
      const token = request.headers()['x-order-access'];
      state.reads.push(token);
      expect(request.headers()['authorization']).toBeUndefined();
      expect(request.url()).not.toContain('private-');
      if (token === state.gatedToken && state.gate) {
        await state.gate;
      }
      return route.fulfill({
        json: {
          number: Number(token.replace('private-', '')),
          fulfillment: 'Pickup',
          total: 29.9,
          status: state.status,
          createdAt: receipt(1542).createdAt,
          updatedAt: receipt(1542).createdAt,
          history: [{ status: state.status, occurredAt: receipt(1542).createdAt }],
        },
      });
    }
    if (path === '/api/menu') {
      return route.fulfill({
        json: { categories: [], items: [], page: 1, pageSize: 24, totalCount: 0 },
      });
    }
    if (path === '/api/public-checkout/delivery-areas') {
      return route.fulfill({ json: [] });
    }
    return route.fulfill({ status: 404, json: {} });
  });
  return state;
}

test('mantém pedidos anteriores ao montar outro, selecionar e recarregar', async ({
  page,
  isMobile,
}) => {
  const state = await setup(page);
  await page.addInitScript(
    ({ checkoutKey, current }) =>
      sessionStorage.setItem(checkoutKey, JSON.stringify({ version: 1, receipt: current })),
    { checkoutKey, current: receipt(1542) },
  );
  await page.goto('/pedido/finalizar');
  await page.getByRole('button', { name: 'Montar outro pedido' }).click();
  await page.getByRole('link', { name: 'Meus pedidos', exact: true }).click();
  await page.getByLabel('Escolher pedido').selectOption('1541');
  await expect(page.getByRole('heading', { name: 'Pedido #1541', exact: true })).toBeVisible();
  await expect(page.getByRole('status')).toContainText('Novo');
  await page.reload();
  await expect(page.getByLabel('Escolher pedido').locator('option')).toHaveCount(2);
  expect(state.writes).toBe(0);
  expect(await page.evaluate((key) => localStorage.getItem(key), key)).toBeNull();
  await expect(page.locator('body')).not.toContainText('private-');
  await page.getByRole('region', { name: 'Acompanhamento do pedido' }).scrollIntoViewIfNeeded();
  await page.screenshot({
    path: `../../.local/order-history-${isMobile ? 'mobile' : 'desktop'}.png`,
    fullPage: true,
  });
});

for (const broken of ['tab', 'device']) {
  test(`cópia ${broken} corrompida não impede recuperar o outro armazenamento`, async ({
    page,
  }) => {
    await setup(page, broken === 'tab' ? { invalid: true } : [receipt(1541)]);
    await page.addInitScript(({ key, saved }) => localStorage.setItem(key, JSON.stringify(saved)), {
      key,
      saved: broken === 'device' ? { invalid: true } : [receipt(1542)],
    });
    await page.goto('/pedido/acompanhar');
    await expect(page.getByRole('alert')).toContainText('recuperar todos');
    await expect(
      page.getByRole('heading', {
        name: `Pedido #${broken === 'device' ? 1541 : 1542}`,
        exact: true,
      }),
    ).toBeVisible();
    await expect(page.getByRole('status')).toContainText('Novo');
    await expect(page.getByLabel('Escolher pedido').locator('option')).toHaveCount(1);
  });
}

test('lembrar é opcional, reabre em outra aba e permite retirar consentimento', async ({
  page,
  context,
}) => {
  await setup(page);
  await page.goto('/pedido/acompanhar');
  const remember = page.getByLabel('Lembrar meus pedidos neste dispositivo');
  await expect(remember).not.toBeChecked();
  await remember.check();
  const stored = await page.evaluate((key) => JSON.parse(localStorage.getItem(key)!), key);
  expect(stored).toHaveLength(2);
  expect(Object.keys(stored[0]).sort()).toEqual([
    'createdAt',
    'fulfillment',
    'number',
    'total',
    'tracking',
  ]);
  const reopened = await context.newPage();
  await reopened.clock.install({ time: new Date('2026-10-06T15:00:00Z') });
  await reopened.route('**/api/**', (route) => route.fulfill({ status: 404, json: {} }));
  await reopened.goto('/pedido/acompanhar');
  await expect(reopened.getByLabel('Escolher pedido').locator('option')).toHaveCount(2);
  await expect(reopened.getByLabel('Lembrar meus pedidos neste dispositivo')).toBeChecked();
  await reopened.getByLabel('Lembrar meus pedidos neste dispositivo').uncheck();
  expect(await reopened.evaluate((key) => localStorage.getItem(key), key)).toBeNull();
  await reopened.close();
});

test('remove acesso sem cancelar pedido e não o restaura a partir do comprovante', async ({
  page,
}) => {
  const state = await setup(page, [receipt(1542)]);
  await page.goto('/pedido/acompanhar');
  await page.evaluate(
    ({ checkoutKey, current }) =>
      sessionStorage.setItem(checkoutKey, JSON.stringify({ version: 1, receipt: current })),
    { checkoutKey, current: receipt(1542) },
  );
  await page.reload();
  await page.getByLabel('Lembrar meus pedidos neste dispositivo').check();
  await page.getByText('Remover este acompanhamento', { exact: true }).click();
  await page.getByRole('button', { name: 'Confirmar remoção do acompanhamento' }).click();
  await expect(page.getByLabel('Escolher pedido')).toHaveCount(0);
  await page.reload();
  await expect(page.getByLabel('Escolher pedido')).toHaveCount(0);
  expect(state.writes).toBe(0);
  const stored = await page.evaluate(
    ({ key, checkoutKey }) => [localStorage.getItem(key), sessionStorage.getItem(checkoutKey)],
    { key, checkoutKey },
  );
  expect(stored.join('')).not.toContain('private-1542');
});

test('descarta acessos inválidos e expirados e limita os recentes a vinte', async ({ page }) => {
  const many = Array.from({ length: 25 }, (_, i) => receipt(1500 + i));
  many.push({
    ...receipt(1600),
    tracking: { token: 'expired', expiresAt: '2026-10-01T14:00:00Z' },
  });
  await setup(page, [...many, null, { number: 1700 }, receipt(1500)]);
  await page.goto('/pedido/acompanhar');
  await expect(page.getByLabel('Escolher pedido').locator('option')).toHaveCount(20);
  const stored = await page.evaluate((key) => sessionStorage.getItem(key), key);
  expect(stored).not.toContain('expired');
  expect(stored).not.toContain('1700');
});

test('trocar enquanto consulta não deixa resposta antiga substituir o pedido escolhido', async ({
  page,
}) => {
  const state = await setup(page);
  let release!: () => void;
  state.gatedToken = 'private-1542';
  state.gate = new Promise<void>((resolve) => {
    release = resolve;
  });
  try {
    await page.goto('/pedido/acompanhar');
    await expect.poll(() => state.reads.length).toBe(1);
    await page.getByLabel('Escolher pedido').selectOption('1541');
    await expect(page.getByRole('status')).toContainText('Novo');
    release();
    await expect(page.getByRole('heading', { name: 'Pedido #1541', exact: true })).toBeVisible();
    await page.clock.fastForward(15000);
    expect(state.reads.at(-1)).toBe('private-1541');
  } finally {
    release();
  }
});

test('histórico danificado e armazenamento bloqueado não enviam pedidos nem escondem o comprovante', async ({
  page,
}) => {
  await setup(page, { invalid: true });
  await page.goto('/pedido/acompanhar');
  await expect(page.getByRole('alert')).toContainText('recuperar');
  await page.evaluate(
    ({ key, checkoutKey, current }) => {
      sessionStorage.removeItem(key);
      sessionStorage.setItem(checkoutKey, JSON.stringify({ version: 1, receipt: current }));
    },
    { key, checkoutKey, current: receipt(1542) },
  );
  await page.reload();
  await expect(page.getByRole('heading', { name: 'Pedido #1542', exact: true })).toBeVisible();
  await page.evaluate(() => {
    Storage.prototype.setItem = () => {
      throw new DOMException('Full', 'QuotaExceededError');
    };
  });
  await page.getByLabel('Lembrar meus pedidos neste dispositivo').click();
  await expect(page.getByRole('alert')).toContainText('Não foi possível alterar');
  await expect(page.getByLabel('Lembrar meus pedidos neste dispositivo')).not.toBeChecked();
});
