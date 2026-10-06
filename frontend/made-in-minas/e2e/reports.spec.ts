import { expect, Page, test } from '@playwright/test';
import type { SalesMetrics, SalesReport } from '../src/app/core/services/reports-api.service';

test.use({ timezoneId: 'Asia/Tokyo' });

const empty: SalesMetrics = {
  created: 0,
  createdAndCancelled: 0,
  confirmed: 0,
  confirmedValue: 0,
  averageTicket: null,
  received: 0,
  refunded: 0,
  netReceived: 0,
};
const firstDay: SalesMetrics = {
  created: 2,
  createdAndCancelled: 0,
  confirmed: 1,
  confirmedValue: 20,
  averageTicket: 20,
  received: 10,
  refunded: 0,
  netReceived: 10,
};
const secondDay: SalesMetrics = {
  created: 3,
  createdAndCancelled: 1,
  confirmed: 2,
  confirmedValue: 70,
  averageTicket: 35,
  received: 50,
  refunded: 20,
  netReceived: 30,
};
const original: SalesReport = {
  startDate: '2026-09-26',
  endDate: '2026-10-02',
  timeZone: 'America/Sao_Paulo',
  startsAt: '2026-09-26T03:00:00Z',
  endsAt: '2026-10-03T03:00:00Z',
  calculatedAt: '2026-10-03T02:30:00Z',
  summary: {
    created: 5,
    createdAndCancelled: 1,
    confirmed: 3,
    confirmedValue: 90,
    averageTicket: 30,
    received: 60,
    refunded: 20,
    netReceived: 40,
  },
  days: [
    { date: '2026-09-26', metrics: firstDay },
    { date: '2026-09-27', metrics: secondDay },
    ...['2026-09-28', '2026-09-29', '2026-09-30', '2026-10-01', '2026-10-02'].map((date) => ({
      date,
      metrics: empty,
    })),
  ],
  paymentMethods: [
    { method: 'Cash', received: 10, refunded: 0, netReceived: 10 },
    { method: 'Pix', received: 50, refunded: 20, netReceived: 30 },
    { method: 'CreditCard', received: 0, refunded: 0, netReceived: 0 },
    { method: 'DebitCard', received: 0, refunded: 0, netReceived: 0 },
  ],
  topProducts: [{ productId: 'product-1', productName: 'Uai Sô', quantity: 4, itemValue: 80 }],
};

