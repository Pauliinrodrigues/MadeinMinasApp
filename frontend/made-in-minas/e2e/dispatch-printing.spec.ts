import { expect, Page, test } from '@playwright/test';
import type {
  DispatchOrder,
  DispatchStatusInput,
} from '../src/app/core/services/dispatch-api.service';

const timestamp = new Date().toISOString();
function order(number = 1542, delivery = true): DispatchOrder {
  return {
    id: 'order-' + number,
    number,
    status: 'Ready',
    version: 4,
    fulfillment: delivery ? 'Delivery' : 'Pickup',
    customerName: 'Maria reservada',
    customerPhone: '+5531999991234',
    address: delivery
      ? {
          id: 'address',
          street: 'Rua A',
          number: '12',
          neighborhood: 'Centro',
          city: 'Belo Horizonte',
          state: 'MG',
          complement: 'Casa',
          postalCode: '30000000',
          reference: 'Portão verde',
        }
      : null,
    items: [
      {
        productId: 'product',
        name: 'Uai Sô',
        quantity: 2,
        notes: 'Sem cebola',
        unitPrice: 29.9,
        lineTotal: 59.8,
      },
    ],
    notes: 'Embalar separado',
    subtotal: 59.8,
    deliveryFee: delivery ? 5 : 0,
    total: delivery ? 64.8 : 59.8,
    createdAt: timestamp,
    updatedAt: timestamp,
    readyAt: timestamp,
    payment: {
      method: 'Pix',
      status: 'Received',
      amount: delivery ? 64.8 : 59.8,
      cashTendered: null,
      changeAmount: null,
    },
  };
}
async function setup(page: Page, role = 'Dispatch') {
  await page.clock.install();
  const state = {
    orders: [order()],
    queries: [] as string[],
    changes: [] as DispatchStatusInput[],
    printQueries: [] as string[],
    readStatus: 200,
    writeStatus: 200,
    printStatus: 200,
    loseResponse: false,
    gate: null as Promise<void> | null,
  };
  const permissions: Record<string, string[]> = {
    Administrator: ['dispatch.work', 'printing.dispatch', 'printing.kitchen', 'orders.manage'],
    Dispatch: ['dispatch.work', 'printing.dispatch'],
    Attendant: ['orders.manage', 'printing.kitchen', 'printing.dispatch'],
    Kitchen: ['kitchen.work', 'printing.kitchen'],
  };
  const profile = {
    id: 'staff',
    name: 'Equipe Teste',
    username: 'staff',
    role,
    permissions: permissions[role],
  };
  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const json = (body: unknown, status = 200) => route.fulfill({ json: body, status });
    if (url.pathname === '/api/auth/login') {
      return json({
        accessToken: 'dispatch-token',
        expiresAt: new Date(Date.now() + 900000).toISOString(),
        tokenType: 'Bearer',
        user: profile,
      });
    }
    expect(request.headers()['authorization']).toBe('Bearer dispatch-token');
    if (url.pathname === '/api/auth/me') {
      return json(profile);
    }
    if (url.pathname === '/api/auth/logout') {
      return json({});
    }
    if (url.pathname === '/api/dispatch/orders') {
      state.queries.push(url.search);
      if (state.readStatus !== 200) {
        return json({}, state.readStatus);
      }
      const filtered = state.orders.filter(
        (order) => order.status === url.searchParams.get('status'),
      );
      const current = Math.min(
        Number(url.searchParams.get('page')),
        Math.max(1, Math.ceil(filtered.length / 20)),
      );
      return json({
        serverTime: timestamp,
        items: filtered.slice((current - 1) * 20, current * 20),
        page: current,
        pageSize: 20,
        totalCount: filtered.length,
      });
    }
    const change = url.pathname.match(/^\/api\/dispatch\/orders\/([^/]+)\/status$/);
    if (change) {
      const input = request.postDataJSON() as DispatchStatusInput;
      state.changes.push(input);
      if (state.gate) {
        await state.gate;
      }
      if (state.writeStatus !== 200) {
        return json({ code: 'OrderVersionConflict' }, state.writeStatus);
      }
      const target = state.orders.find((order) => order.id === change[1])!;
      target.status = input.status;
      target.version++;
      return state.loseResponse ? json({}, 503) : json(target);
    }
    const print = url.pathname.match(/^\/api\/print\/orders\/([^/]+)\/(kitchen|dispatch)$/);
    if (print) {
      state.printQueries.push(url.pathname);
      if (state.printStatus !== 200) {
        return json({}, state.printStatus);
      }
      const target = state.orders.find((order) => order.id === print[1])!;
      return json({
        generatedAt: timestamp,
        order:
          print[2] === 'dispatch'
            ? target
            : {
                id: target.id,
                number: target.number,
                status: target.status,
                version: target.version,
                fulfillment: target.fulfillment,
                createdAt: target.createdAt,
                notes: target.notes,
                items: target.items.map((item, index) => ({
                  position: index + 1,
                  name: item.name,
                  quantity: item.quantity,
                  notes: item.notes,
                })),
              },
      });
    }
    return json({}, 404);
  });
  return state;
}
async function login(page: Page) {
  await page.goto('/entrar?returnUrl=%2Fequipe');
  await page.getByLabel('Login', { exact: true }).fill('staff');
  await page.getByLabel('Senha', { exact: true }).fill('Senha apenas para testes');
  await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await expect(page).toHaveURL(/\/equipe$/);
}
async function navigate(page: Page, path: string) {
  await page.evaluate((path) => {
    history.pushState(null, '', path);
    dispatchEvent(new PopStateEvent('popstate'));
  }, path);
}
async function open(page: Page) {
  await login(page);
  await page.getByRole('link', { name: 'Expedição', exact: true }).click();
  await expect(page.getByRole('article', { name: 'Pedido 1542' })).toBeVisible();
}
async function advance(page: Page, action: string) {
  await page.getByRole('button', { name: action, exact: true }).click();
  await page.getByRole('button', { name: 'Confirmar etapa', exact: true }).click();
  await expect(page.getByRole('article', { name: 'Pedido 1542' })).toHaveCount(0);
}
for (const delivery of [true, false]) {
  test('expedição: fluxo completo ' + (delivery ? 'entrega' : 'retirada'), async ({ page }) => {
    const errors: string[] = [];
    page.on('pageerror', (error) => errors.push(error.message));
    const state = await setup(page);
    state.orders = [order(1542, delivery)];
    await open(page);
    await expect(page.getByRole('article')).toContainText('Sem cebola');
    if (delivery) {
      await expect(page.getByRole('article')).toContainText('Portão verde');
      await advance(page, 'Liberar para entrega');
      await page.getByRole('button', { name: 'Aguardando entrega', exact: true }).click();
      await advance(page, 'Registrar saída');
      await page.getByRole('button', { name: 'Saiu para entrega', exact: true }).click();
    }
    await advance(page, delivery ? 'Confirmar entrega' : 'Registrar retirada');
    await page.getByRole('button', { name: 'Entregue', exact: true }).click();
    await advance(page, 'Finalizar pedido');
    expect(state.changes.map((input) => input.status)).toEqual(
      delivery
        ? ['AwaitingDelivery', 'OutForDelivery', 'Delivered', 'Finalized']
        : ['Delivered', 'Finalized'],
    );
    expect(state.changes[0].expectedVersion).toBe(4);
    expect(errors).toEqual([]);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(
      true,
    );
  });
}
test('expedição: pagamento pendente bloqueia finalização até nova consulta', async ({ page }) => {
  const state = await setup(page);
  state.orders[0].status = 'Delivered';
  state.orders[0].payment!.status = 'Pending';
  await login(page);
  await page.getByRole('link', { name: 'Expedição', exact: true }).click();
  await page.getByRole('button', { name: 'Entregue', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Finalizar pedido' })).toBeDisabled();
  await expect(page.getByText('Solicite ao atendimento', { exact: false })).toBeVisible();
  state.orders[0].payment!.status = 'Received';
  await page.getByRole('button', { name: 'Atualizar expedição' }).click();
  await expect(page.getByRole('button', { name: 'Finalizar pedido' })).toBeEnabled();
});
test('expedição: pagina e atualização automática remove pedido; sair encerra consultas', async ({
  page,
}) => {
  const state = await setup(page);
  for (let index = 0; index < 20; index++) {
    state.orders.push(order(1600 + index));
  }
  await open(page);
  await page.getByRole('button', { name: 'Próxima', exact: true }).click();
  await expect(page.getByRole('article', { name: 'Pedido 1619' })).toBeVisible();
  expect(state.queries.at(-1)).toContain('page=2');
  state.orders.pop();
  await page.clock.fastForward(11000);
  await expect(page.getByText('Página 1', { exact: true })).toBeVisible();
  await page.getByRole('link', { name: 'Minha conta', exact: true }).click();
  await expect(page).toHaveURL(/\/equipe$/);
  const count = state.queries.length;
  await page.clock.fastForward(31000);
  expect(state.queries).toHaveLength(count);
});
test('expedição: falha e confirmação expirada bloqueiam ações', async ({ page }) => {
  const state = await setup(page);
  await open(page);
  state.readStatus = 503;
  await page.getByRole('button', { name: 'Atualizar expedição' }).click();
  await expect(page.getByRole('alert')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Liberar para entrega' })).toBeDisabled();
  state.readStatus = 200;
  await page.getByRole('button', { name: 'Atualizar expedição' }).click();
  await page.getByRole('button', { name: 'Liberar para entrega' }).click();
  await page.clock.fastForward(31000);
  await expect(page.getByRole('button', { name: 'Confirmar etapa' })).toBeDisabled();
});
test('expedição: envio bloqueia repetição e conflito exige consulta', async ({ page }) => {
  const state = await setup(page);
  state.writeStatus = 409;
  let release = () => {};
  state.gate = new Promise<void>((resolve) => {
    release = resolve;
  });
  await open(page);
  await page.getByRole('button', { name: 'Liberar para entrega' }).click();
  await page.getByRole('button', { name: 'Confirmar etapa' }).click();
  await expect(page.getByRole('button', { name: 'Salvando…' })).toBeDisabled();
  release();
  await expect(page.getByRole('alert')).toContainText('Atualize');
  await expect(page.getByRole('button', { name: 'Liberar para entrega' })).toBeDisabled();
  expect(state.changes).toHaveLength(1);
});
test('expedição: resposta perdida e sessão revogada', async ({ page }) => {
  const state = await setup(page);
  state.loseResponse = true;
  await open(page);
  await page.getByRole('button', { name: 'Liberar para entrega' }).click();
  await page.getByRole('button', { name: 'Confirmar etapa' }).click();
  await expect(page.getByRole('alert')).toContainText('confira o status');
  await page.getByRole('button', { name: 'Atualizar expedição' }).click();
  await expect(page.getByRole('article')).toHaveCount(0);
  expect(state.changes).toHaveLength(1);
  state.readStatus = 401;
  await page.clock.fastForward(11000);
  await expect(page).toHaveURL(/\/entrar(?:\?.*)?$/);
});
for (const role of ['Attendant', 'Kitchen']) {
  test('expedição: guard impede acesso de ' + role, async ({ page }) => {
    const state = await setup(page, role);
    await login(page);
    await navigate(page, '/equipe/expedicao');
    await expect(page).toHaveURL(/\/equipe$/);
    expect(state.queries).toHaveLength(0);
  });
}
for (const mode of ['kitchen', 'dispatch']) {
  test(
    'impressão: via ' + mode + ', diálogo explícito e layout sem controles',
    async ({ page }) => {
      const state = await setup(page, mode === 'kitchen' ? 'Kitchen' : 'Dispatch');
      state.orders[0].notes =
        '<img src=x onerror=alert(1)> Observação longa ' + 'conferir '.repeat(60);
      await login(page);
      await navigate(page, '/comanda/order-1542/' + mode + '?from=' + mode);
      const preview = page.getByRole('article', { name: 'Prévia da comanda' });
      await expect(preview).toContainText('2 × Uai Sô');
      await expect(preview.locator('img')).toHaveCount(0);
      if (mode === 'kitchen') {
        await expect(preview).not.toContainText('Maria reservada');
        await expect(preview).not.toContainText('64,80');
      } else {
        await expect(preview).toContainText('Maria reservada');
        await expect(preview).toContainText('RECEBIDO');
      }
      await page.evaluate(() => {
        document.documentElement.dataset['printCalls'] = '0';
        window.print = () => {
          document.documentElement.dataset['printCalls'] = String(
            Number(document.documentElement.dataset['printCalls']) + 1,
          );
        };
      });
      await page.getByRole('button', { name: 'Imprimir comanda', exact: true }).click();
      expect(await page.evaluate(() => document.documentElement.dataset['printCalls'])).toBe('1');
      await expect(page.getByRole('status')).toContainText('não confirma impressão');
      for (const width of ['58', '80', 'A4']) {
        await page.getByLabel('Largura do papel').selectOption(width);
        await page.emulateMedia({ media: 'print' });
        await expect(
          page.getByRole('button', { name: 'Imprimir comanda', exact: true }),
        ).toBeHidden();
        await expect(preview).toBeVisible();
        expect(await preview.evaluate((el) => el.scrollWidth <= el.clientWidth)).toBe(true);
        await page.emulateMedia({ media: 'screen' });
      }
      expect(state.changes).toHaveLength(0);
      await page.clock.fastForward(31000);
      await expect(
        page.getByRole('button', { name: 'Imprimir comanda', exact: true }),
      ).toBeDisabled();
    },
  );
}
test('impressão: falha remove documento antigo e recuperação mostra cancelamento', async ({
  page,
}) => {
  const state = await setup(page);
  await open(page);
  await page.getByRole('link', { name: 'Comanda de expedição' }).click();
  await expect(page.getByRole('article')).toContainText('Pedido #1542');
  state.printStatus = 503;
  await page.getByRole('button', { name: 'Atualizar comanda' }).click();
  await expect(page.getByRole('alert')).toBeVisible();
  await expect(page.getByRole('article')).toHaveCount(0);
  state.printStatus = 200;
  state.orders[0].status = 'Cancelled';
  await page.getByRole('button', { name: 'Atualizar comanda' }).click();
  await expect(page.getByRole('article')).toContainText('CANCELADO — NÃO PRODUZIR / NÃO ENTREGAR');
  state.printStatus = 401;
  await page.getByRole('button', { name: 'Atualizar comanda' }).click();
  await expect(page).toHaveURL(/\/entrar(?:\?.*)?$/);
  await expect(page.getByRole('article')).toHaveCount(0);
});
for (const [role, mode] of [
  ['Kitchen', 'dispatch'],
  ['Dispatch', 'kitchen'],
]) {
  test('impressão: ' + role + ' não acessa via ' + mode, async ({ page }) => {
    const state = await setup(page, role);
    await login(page);
    await navigate(page, '/comanda/order-1542/' + mode);
    await expect(page).toHaveURL(/\/equipe$/);
    expect(state.printQueries).toHaveLength(0);
  });
}

test('impressão: trocar pedido e via recarrega a prévia sem reutilizar dados', async ({ page }) => {
  const state = await setup(page, 'Administrator');
  state.orders.push(order(1543, false));
  await login(page);
  await navigate(page, '/comanda/order-1542/dispatch');
  await expect(page.getByRole('article')).toContainText('Maria reservada');
  await navigate(page, '/comanda/order-1543/kitchen');
  await expect(page.getByRole('article')).toContainText('Pedido #1543');
  await expect(page.getByRole('article')).not.toContainText('Maria reservada');
  expect(state.printQueries).toHaveLength(2);
});
