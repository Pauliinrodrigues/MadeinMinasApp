import { expect, Page, test } from '@playwright/test';
import type {
  KitchenOrder,
  KitchenStatus,
  KitchenStatusInput,
} from '../src/app/core/services/kitchen-api.service';

const now = Date.now();
const timestamp = new Date(now).toISOString();
function order(number = 1542, status: KitchenStatus = 'Confirmed'): KitchenOrder {
  return {
    id: 'order-' + number,
    number,
    fulfillment: 'Delivery',
    status,
    version: status === 'Confirmed' ? 2 : status === 'InPreparation' ? 3 : 4,
    createdAt: new Date(now - 900000).toISOString(),
    confirmedAt: new Date(now - 600000).toISOString(),
    preparationStartedAt: status === 'Confirmed' ? null : new Date(now - 300000).toISOString(),
    readyAt: status === 'Ready' ? new Date(now - 60000).toISOString() : null,
    notes: 'Embalagem separada',
    items: [{ position: 1, name: 'Uai Sô', quantity: 2, notes: 'Sem cebola\nMolho à parte' }],
  };
}
async function setup(page: Page, role = 'Kitchen') {
  await page.clock.install();
  const state = {
    orders: [order(), order(1543, 'InPreparation'), order(1544, 'Ready')],
    queries: [] as string[],
    changes: [] as { id: string; input: KitchenStatusInput }[],
    boardStatus: 200,
    writeStatus: 200,
    gate: null as Promise<void> | null,
    boardGate: null as Promise<void> | null,
    loseResponse: false,
  };
  const profile = {
    id: 'staff',
    name: 'Equipe Teste',
    username: 'staff',
    role,
    permissions: ['Administrator', 'Kitchen'].includes(role) ? ['kitchen.work'] : [],
  };
  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const json = (body: unknown, status = 200) => route.fulfill({ status, json: body });
    if (url.pathname === '/api/auth/login') {
      return json({
        accessToken: 'kitchen-token',
        expiresAt: new Date(Date.now() + 900000).toISOString(),
        tokenType: 'Bearer',
        user: profile,
      });
    }
    expect(request.headers()['authorization']).toBe('Bearer kitchen-token');
    if (url.pathname === '/api/auth/me') {
      return json(profile);
    }
    if (url.pathname === '/api/auth/logout') {
      return json({});
    }
    if (url.pathname === '/api/kitchen/orders') {
      state.queries.push(url.search);
      if (state.boardGate) {
        await state.boardGate;
      }
      if (state.boardStatus !== 200) {
        return json({}, state.boardStatus);
      }
      const statuses: KitchenStatus[] = ['Confirmed', 'InPreparation', 'Ready'];
      const columns = statuses.map((status, index) => {
        const items = state.orders.filter((order) => order.status === status);
        const wanted = Number(
          url.searchParams.get(['confirmedPage', 'preparingPage', 'readyPage'][index]) ?? 1,
        );
        const current = Math.min(wanted, Math.max(1, Math.ceil(items.length / 20)));
        return {
          status,
          items: items.slice((current - 1) * 20, current * 20),
          page: current,
          pageSize: 20,
          totalCount: items.length,
        };
      });
      return json({ serverTime: timestamp, columns });
    }
    const match = url.pathname.match(/^\/api\/kitchen\/orders\/([^/]+)\/status$/);
    if (match) {
      const input = request.postDataJSON() as KitchenStatusInput;
      state.changes.push({ id: match[1], input });
      if (state.gate) {
        await state.gate;
      }
      if (state.writeStatus !== 200) {
        return json({ code: 'OrderVersionConflict' }, state.writeStatus);
      }
      const order = state.orders.find((order) => order.id === match[1])!;
      order.status = input.status;
      order.version++;
      if (input.status === 'InPreparation') {
        order.preparationStartedAt = timestamp;
      } else {
        order.readyAt = timestamp;
      }
      if (state.loseResponse) {
        return json({}, 503);
      }
      return json(order);
    }
    return json({}, 404);
  });
  return state;
}
async function login(page: Page) {
  await page.goto('/entrar');
  await page.getByLabel('Login', { exact: true }).fill('staff');
  await page.getByLabel('Senha', { exact: true }).fill('Senha apenas para testes');
  await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await expect(page).toHaveURL(/\/equipe$/);
}
async function open(page: Page) {
  await login(page);
  await page.getByRole('link', { name: 'Cozinha', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Cozinha', exact: true })).toBeVisible();
  await expect(page.getByRole('region', { name: 'Confirmado', exact: true })).toBeVisible();
}

for (const role of ['Kitchen', 'Administrator']) {
  test('cozinha: ' + role + ' vê produção e avança com confirmação', async ({ page }) => {
    const errors: string[] = [];
    page.on('pageerror', (error) => errors.push(error.message));
    const state = await setup(page, role);
    await open(page);
    const card = page.getByRole('article', { name: 'Pedido 1542', exact: true });
    await expect(card).toContainText('2 × Uai Sô');
    await expect(card).toContainText('Sem cebola');
    await expect(card).toContainText('Molho à parte');
    await expect(card).toContainText('Espera até iniciar: 10 min');
    await expect(page.getByRole('article', { name: 'Pedido 1544', exact: true })).toContainText(
      'Preparo: 4 min',
    );
    await page.getByRole('button', { name: 'Iniciar preparo do pedido 1542', exact: true }).click();
    expect(state.changes).toHaveLength(0);
    await page.getByRole('button', { name: 'Voltar', exact: true }).click();
    await page.getByRole('button', { name: 'Iniciar preparo do pedido 1542', exact: true }).click();
    await page.getByRole('button', { name: 'Confirmar etapa', exact: true }).click();
    await expect(page.getByRole('region', { name: 'Em preparação', exact: true })).toContainText(
      '#1542',
    );
    await page.getByRole('button', { name: 'Marcar pronto o pedido 1542', exact: true }).click();
    await expect(
      page.getByRole('region', { name: 'Confirmar etapa de produção', exact: true }),
    ).toContainText('Todos os itens');
    await page.getByRole('button', { name: 'Confirmar etapa', exact: true }).click();
    await expect(page.getByRole('region', { name: 'Pronto', exact: true })).toContainText('#1542');
    expect(state.changes).toEqual([
      { id: 'order-1542', input: { status: 'InPreparation', expectedVersion: 2 } },
      { id: 'order-1542', input: { status: 'Ready', expectedVersion: 3 } },
    ]);
    await expect(card.getByRole('button')).toHaveCount(0);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(
      true,
    );
    expect(errors).toEqual([]);
  });
}

test('cozinha: pagina cada coluna sem esconder pedidos de dias anteriores', async ({ page }) => {
  const state = await setup(page);
  for (let index = 0; index < 20; index++) {
    state.orders.push(order(1600 + index));
  }
  state.orders[0].confirmedAt = new Date(now - 2 * 86400000).toISOString();
  await open(page);
  await expect(page.getByRole('article', { name: 'Pedido 1542' })).toContainText('2880 min');
  const column = page.getByRole('region', { name: 'Confirmado', exact: true });
  await column.getByRole('button', { name: 'Próxima', exact: true }).click();
  await expect(column.getByRole('article', { name: 'Pedido 1619' })).toBeVisible();
  expect(state.queries.at(-1)).toContain('confirmedPage=2');
  expect(state.queries.at(-1)).toContain('preparingPage=1');
  await column.getByRole('button', { name: 'Anterior', exact: true }).click();
  await expect(column.getByRole('article', { name: 'Pedido 1542' })).toBeVisible();
});

test('cozinha: atualização automática remove cancelados e para ao sair da tela', async ({
  page,
}) => {
  const state = await setup(page);
  await open(page);
  state.orders = state.orders.filter((order) => order.number !== 1542);
  await page.clock.fastForward(11000);
  await expect(page.getByRole('article', { name: 'Pedido 1542', exact: true })).toHaveCount(0);
  expect(state.queries.length).toBeGreaterThan(1);
  await page.getByRole('link', { name: 'Minha conta', exact: true }).click();
  await expect(page).toHaveURL(/\/equipe$/);
  const requests = state.queries.length;
  await page.clock.fastForward(31000);
  expect(state.queries).toHaveLength(requests);
});

test('cozinha: falha de consulta preserva cards e bloqueia ações até recuperar', async ({
  page,
}) => {
  const state = await setup(page);
  await open(page);
  state.boardStatus = 503;
  await page.getByRole('button', { name: 'Atualizar painel', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('Painel sem atualização');
  await expect(page.getByRole('article', { name: 'Pedido 1542' })).toBeVisible();
  await expect(
    page.getByRole('button', { name: 'Iniciar preparo do pedido 1542', exact: true }),
  ).toBeDisabled();
  state.boardStatus = 200;
  await page.getByRole('button', { name: 'Atualizar painel', exact: true }).click();
  await expect(
    page.getByRole('button', { name: 'Iniciar preparo do pedido 1542', exact: true }),
  ).toBeEnabled();
});

test('cozinha: confirmação antiga exige atualizar o painel', async ({ page }) => {
  await setup(page);
  await open(page);
  await page.getByRole('button', { name: 'Iniciar preparo do pedido 1542', exact: true }).click();
  await page.clock.fastForward(31000);
  await expect(page.getByRole('alert')).toContainText('Dados desatualizados');
  await expect(page.getByRole('button', { name: 'Confirmar etapa', exact: true })).toBeDisabled();
  await page.getByRole('button', { name: 'Atualizar painel', exact: true }).click();
  await expect(page.getByRole('region', { name: 'Confirmar etapa de produção' })).toHaveCount(0);
  await expect(
    page.getByRole('button', { name: 'Iniciar preparo do pedido 1542', exact: true }),
  ).toBeEnabled();
});

test('cozinha: requisição em andamento não sobrepõe consultas e bloqueia comandos', async ({
  page,
}) => {
  const state = await setup(page);
  await open(page);
  let release = () => {};
  state.boardGate = new Promise<void>((resolve) => {
    release = resolve;
  });
  await page.getByRole('button', { name: 'Atualizar painel', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Atualizar painel', exact: true })).toBeDisabled();
  const queries = state.queries.length;
  await page.clock.fastForward(11000);
  expect(state.queries).toHaveLength(queries);
  await expect(
    page.getByRole('button', { name: 'Iniciar preparo do pedido 1542', exact: true }),
  ).toBeDisabled();
  release();
  await expect(
    page.getByRole('button', { name: 'Iniciar preparo do pedido 1542', exact: true }),
  ).toBeEnabled();
});

test('cozinha: envio duplo bloqueado e conflito consulta novo status', async ({ page }) => {
  const state = await setup(page);
  state.writeStatus = 409;
  let release = () => {};
  state.gate = new Promise<void>((resolve) => {
    release = resolve;
  });
  await open(page);
  await page.getByRole('button', { name: 'Iniciar preparo do pedido 1542', exact: true }).click();
  await page.getByRole('button', { name: 'Confirmar etapa', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Salvando…', exact: true })).toBeDisabled();
  await expect(page.getByRole('button', { name: 'Atualizar painel', exact: true })).toBeDisabled();
  release();
  await expect(page.getByRole('alert')).toContainText('outro atendimento');
  state.orders[0].status = 'InPreparation';
  state.orders[0].version = 3;
  await page.getByRole('button', { name: 'Atualizar painel', exact: true }).click();
  await expect(
    page.getByRole('button', { name: 'Iniciar preparo do pedido 1542', exact: true }),
  ).toHaveCount(0);
  await expect(
    page.getByRole('button', { name: 'Marcar pronto o pedido 1542', exact: true }),
  ).toBeEnabled();
  expect(state.changes).toHaveLength(1);
});

test('cozinha: resposta perdida exige consultar sem repetir a preparação', async ({ page }) => {
  const state = await setup(page);
  state.loseResponse = true;
  await open(page);
  await page.getByRole('button', { name: 'Iniciar preparo do pedido 1542', exact: true }).click();
  await page.getByRole('button', { name: 'Confirmar etapa', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('confira o status');
  await expect(
    page.getByRole('button', { name: 'Iniciar preparo do pedido 1542', exact: true }),
  ).toBeDisabled();
  await page.getByRole('button', { name: 'Atualizar painel', exact: true }).click();
  await expect(page.getByRole('region', { name: 'Em preparação', exact: true })).toContainText(
    '#1542',
  );
  expect(state.changes).toHaveLength(1);
});

for (const role of ['Attendant', 'Dispatch']) {
  test('cozinha: ' + role + ' não acessa o painel', async ({ page }) => {
    const state = await setup(page, role);
    await login(page);
    await expect(page.getByRole('link', { name: 'Cozinha', exact: true })).toHaveCount(0);
    await page.evaluate(() => {
      history.pushState(null, '', '/equipe/cozinha');
      dispatchEvent(new PopStateEvent('popstate'));
    });
    await expect(page).toHaveURL(/\/equipe$/);
    expect(state.queries).toHaveLength(0);
  });
}

test('cozinha: falha inicial tem recuperação e sessão revogada remove os dados', async ({
  page,
}) => {
  const state = await setup(page);
  state.boardStatus = 503;
  await login(page);
  await page.getByRole('link', { name: 'Cozinha', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('Painel sem atualização');
  state.boardStatus = 200;
  await page.getByRole('button', { name: 'Atualizar painel', exact: true }).click();
  await expect(page.getByRole('article', { name: 'Pedido 1542', exact: true })).toBeVisible();
  state.boardStatus = 401;
  await page.clock.fastForward(11000);
  await expect(page).toHaveURL(/\/entrar$/);
  await expect(page.getByRole('article', { name: 'Pedido 1542', exact: true })).toHaveCount(0);
});
