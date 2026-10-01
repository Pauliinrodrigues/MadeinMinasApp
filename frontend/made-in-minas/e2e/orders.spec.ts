import { expect, Page, test } from '@playwright/test';
import type { CartQuote, CartQuoteInput } from '../src/app/core/services/cart-api.service';
import type {
  CreateOrderInput,
  Order,
  OrderStatusInput,
} from '../src/app/core/services/order-api.service';

const now = '2026-10-01T12:00:00Z';
const customer = {
  id: 'customer',
  name: 'Maria',
  phone: '+5531999991234',
  isActive: true,
  createdAt: now,
  updatedAt: now,
};
const product = {
  id: 'product',
  name: 'Uai Sô',
  categoryId: 'category',
  categoryName: 'Lanches',
  description: null,
  price: 29.9,
};
const address = {
  id: 'address',
  customerId: customer.id,
  street: 'Rua A',
  number: '12',
  neighborhood: 'Centro',
  city: 'Belo Horizonte',
  state: 'MG',
  complement: null,
  postalCode: null,
  reference: 'Portão verde',
  isActive: true,
  createdAt: now,
  updatedAt: now,
};
const original: Order = {
  id: 'order-1',
  number: 1542,
  origin: 'Manual',
  status: 'New',
  version: 1,
  customer,
  fulfillment: 'Pickup',
  address: null,
  items: [
    {
      productId: product.id,
      name: product.name,
      quantity: 2,
      unitPrice: 29.9,
      lineTotal: 59.8,
      notes: 'Sem cebola',
    },
  ],
  notes: 'Embalar separado',
  subtotal: 59.8,
  deliveryFee: 0,
  total: 59.8,
  createdAt: now,
  updatedAt: now,
  history: [
    {
      version: 1,
      fromStatus: null,
      toStatus: 'New',
      actorId: 'staff',
      actorName: 'Equipe Teste',
      reason: null,
      occurredAt: now,
    },
  ],
};

