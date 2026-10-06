import { expect, Page, test } from '@playwright/test';
import type { PublicOrderReceipt } from '../src/app/core/services/public-checkout-api.service';
import type { PublicOrderTracking } from '../src/app/core/services/public-order-tracking-api.service';

const storageKey = 'made-in-minas.public-checkout.v1';
const receipt: PublicOrderReceipt = {
  number: 1542,
  fulfillment: 'Pickup',
  total: 59.8,
  createdAt: '2026-10-05T14:00:00Z',
  tracking: { token: 'private-order-access', expiresAt: '2026-10-12T14:00:00Z' },
};

async function setup(page: Page, saved: PublicOrderReceipt | null = receipt) {
  await page.clock.install({ time: new Date('2026-10-05T14:05:00Z') });
  await page.addInitScript(
    ({ key, saved }) => {
      if (saved && !sessionStorage.getItem(key)) {
        sessionStorage.setItem(key, JSON.stringify({ version: 1, receipt: saved }));
      }
    },
    { key: storageKey, saved },
  );
  const state = {
    calls: 0,
    status: 200,
    gate: null as Promise<void> | null,
    order: {
      number: 1542,
      fulfillment: 'Pickup',
      total: 59.8,
      status: 'New',
      createdAt: '2026-10-05T14:00:00Z',
      updatedAt: '2026-10-05T14:00:00Z',
      history: [{ status: 'New', occurredAt: '2026-10-05T14:00:00Z' }],
    } as PublicOrderTracking,
  };
  const user = {
    id: 'staff',
    name: 'Ana',
    username: 'ana',
    role: 'Administrator',
    permissions: ['orders.manage'],
  };
  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    if (url.pathname === '/api/public-orders/tracking') {
      state.calls++;
      expect(request.method()).toBe('GET');
      expect(request.headers()['authorization']).toBeUndefined();
      expect(request.headers()['x-order-access']).toBe(receipt.tracking!.token);
      expect(url.search).toBe('');
      expect(request.url()).not.toContain(receipt.tracking!.token);
      if (state.gate) {
        await state.gate;
      }
      return route.fulfill({ status: state.status, json: state.status === 200 ? state.order : {} });
    }
    if (url.pathname === '/api/auth/login') {
      return route.fulfill({
        json: {
          user,
          accessToken: 'staff-token',
          tokenType: 'Bearer',
          expiresAt: '2026-10-05T14:20:00Z',
        },
      });
    }
    if (url.pathname === '/api/auth/me') {
      expect(request.headers()['authorization']).toBe('Bearer staff-token');
      return route.fulfill({ json: user });
    }
    return route.fulfill({ status: 404, json: {} });
  });
  return state;
}

test('comprovante abre acompanhamento sem segredo na URL e sobrevive ao reload', async ({
  page,
}) => {
  const state = await setup(page);
  await page.goto('/pedido/finalizar');
  await page.getByRole('link', { name: 'Acompanhar meu pedido' }).click();
  await expect(page).toHaveURL(/\/pedido\/acompanhar$/);
  await expect(page.getByRole('status')).toContainText('Novo');
  await expect(page.getByRole('list', { name: 'Histórico do pedido' })).toContainText(
    '05/10 11:00:00',
  );
  await expect(page.getByRole('region', { name: 'Acompanhamento do pedido' })).toContainText(
    'R$ 59,80',
  );
  await expect(page.locator('body')).not.toContainText(receipt.tracking!.token);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.reload();
  await expect(page.getByRole('status')).toContainText('Novo');
  expect(state.calls).toBe(2);
});

test('atualização automática mostra status e horários recebidos da API', async ({ page }) => {
  const state = await setup(page);
  await page.goto('/pedido/acompanhar');
  await expect(page.getByRole('status')).toContainText('Novo');
  state.order = {
    ...state.order,
    status: 'Ready',
    updatedAt: '2026-10-05T14:05:00Z',
    history: [
      ...state.order.history,
      { status: 'Confirmed', occurredAt: '2026-10-05T14:01:00Z' },
      { status: 'InPreparation', occurredAt: '2026-10-05T14:02:00Z' },
      { status: 'Ready', occurredAt: '2026-10-05T14:05:00Z' },
    ],
  };
  await page.clock.fastForward(15000);
  await expect(page.getByRole('status')).toContainText('pronto para retirada no balcão');
  await expect(page.getByRole('listitem')).toHaveCount(4);
  await expect(page.getByRole('listitem').last()).toContainText('05/10 11:05:00');
  expect(state.calls).toBe(2);
});

for (const status of ['Finalized', 'Cancelled'] as const) {
  test(`${status} encerra consultas automáticas`, async ({ page }) => {
    const state = await setup(page);
    state.order.status = status;
    await page.goto('/pedido/acompanhar');
    await expect(page.getByRole('status')).toContainText(
      status === 'Finalized' ? 'Finalizado' : 'Cancelado',
    );
    await expect(page.getByRole('button', { name: 'Atualizar pedido' })).toHaveCount(0);
    await page.clock.fastForward(60000);
    expect(state.calls).toBe(1);
  });
}

test('retirada ainda acompanha até a finalização', async ({ page }) => {
  const state = await setup(page);
  state.order.status = 'Delivered';
  await page.goto('/pedido/acompanhar');
  await expect(page.getByRole('status')).toContainText('Retirado');
  state.order.status = 'Finalized';
  await page.clock.fastForward(15000);
  await expect(page.getByRole('status')).toContainText('Finalizado');
});

