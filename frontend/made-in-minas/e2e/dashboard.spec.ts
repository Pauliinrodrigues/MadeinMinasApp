import { expect, Page, test } from '@playwright/test';
import type { DailyDashboard } from '../src/app/core/services/dashboard-api.service';

test.use({ timezoneId: 'Asia/Tokyo' });

const original: DailyDashboard = {
  date: '2026-10-02',
  timeZone: 'America/Sao_Paulo',
  startsAt: '2026-10-02T03:00:00Z',
  endsAt: '2026-10-03T03:00:00Z',
  calculatedAt: '2026-10-03T02:30:00Z',
  orders: {
    created: 15,
    createdAndCancelled: 2,
    confirmed: 10,
    confirmedValue: 450.5,
    averageTicket: 45.05,
  },
  receipts: { received: 300, refunded: 50, netReceived: 250 },
  queues: [
    { status: 'New', count: 3 },
    { status: 'Confirmed', count: 4 },
    { status: 'InPreparation', count: 6 },
    { status: 'Ready', count: 2 },
    { status: 'AwaitingDelivery', count: 1 },
    { status: 'OutForDelivery', count: 8 },
    { status: 'Delivered', count: 5 },
  ],
  production: { completed: 7, averageMinutes: 18.75 },
  topProducts: [
    { productId: 'product-1', productName: 'Uai Sô', quantity: 12, itemValue: 240 },
    { productId: 'product-2', productName: 'Trem Bão', quantity: 5, itemValue: 150 },
  ],
};

async function setup(page: Page, role = 'Administrator') {
  const state = {
    dashboard: structuredClone(original),
    status: 200,
    reads: 0,
    writes: [] as string[],
    gate: null as Promise<void> | null,
  };
  const profile = {
    id: 'staff',
    name: 'Equipe',
    username: 'staff',
    role,
    permissions:
      role === 'Administrator'
        ? ['dashboard.view', 'orders.manage', 'kitchen.work', 'dispatch.work']
        : [],
  };
  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    const json = (body: unknown, status = 200) => route.fulfill({ status, json: body });
    if (path === '/api/auth/login') {
      return json({
        accessToken: 'dashboard-token',
        tokenType: 'Bearer',
        expiresAt: new Date(Date.now() + 900000).toISOString(),
        user: profile,
      });
    }
    expect(request.headers()['authorization']).toBe('Bearer dashboard-token');
    if (path === '/api/auth/me') {
      return json(profile);
    }
    if (request.method() !== 'GET') {
      state.writes.push(path);
    }
    if (path === '/api/dashboard/today') {
      state.reads++;
      if (state.gate) {
        await state.gate;
      }
      return state.status === 200 ? json(state.dashboard) : json({}, state.status);
    }
    return json({}, 404);
  });
  return state;
}

async function login(page: Page) {
  await page.goto('/entrar?returnUrl=%2Fequipe');
  await page.getByLabel('Login', { exact: true }).fill('staff');
  await page.getByLabel('Senha', { exact: true }).fill('Senha de teste');
  await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await expect(page).toHaveURL(/\/equipe$/);
}