async function setup(page: Page, role = 'Administrator') {
  const state = {
    report: structuredClone(original),
    status: 200,
    queries: [] as string[],
    writes: [] as string[],
    gate: null as Promise<void> | null,
  };
  const profile = {
    id: 'staff',
    name: 'Equipe',
    username: 'staff',
    role,
    permissions: role === 'Administrator' ? ['reports.view'] : [],
  };
  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const json = (body: unknown, status = 200) => route.fulfill({ status, json: body });
    if (url.pathname === '/api/auth/login') {
      return json({
        accessToken: 'reports-token',
        tokenType: 'Bearer',
        expiresAt: new Date(Date.now() + 900000).toISOString(),
        user: profile,
      });
    }
    expect(request.headers()['authorization']).toBe('Bearer reports-token');
    if (url.pathname === '/api/auth/me') {
      return json(profile);
    }
    if (request.method() !== 'GET') {
      state.writes.push(url.pathname);
    }
    if (url.pathname === '/api/reports/sales') {
      state.queries.push(url.search);
      if (state.gate) {
        await state.gate;
      }
      const response = structuredClone(state.report);
      if (url.searchParams.has('startDate')) {
        response.startDate = url.searchParams.get('startDate')!;
        response.endDate = url.searchParams.get('endDate')!;
      }
      return state.status === 200 ? json(response) : json({}, state.status);
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

async function openReports(page: Page) {
  await login(page);
  await page.getByRole('link', { name: 'Relatórios', exact: true }).click();
  await expect(
    page.getByRole('heading', { name: 'Relatórios por período', exact: true }),
  ).toBeVisible();
}

function metric(page: Page, label: string) {
  return page.locator('dl > div').filter({ has: page.getByText(label, { exact: true }) });
}

test('Relatórios: resumo, dias vazios e formas de pagamento com datas de Brasília', async ({
  page,
}) => {
  const errors: string[] = [];
  page.on('pageerror', (error) => errors.push(error.message));
  const state = await setup(page);
  await openReports(page);
  await expect(page.getByLabel('Data inicial', { exact: true })).toHaveValue('2026-09-26');
  await expect(page.getByLabel('Data final', { exact: true })).toHaveValue('2026-10-02');
  await expect(page.getByText(/Período consultado:/)).toContainText('02/10/2026, 23:30:00');
  await expect(metric(page, 'Valor confirmado')).toContainText(/R\$\s*90,00/);
  await expect(metric(page, 'Ticket médio confirmado')).toContainText(/R\$\s*30,00/);
  await expect(metric(page, 'Recebido no período')).toContainText(/R\$\s*60,00/);
  await expect(metric(page, 'Estornado no período')).toContainText(/R\$\s*20,00/);
  await expect(metric(page, 'Recebido menos estornos')).toContainText(/R\$\s*40,00/);
  const daily = page.getByRole('table', { name: 'Vendas e recebimentos por dia de Brasília' });
  await expect(daily.getByRole('row')).toHaveCount(8);
  await expect(daily.getByRole('row').filter({ hasText: '28/09/2026' })).toContainText(
    'Sem confirmações',
  );
  const methods = page.getByRole('table', {
    name: 'Recebimentos e estornos por forma de pagamento',
  });
  await expect(methods.getByRole('row')).toHaveCount(5);
  await expect(methods.getByRole('row').filter({ hasText: 'Pix' })).toContainText(/R\$\s*30,00/);
  await expect(page.getByRole('listitem').first()).toContainText('4 un.');
  expect(state.queries).toEqual(['']);
  expect(state.writes).toEqual([]);
  expect(errors).toEqual([]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('Relatórios: alterar datas remove resultado antigo e consulta o intervalo exato', async ({
  page,
}) => {
  const state = await setup(page);
  await openReports(page);
  await expect(metric(page, 'Valor confirmado')).toBeVisible();
  await page.getByLabel('Data inicial', { exact: true }).fill('2026-10-01');
  await expect(page.getByRole('region', { name: 'Resumo do período', exact: true })).toHaveCount(0);
  await expect(page.getByRole('status')).toContainText('Datas alteradas');
  await page.getByLabel('Data final', { exact: true }).fill('2026-10-01');
  state.report.summary.confirmedValue = 125.37;
  await page.getByRole('button', { name: 'Gerar relatório', exact: true }).click();
  await expect(metric(page, 'Valor confirmado')).toContainText(/R\$\s*125,37/);
  await expect(page.getByText(/Período consultado:/)).toContainText('01/10/2026 a 01/10/2026');
  expect(state.queries).toEqual(['', '?startDate=2026-10-01&endDate=2026-10-01']);
});

for (const [label, first, last] of [
  ['sem início', '', '2026-10-01'],
  ['invertido', '2026-10-02', '2026-10-01'],
  ['acima de 90 dias', '2026-01-01', '2026-04-01'],
]) {
  test('Relatórios: período ' + label + ' é rejeitado antes da consulta', async ({ page }) => {
    const state = await setup(page);
    await openReports(page);
    await expect(metric(page, 'Valor confirmado')).toBeVisible();
    await page.getByLabel('Data inicial', { exact: true }).fill(first);
    await page.getByLabel('Data final', { exact: true }).fill(last);
    await page.getByRole('button', { name: 'Gerar relatório', exact: true }).click();
    await expect(page.getByRole('alert')).toContainText('no máximo 90 dias');
    expect(state.queries).toEqual(['']);
    await expect(page.getByRole('region', { name: 'Resumo do período', exact: true })).toHaveCount(
      0,
    );
  });
}

test('Relatórios: últimos sete dias usam as datas retornadas pelo servidor', async ({ page }) => {
  const state = await setup(page);
  await openReports(page);
  await expect(metric(page, 'Valor confirmado')).toBeVisible();
  await page.getByLabel('Data inicial', { exact: true }).fill('2026-01-01');
  await page.getByRole('button', { name: 'Últimos 7 dias', exact: true }).click();
  await expect(page.getByLabel('Data inicial', { exact: true })).toHaveValue('2026-09-26');
  expect(state.queries).toEqual(['', '']);
});

test('Relatórios: sem confirmações mantém médias ausentes e pode ter estorno negativo', async ({
  page,
}) => {
  const state = await setup(page);
  state.report.summary = { ...empty, refunded: 20, netReceived: -20 };
  state.report.topProducts = [];
  await openReports(page);
  await expect(metric(page, 'Ticket médio confirmado')).toContainText('Sem pedidos confirmados');
  await expect(metric(page, 'Recebido menos estornos')).toContainText(/-R\$\s*20,00/);
  await expect(page.getByText('Nenhum produto em pedidos confirmados no período.')).toBeVisible();
});

test('Relatórios: carregamento bloqueia duplicação e falha permite repetir as mesmas datas', async ({
  page,
}) => {
  const state = await setup(page);
  await openReports(page);
  await expect(metric(page, 'Valor confirmado')).toBeVisible();
  let release = () => {};
  state.gate = new Promise<void>((resolve) => {
    release = resolve;
  });
  state.status = 503;
  await page.getByRole('button', { name: 'Gerar relatório', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Consultando…', exact: true })).toBeDisabled();
  await expect(page.getByLabel('Data inicial', { exact: true })).toBeDisabled();
  await expect(page.getByRole('button', { name: 'Últimos 7 dias', exact: true })).toBeDisabled();
  await expect(page.getByRole('region', { name: 'Resumo do período', exact: true })).toHaveCount(0);
  release();
  await expect(page.getByRole('alert')).toBeVisible();
  state.status = 200;
  await page.getByRole('button', { name: 'Gerar relatório', exact: true }).click();
  await expect(metric(page, 'Valor confirmado')).toBeVisible();
  expect(state.queries).toEqual([
    '',
    '?startDate=2026-09-26&endDate=2026-10-02',
    '?startDate=2026-09-26&endDate=2026-10-02',
  ]);
  expect(state.writes).toEqual([]);
});

test('Relatórios: falha inicial pode ser recuperada pelo período padrão', async ({ page }) => {
  const state = await setup(page);
  state.status = 503;
  await openReports(page);
  await expect(page.getByRole('alert')).toBeVisible();
  await expect(page.getByRole('table')).toHaveCount(0);
  state.status = 200;
  await page.getByRole('button', { name: 'Últimos 7 dias', exact: true }).click();
  await expect(metric(page, 'Valor confirmado')).toBeVisible();
});

test('Relatórios: sessão revogada remove todos os resultados', async ({ page }) => {
  const state = await setup(page);
  await openReports(page);
  await expect(metric(page, 'Valor confirmado')).toBeVisible();
  state.status = 401;
  await page.getByRole('button', { name: 'Gerar relatório', exact: true }).click();
  await expect(page).toHaveURL(/\/entrar(?:\?.*)?$/);
  await expect(page.getByRole('table')).toHaveCount(0);
});

for (const role of ['Attendant', 'Kitchen', 'Dispatch']) {
  test('Relatórios: ' + role + ' não acessa dados administrativos', async ({ page }) => {
    const state = await setup(page, role);
    await login(page);
    await expect(page.getByRole('link', { name: 'Relatórios', exact: true })).toHaveCount(0);
    await page.evaluate(() => {
      history.pushState(null, '', '/equipe/relatorios');
      dispatchEvent(new PopStateEvent('popstate'));
    });
    await expect(page).toHaveURL(/\/equipe$/);
    expect(state.queries).toEqual([]);
  });
}

test('Relatórios: visitante é direcionado ao login', async ({ page }) => {
  const state = await setup(page);
  await page.goto('/equipe/relatorios');
  await expect(page).toHaveURL(/\/entrar(?:\?.*)?$/);
  expect(state.queries).toEqual([]);
});

test('Relatórios: nomes extensos são texto e tabelas não alargam a página', async ({ page }) => {
  const state = await setup(page);
  state.report.topProducts[0].productName = '<script>alert(1)</script>' + 'Produto'.repeat(14);
  await openReports(page);
  await expect(page.getByRole('listitem').first()).toContainText(
    state.report.topProducts[0].productName,
  );
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});