test('falha remove status antigo e nova consulta recupera', async ({ page }) => {
  const state = await setup(page);
  await page.goto('/pedido/acompanhar');
  await expect(page.getByRole('status')).toContainText('Novo');
  state.status = 503;
  await page.getByRole('button', { name: 'Atualizar pedido' }).click();
  await expect(page.getByRole('alert')).toContainText('Não foi possível atualizar');
  await expect(page.getByRole('status')).toHaveCount(0);
  state.status = 200;
  state.order.status = 'InPreparation';
  await page.clock.fastForward(30000);
  await expect(page.getByRole('status')).toContainText('Em preparação');
  await expect(page.getByRole('alert')).toHaveCount(0);
});

test('acesso expirado orienta atendimento e interrompe consultas', async ({ page }) => {
  const state = await setup(page);
  state.status = 404;
  await page.goto('/pedido/acompanhar');
  await expect(page.getByRole('alert')).toContainText('expirou');
  await expect(page.getByRole('heading', { name: 'Pedido #1542' })).toBeVisible();
  await expect(page.getByRole('button')).toHaveCount(0);
  await page.clock.fastForward(60000);
  expect(state.calls).toBe(1);
  expect(await page.evaluate((key) => sessionStorage.getItem(key), storageKey)).toContain('1542');
});

test('limite de consultas aguarda um minuto', async ({ page }) => {
  const state = await setup(page);
  state.status = 429;
  await page.goto('/pedido/acompanhar');
  await expect(page.getByRole('alert')).toContainText('Aguarde um minuto');
  await page.clock.fastForward(59000);
  expect(state.calls).toBe(1);
  state.status = 200;
  await page.clock.fastForward(1000);
  await expect(page.getByRole('status')).toContainText('Novo');
});

test('consulta pendente não permite sobreposição e timeout libera retomada', async ({ page }) => {
  const state = await setup(page);
  let release!: () => void;
  state.gate = new Promise<void>((resolve) => {
    release = resolve;
  });
  try {
    await page.goto('/pedido/acompanhar');
    await expect(page.getByRole('button', { name: 'Consultando…' })).toBeDisabled();
    await page.clock.fastForward(10000);
    expect(state.calls).toBe(1);
    await page.clock.fastForward(6000);
    await expect(page.getByRole('alert')).toContainText('Não foi possível atualizar');
  } finally {
    release();
    state.gate = null;
  }
  await page.getByRole('button', { name: 'Atualizar pedido' }).click();
  await expect(page.getByRole('status')).toContainText('Novo');
  expect(state.calls).toBe(2);
});

test('ocultar a aba pausa consultas, voltar atualiza e sair encerra', async ({ page }) => {
  const state = await setup(page);
  await page.goto('/pedido/acompanhar');
  await expect(page.getByRole('status')).toContainText('Novo');
  await page.evaluate(() => {
    Object.defineProperty(document, 'hidden', { configurable: true, value: true });
    document.dispatchEvent(new Event('visibilitychange'));
  });
  await page.clock.fastForward(60000);
  expect(state.calls).toBe(1);
  state.order.status = 'Confirmed';
  await page.evaluate(() => {
    Object.defineProperty(document, 'hidden', { configurable: true, value: false });
    document.dispatchEvent(new Event('visibilitychange'));
  });
  await expect(page.getByRole('status')).toContainText('Confirmado');
  await page.getByRole('link', { name: 'Voltar ao comprovante' }).click();
  await expect(page).toHaveURL(/\/pedido\/finalizar$/);
  await page.clock.fastForward(60000);
  expect(state.calls).toBe(2);
});

for (const condition of ['missing', 'legacy', 'corrupt'] as const) {
  test(`acesso ${condition} não consulta e oferece comprovante`, async ({ page }) => {
    const saved =
      condition === 'missing'
        ? null
        : condition === 'legacy'
          ? { ...receipt, tracking: undefined }
          : { ...receipt, tracking: { token: '', expiresAt: 'invalid' } };
    const state = await setup(page, saved);
    await page.goto('/pedido/acompanhar?number=1542&phone=31999991234');
    await expect(
      page.getByRole('heading', { name: 'Abra o comprovante do seu pedido' }),
    ).toBeVisible();
    await expect(page.getByRole('link', { name: 'Ver meu comprovante' })).toBeVisible();
    expect(state.calls).toBe(0);
  });
}

test('falha pública não envia JWT nem encerra sessão da equipe', async ({ page }) => {
  const state = await setup(page);
  await page.goto('/entrar?returnUrl=%2Fequipe');
  await page.getByLabel('Login', { exact: true }).fill('ana');
  await page.getByLabel('Senha', { exact: true }).fill('Senha teste 123!');
  await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await expect(page).toHaveURL(/\/equipe$/);
  state.status = 401;
  await page.evaluate(() => {
    history.pushState(null, '', '/pedido/acompanhar');
    dispatchEvent(new PopStateEvent('popstate'));
  });
  await expect(page.getByRole('alert')).toBeVisible();
  await page.evaluate(() => {
    history.pushState(null, '', '/equipe');
    dispatchEvent(new PopStateEvent('popstate'));
  });
  await expect(page).toHaveURL(/\/equipe$/);
  await expect(page.getByRole('heading', { name: 'Olá, Ana.' })).toBeVisible();
});

for (const status of ['New', 'Ready', 'Delivered'] as const) {
  test(`entrega mostra orientação correta em ${status}`, async ({ page }) => {
    const state = await setup(page, { ...receipt, fulfillment: 'Delivery' });
    state.order.fulfillment = 'Delivery';
    state.order.status = status;
    await page.goto('/pedido/acompanhar');
    const region = page.getByRole('region', { name: 'Acompanhamento do pedido' });
    await expect(region).toContainText('Entrega no endereço informado');
    await expect(region).not.toContainText('retirada');
    await expect(region).not.toContainText('antes de buscar');
    if (status === 'Delivered') {
      await expect(page.getByRole('status')).toContainText('Entregue');
    }
  });
}