async function openDashboard(page: Page) {
  await login(page);
  await page.getByRole('link', { name: 'Dashboard', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Dashboard do dia', exact: true })).toBeVisible();
}

function metric(page: Page, label: string) {
  return page.locator('dl > div').filter({ has: page.getByText(label, { exact: true }) });
}

test('Dashboard: valores distintos, horário de Brasília e links da operação', async ({ page }) => {
  const errors: string[] = [];
  page.on('pageerror', (error) => errors.push(error.message));
  const state = await setup(page);
  await openDashboard(page);
  await expect(metric(page, 'Valor confirmado hoje')).toContainText(/R\$\s*450,50/);
  await expect(metric(page, 'Recebido hoje')).toContainText(/R\$\s*300,00/);
  await expect(metric(page, 'Estornado hoje')).toContainText(/R\$\s*50,00/);
  await expect(metric(page, 'Recebido menos estornos')).toContainText(/R\$\s*250,00/);
  await expect(metric(page, 'Ticket médio confirmado')).toContainText(/R\$\s*45,05/);
  await expect(metric(page, 'Tempo médio de produção')).toContainText('18,75 min');
  await expect(metric(page, 'Em preparação')).toContainText('6');
  await expect(page.getByRole('link', { name: 'Em preparação', exact: true })).toHaveAttribute(
    'href',
    '/equipe/pedidos?status=InPreparation',
  );
  await expect(page.getByText(/Consultado em/)).toContainText('02/10/2026, 23:30:00');
  await expect(page.getByRole('region', { name: 'Filas da operação agora' })).toContainText(
    'dias anteriores',
  );
  await expect(page.getByRole('link', { name: 'Abrir cozinha', exact: true })).toHaveAttribute(
    'href',
    '/equipe/cozinha',
  );
  await expect(page.getByRole('link', { name: 'Abrir expedição', exact: true })).toHaveAttribute(
    'href',
    '/equipe/expedicao',
  );
  await expect(page.getByRole('link', { name: 'Abrir pedidos', exact: true })).toHaveAttribute(
    'href',
    '/equipe/pedidos',
  );
  const products = page.getByRole('region', { name: 'Produtos mais pedidos hoje' });
  await expect(products.getByRole('listitem')).toHaveCount(2);
  await expect(products.getByRole('listitem').first()).toContainText('Uai Sô');
  await expect(products.getByRole('listitem').first()).toContainText('12 un.');
  expect(state.writes).toEqual([]);
  expect(errors).toEqual([]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('Dashboard: dia sem movimento não inventa médias', async ({ page }) => {
  const state = await setup(page);
  state.dashboard.orders = {
    created: 0,
    createdAndCancelled: 0,
    confirmed: 0,
    confirmedValue: 0,
    averageTicket: null,
  };
  state.dashboard.production = { completed: 0, averageMinutes: null };
  state.dashboard.receipts = { received: 0, refunded: 0, netReceived: 0 };
  state.dashboard.queues.forEach((queue) => {
    queue.count = 0;
  });
  state.dashboard.topProducts = [];
  await openDashboard(page);
  await expect(metric(page, 'Ticket médio confirmado')).toContainText('Sem pedidos confirmados');
  await expect(metric(page, 'Tempo médio de produção')).toContainText('Sem produções concluídas');
  await expect(metric(page, 'Recebido hoje')).toContainText(/R\$\s*0,00/);
  await expect(page.getByText('Nenhum produto em pedidos confirmados hoje.')).toBeVisible();
});

test('Dashboard: recebimento líquido negativo e nomes extensos permanecem legíveis', async ({
  page,
}) => {
  const state = await setup(page);
  state.dashboard.receipts = { received: 0, refunded: 125, netReceived: -125 };
  state.dashboard.topProducts[0].productName = '<script>alert(1)</script>' + 'Produto'.repeat(14);
  await openDashboard(page);
  await expect(metric(page, 'Recebido menos estornos')).toContainText(/-R\$\s*125,00/);
  await expect(page.getByRole('listitem').first()).toContainText(
    state.dashboard.topProducts[0].productName,
  );
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('Dashboard: atualização bloqueia repetição, remove valores antigos e permite recuperação', async ({
  page,
}) => {
  const state = await setup(page);
  await openDashboard(page);
  await expect(metric(page, 'Valor confirmado hoje')).toBeVisible();
  let release = () => {};
  state.gate = new Promise<void>((resolve) => {
    release = resolve;
  });
  state.status = 503;
  await page.getByRole('button', { name: 'Atualizar painel', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Atualizando…', exact: true })).toBeDisabled();
  await expect(page.getByRole('region', { name: 'Pedidos do dia' })).toHaveCount(0);
  release();
  await expect(page.getByRole('alert')).toBeVisible();
  state.status = 200;
  state.dashboard.orders.confirmedValue = 789.12;
  await page.getByRole('button', { name: 'Atualizar painel', exact: true }).click();
  await expect(metric(page, 'Valor confirmado hoje')).toContainText(/R\$\s*789,12/);
  expect(state.reads).toBe(3);
  expect(state.writes).toEqual([]);
});

test('Dashboard: indisponibilidade inicial não mostra números artificiais', async ({ page }) => {
  const state = await setup(page);
  state.status = 503;
  await openDashboard(page);
  await expect(page.getByRole('alert')).toContainText('O servidor não conseguiu concluir');
  await expect(page.getByRole('region')).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Atualizar painel', exact: true })).toBeEnabled();
});

test('Dashboard: sessão revogada remove os indicadores e retorna ao login', async ({ page }) => {
  const state = await setup(page);
  await openDashboard(page);
  await expect(metric(page, 'Valor confirmado hoje')).toBeVisible();
  state.status = 401;
  await page.getByRole('button', { name: 'Atualizar painel', exact: true }).click();
  await expect(page).toHaveURL(/\/entrar(?:\?.*)?$/);
  await expect(page.getByRole('region', { name: 'Pedidos do dia' })).toHaveCount(0);
});

for (const role of ['Attendant', 'Kitchen', 'Dispatch']) {
  test('Dashboard: ' + role + ' não acessa indicadores administrativos', async ({ page }) => {
    const state = await setup(page, role);
    await login(page);
    await expect(page.getByRole('link', { name: 'Dashboard', exact: true })).toHaveCount(0);
    await page.evaluate(() => {
      history.pushState(null, '', '/equipe/dashboard');
      dispatchEvent(new PopStateEvent('popstate'));
    });
    await expect(page).toHaveURL(/\/equipe$/);
    expect(state.reads).toBe(0);
  });
}

test('Dashboard: visitante precisa entrar antes de consultar', async ({ page }) => {
  const state = await setup(page);
  await page.goto('/equipe/dashboard');
  await expect(page).toHaveURL(/\/entrar(?:\?.*)?$/);
  expect(state.reads).toBe(0);
});