async function setup(page: Page, role = 'Attendant') {
  const state = {
    orders: [structuredClone(original)],
    created: [] as CreateOrderInput[],
    changes: [] as OrderStatusInput[],
    queries: [] as string[],
    createStatus: 201,
    createCode: 'OrderReviewChanged',
    listStatus: 200,
    changeStatus: 200,
    createGate: null as Promise<void> | null,
    orderRequests: 0,
  };
  const profile = {
    id: 'staff',
    name: 'Equipe Teste',
    username: 'staff',
    role,
    permissions: ['Administrator', 'Attendant'].includes(role)
      ? ['orders.manage', 'customers.manage']
      : [],
  };
  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname;
    const json = (body: unknown, status = 200) => route.fulfill({ status, json: body });
    if (path === '/api/auth/login') {
      return json({
        accessToken: 'orders-token',
        expiresAt: new Date(Date.now() + 900000).toISOString(),
        tokenType: 'Bearer',
        user: profile,
      });
    }
    expect(request.headers()['authorization']).toBe('Bearer orders-token');
    if (path === '/api/auth/me') {
      return json(profile);
    }
    if (path === '/api/auth/logout') {
      return json({});
    }
    const pageOf = <T>(items: T[]) => {
      const current = Number(url.searchParams.get('page') ?? 1);
      return {
        items: items.slice((current - 1) * 20, current * 20),
        page: current,
        pageSize: 20,
        totalCount: items.length,
      };
    };
    if (path === '/api/cart/products') {
      return json(pageOf([product]));
    }
    if (path === '/api/customers') {
      return json(pageOf([customer]));
    }
    if (path === '/api/customers/customer/addresses') {
      return json(pageOf([address]));
    }
    if (path === '/api/cart/quote') {
      const input = request.postDataJSON() as CartQuoteInput;
      const items = input.items.map((item) => ({
        ...item,
        name: product.name,
        unitPrice: product.price,
        lineTotal: product.price * item.quantity,
      }));
      const subtotal = items.reduce((sum, item) => sum + item.lineTotal, 0);
      const quote: CartQuote = {
        customer,
        fulfillment: input.fulfillment,
        address: input.fulfillment === 'Delivery' ? address : null,
        items,
        notes: input.notes,
        subtotal,
        deliveryFee: input.deliveryFee,
        total: subtotal + input.deliveryFee,
        calculatedAt: now,
        reviewToken: 'A'.repeat(64),
      };
      return json(quote);
    }
    if (path.startsWith('/api/orders')) {
      state.orderRequests++;
    }
    if (path === '/api/orders' && request.method() === 'POST') {
      const input = request.postDataJSON() as CreateOrderInput;
      state.created.push(input);
      if (state.createGate) {
        await state.createGate;
      }
      if (state.createStatus >= 400) {
        return json({ code: state.createCode }, state.createStatus);
      }
      const order = state.orders[0];
      order.fulfillment = input.cart.fulfillment;
      order.address = input.cart.fulfillment === 'Delivery' ? address : null;
      order.deliveryFee = input.cart.deliveryFee;
      order.total = order.subtotal + input.cart.deliveryFee;
      return json(order, state.createStatus);
    }
    if (path === '/api/orders') {
      state.queries.push(url.search);
      if (state.listStatus !== 200) {
        return json({}, state.listStatus);
      }
      const search = (url.searchParams.get('search') ?? '').toLowerCase();
      const status = url.searchParams.get('status');
      const filtered = state.orders.filter(
        (order) =>
          (!status || order.status === status) &&
          (order.customer.name.toLowerCase().includes(search) ||
            String(order.number).includes(search)),
      );
      return json(
        pageOf(filtered.map((order) => ({ ...order, customerName: order.customer.name }))),
      );
    }
    const match = path.match(/^\/api\/orders\/([^/]+)(\/status)?$/);
    if (match) {
      const order = state.orders.find((item) => item.id === match[1]);
      if (!order) {
        return json({ code: 'OrderNotFound' }, 404);
      }
      if (request.method() === 'PUT') {
        const input = request.postDataJSON() as OrderStatusInput;
        state.changes.push(input);
        if (state.changeStatus !== 200) {
          return json({ code: 'OrderVersionConflict' }, state.changeStatus);
        }
        const from = order.status;
        order.status = input.status;
        order.version++;
        order.history.push({
          version: order.version,
          fromStatus: from,
          toStatus: input.status,
          actorId: profile.id,
          actorName: profile.name,
          reason: input.reason,
          occurredAt: now,
        });
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
async function cart(page: Page) {
  await login(page);
  await page.getByRole('link', { name: 'Carrinho', exact: true }).click();
  await page.getByRole('button', { name: 'Selecionar cliente Maria', exact: true }).click();
  await page.getByRole('button', { name: 'Adicionar Uai Sô', exact: true }).click();
  await page.getByLabel('Quantidade do item 1', { exact: true }).fill('2');
  await page.getByLabel('Observações do item 1', { exact: true }).fill('Sem cebola');
}
async function review(page: Page) {
  await page.getByRole('button', { name: 'Revisar carrinho', exact: true }).click();
  await expect(page.getByRole('region', { name: 'Revisão do carrinho' })).toBeVisible();
}
async function detail(page: Page) {
  await login(page);
  await page.getByRole('link', { name: 'Pedidos', exact: true }).click();
  await page.getByRole('link', { name: 'Abrir pedido 1542', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Pedido #1542', exact: true })).toBeVisible();
}

test('pedidos: registra entrega revisada e abre detalhes com histórico', async ({ page }) => {
  const errors: string[] = [];
  page.on('pageerror', (error) => errors.push(error.message));
  const state = await setup(page);
  await cart(page);
  await page.getByRole('combobox', { name: 'Recebimento', exact: true }).selectOption('Delivery');
  await page.getByRole('button', { name: 'Selecionar endereço Rua A, 12', exact: true }).click();
  for (const fee of ['-1', '0.001', '10000', '']) {
    await page.getByLabel('Taxa de entrega (R$)', { exact: true }).fill(fee);
    await expect(
      page.getByRole('button', { name: 'Revisar carrinho', exact: true }),
    ).toBeDisabled();
  }
  await page.getByLabel('Taxa de entrega (R$)', { exact: true }).fill('4.50');
  await review(page);
  await expect(page.getByRole('region', { name: 'Revisão do carrinho' })).toContainText('64,30');
  await page.getByRole('button', { name: 'Registrar pedido', exact: true }).click();
  await expect(page).toHaveURL(/\/equipe\/pedidos\/order-1$/);
  await expect(page.getByRole('heading', { name: 'Pedido #1542' })).toBeVisible();
  await expect(page.getByRole('region', { name: 'Pedido #1542' })).toContainText('64,30');
  await expect(page.getByRole('region', { name: 'Histórico do pedido' })).toContainText('Novo');
  expect(state.created).toHaveLength(1);
  expect(state.created[0].requestId).toMatch(/^[0-9a-f-]{36}$/);
  expect(state.created[0].reviewToken).toBe('A'.repeat(64));
  expect(state.created[0].cart.deliveryFee).toBe(4.5);
  expect(state.created[0].cart.items[0]).not.toHaveProperty('unitPrice');
  expect(errors).toEqual([]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('pedidos: falha incerta conserva tentativa e bloqueia edição até repetir', async ({
  page,
}) => {
  const state = await setup(page);
  state.createStatus = 503;
  await cart(page);
  await review(page);
  await page.getByRole('button', { name: 'Registrar pedido', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('Não foi possível confirmar o resultado');
  await expect(page.getByLabel('Quantidade do item 1', { exact: true })).toBeDisabled();
  await expect(page.getByRole('button', { name: 'Limpar carrinho', exact: true })).toBeDisabled();
  state.createStatus = 200;
  await page.getByRole('button', { name: 'Tentar registro novamente', exact: true }).click();
  await expect(page).toHaveURL(/\/pedidos\/order-1$/);
  expect(state.created).toHaveLength(2);
  expect(state.created[1]).toEqual(state.created[0]);
});

test('pedidos: envio em andamento bloqueia repetição e revisão antiga exige nova revisão', async ({
  page,
}) => {
  const state = await setup(page);
  let release = () => {};
  state.createGate = new Promise<void>((resolve) => {
    release = resolve;
  });
  state.createStatus = 409;
  await cart(page);
  await review(page);
  await page.getByRole('button', { name: 'Registrar pedido', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Registrando…', exact: true })).toBeDisabled();
  await expect(page.getByLabel('Quantidade do item 1', { exact: true })).toBeDisabled();
  release();
  await expect(page.getByRole('alert')).toContainText('Revise os valores');
  await expect(page.getByRole('button', { name: 'Registrar pedido', exact: true })).toHaveCount(0);
  await expect(page.getByLabel('Quantidade do item 1', { exact: true })).toHaveValue('2');
  state.createStatus = 201;
  await review(page);
  await page.getByRole('button', { name: 'Registrar pedido', exact: true }).click();
  await expect(page).toHaveURL(/\/pedidos\/order-1$/);
  expect(state.created).toHaveLength(2);
  expect(state.created[1].requestId).not.toBe(state.created[0].requestId);
});

test('pedidos: conflito de tentativa não libera novo envio', async ({ page }) => {
  const state = await setup(page);
  state.createStatus = 409;
  state.createCode = 'OrderRequestConflict';
  await cart(page);
  await review(page);
  await page.getByRole('button', { name: 'Registrar pedido', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('Consulte os pedidos');
  await expect(page.getByRole('button', { name: 'Registrar pedido', exact: true })).toBeDisabled();
  await expect(page.getByLabel('Quantidade do item 1', { exact: true })).toBeDisabled();
  expect(state.created).toHaveLength(1);
});

test('pedidos: atendente confirma e precisa de administrador para cancelar confirmado', async ({
  page,
}) => {
  const state = await setup(page);
  await detail(page);
  await page.getByRole('button', { name: 'Confirmar pedido', exact: true }).click();
  await page.getByRole('button', { name: 'Confirmar alteração', exact: true }).click();
  await expect(page.getByRole('status')).toContainText('Pedido confirmado');
  await expect(page.getByRole('button', { name: 'Cancelar pedido', exact: true })).toHaveCount(0);
  await expect(page.getByText('solicite ao administrador', { exact: false })).toBeVisible();
  expect(state.changes).toEqual([{ status: 'Confirmed', expectedVersion: 1, reason: null }]);
});

for (const role of ['Administrator', 'Attendant']) {
  test('pedidos: ' + role + ' cancela com motivo e preserva histórico', async ({ page }) => {
    const state = await setup(page, role);
    if (role === 'Administrator') {
      state.orders[0].status = 'Confirmed';
    }
    await detail(page);
    await page.getByRole('button', { name: 'Cancelar pedido', exact: true }).click();
    await expect(
      page.getByRole('button', { name: 'Confirmar alteração', exact: true }),
    ).toBeDisabled();
    await page.getByLabel('Motivo do cancelamento', { exact: true }).fill('Cliente desistiu');
    await page.getByRole('button', { name: 'Confirmar alteração', exact: true }).click();
    await expect(page.getByRole('status')).toContainText('Pedido cancelado');
    await expect(page.getByRole('region', { name: 'Histórico do pedido' })).toContainText(
      'Cliente desistiu',
    );
    await expect(page.getByRole('button', { name: 'Confirmar pedido', exact: true })).toHaveCount(
      0,
    );
    await expect(page.getByRole('button', { name: 'Cancelar pedido', exact: true })).toHaveCount(0);
    expect(state.changes[0].reason).toBe('Cliente desistiu');
  });
}

for (const status of ['InPreparation', 'Ready'] as const) {
  for (const role of ['Administrator', 'Attendant']) {
    test('pedidos: cancelamento durante produção ' + status + ' por ' + role, async ({ page }) => {
      const state = await setup(page, role);
      state.orders[0].status = status;
      state.orders[0].version = 3;
      await detail(page);
      if (role === 'Attendant') {
        await expect(
          page.getByRole('button', { name: 'Cancelar pedido', exact: true }),
        ).toHaveCount(0);
        await expect(page.getByText('solicite ao administrador', { exact: false })).toBeVisible();
        expect(state.changes).toHaveLength(0);
        return;
      }
      await page.getByRole('button', { name: 'Cancelar pedido', exact: true }).click();
      await page.getByLabel('Motivo do cancelamento', { exact: true }).fill('Cliente desistiu');
      await page.getByRole('button', { name: 'Confirmar alteração', exact: true }).click();
      await expect(page.getByRole('status')).toContainText('Pedido cancelado');
      expect(state.changes).toEqual([
        { status: 'Cancelled', expectedVersion: 3, reason: 'Cliente desistiu' },
      ]);
    });
  }
}

test('pedidos: conflito de status exige atualização antes de nova ação', async ({ page }) => {
  const state = await setup(page);
  state.changeStatus = 409;
  await detail(page);
  await page.getByRole('button', { name: 'Confirmar pedido', exact: true }).click();
  await page.getByRole('button', { name: 'Confirmar alteração', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('outro atendimento');
  await expect(page.getByRole('button', { name: 'Confirmar pedido', exact: true })).toBeDisabled();
  state.orders[0].status = 'Cancelled';
  await page.getByRole('button', { name: 'Atualizar pedido', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Confirmar pedido', exact: true })).toHaveCount(0);
  await expect(page.getByRole('region', { name: 'Pedido #1542' })).toContainText('Cancelado');
});

test('pedidos: lista pagina, filtra e recupera falha de consulta', async ({ page }) => {
  const state = await setup(page);
  for (let index = 0; index < 20; index++) {
    state.orders.push({ ...structuredClone(original), id: 'extra-' + index, number: 1600 + index });
  }
  state.listStatus = 503;
  await login(page);
  await page.getByRole('link', { name: 'Pedidos', exact: true }).click();
  await expect(page.getByRole('alert')).toBeVisible();
  state.listStatus = 200;
  await page.getByRole('button', { name: 'Tentar novamente', exact: true }).click();
  await page.getByRole('button', { name: 'Próxima', exact: true }).click();
  await expect(page.getByRole('link', { name: 'Abrir pedido 1619' })).toBeVisible();
  await page.getByLabel('Buscar por número, cliente ou telefone', { exact: true }).fill('1542');
  await page.getByRole('combobox', { name: 'Status', exact: true }).selectOption('New');
  await page.getByRole('button', { name: 'Buscar pedidos', exact: true }).click();
  await expect(page.getByRole('link', { name: 'Abrir pedido 1542' })).toBeVisible();
  expect(state.queries.at(-1)).toContain('status=New');
  await page.getByRole('combobox', { name: 'Status', exact: true }).selectOption('Cancelled');
  await page.getByRole('button', { name: 'Buscar pedidos', exact: true }).click();
  await expect(page.getByText('Nenhum pedido nesta página.', { exact: false })).toBeVisible();
});

for (const role of ['Kitchen', 'Dispatch']) {
  test('pedidos: ' + role + ' não acessa lista ou detalhes', async ({ page }) => {
    const state = await setup(page, role);
    await login(page);
    await expect(page.getByRole('link', { name: 'Pedidos', exact: true })).toHaveCount(0);
    for (const path of ['/equipe/pedidos', '/equipe/pedidos/order-1']) {
      await page.evaluate((path) => {
        history.pushState(null, '', path);
        dispatchEvent(new PopStateEvent('popstate'));
      }, path);
      await expect(page).toHaveURL(/\/equipe$/);
    }
    expect(state.orderRequests).toBe(0);
  });
}
