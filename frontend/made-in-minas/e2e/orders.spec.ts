import { expect, Page, test } from '@playwright/test';
import type { CartQuote, CartQuoteInput } from '../src/app/core/services/cart-api.service';
import type {
  CreateOrderInput,
  Order,
  OrderPaymentStatus,
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
  stockStatus: 'Pending',
  stockComponents: [],
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

type SoundWindow = Window & {
  orderSound: { context: AudioContext; tones: number };
};

async function observeSound(page: Page, rejectResume = false) {
  await page.addInitScript((rejectResume) => {
    const NativeAudioContext = window.AudioContext;
    window.AudioContext = class extends NativeAudioContext {
      constructor(options?: AudioContextOptions) {
        super(options);
        (window as SoundWindow).orderSound = { context: this, tones: 0 };
      }

      override createOscillator(): OscillatorNode {
        const tone = super.createOscillator();
        const start = tone.start.bind(tone);
        tone.start = (when?: number) => {
          (window as SoundWindow).orderSound.tones++;
          start(when);
        };
        return tone;
      }

      override resume(): Promise<void> {
        return rejectResume ? Promise.reject(new Error('Audio blocked for test')) : super.resume();
      }
    };
  }, rejectResume);
}

async function soundState(page: Page) {
  return page.evaluate(() => {
    const sound = (window as SoundWindow).orderSound;
    return { state: sound.context.state, tones: sound.tones };
  });
}

async function setup(page: Page, role = 'Attendant') {
  const state = {
    orders: [structuredClone(original)],
    created: [] as CreateOrderInput[],
    changes: [] as OrderStatusInput[],
    queries: [] as string[],
    paymentStatuses: {} as Record<string, OrderPaymentStatus>,
    createStatus: 201,
    createCode: 'OrderReviewChanged',
    listStatus: 200,
    changeStatus: 200,
    changeCode: 'OrderVersionConflict',
    createGate: null as Promise<void> | null,
    listGate: null as Promise<void> | null,
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
      if (state.listGate) {
        await state.listGate;
      }
      if (state.listStatus !== 200) {
        return json({}, state.listStatus);
      }
      const search = (url.searchParams.get('search') ?? '').toLowerCase();
      const status = url.searchParams.get('status');
      const origin = url.searchParams.get('origin');
      const paymentStatus = url.searchParams.get('paymentStatus');
      const summary = (order: Order) => ({
        ...order,
        customerName: order.customer.name,
        paymentStatus:
          state.paymentStatuses[order.id] ??
          (order.status === 'Cancelled' ? 'NotDue' : 'NotRegistered'),
      });
      const filtered = state.orders.filter(
        (order) =>
          (!status || order.status === status) &&
          (!origin || order.origin === origin) &&
          (!paymentStatus ||
            (paymentStatus === 'Unpaid'
              ? !['Cancelled', 'Finalized'].includes(order.status) &&
                summary(order).paymentStatus !== 'Received'
              : summary(order).paymentStatus === paymentStatus)) &&
          (order.customer.name.toLowerCase().includes(search) ||
            String(order.number).includes(search)),
      );
      return json(pageOf(filtered.map(summary)));
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
          return json({ code: state.changeCode }, state.changeStatus);
        }
        const from = order.status;
        order.status = input.status;
        order.stockStatus =
          input.status === 'Confirmed'
            ? 'Consumed'
            : order.stockStatus === 'Consumed'
              ? from === 'Confirmed'
                ? 'Returned'
                : 'Retained'
              : order.stockStatus === 'Legacy'
                ? 'Legacy'
                : 'NotRequired';
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
  return Object.assign(state, { profile });
}

async function login(page: Page) {
  await page.goto('/entrar?returnUrl=%2Fequipe');
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

test('estoque do pedido: informa baixa e mostra composição histórica', async ({ page }) => {
  const state = await setup(page);
  await detail(page);
  await expect(page.getByRole('region', { name: 'Estoque do pedido' })).toContainText(
    'Baixa pendente',
  );
  await page.getByRole('button', { name: 'Confirmar pedido', exact: true }).click();
  await expect(page.getByRole('region', { name: 'Confirmar alteração do pedido' })).toContainText(
    'saldo suficiente',
  );
  state.orders[0].stockComponents = [
    {
      productId: 'product',
      productName: 'Uai Sô',
      ingredientId: 'meat',
      ingredientName: 'Carne histórica',
      unit: 'kg',
      productQuantity: 2,
      recipeYield: 3,
      recipeQuantity: 0.5,
      consumedQuantity: 0.334,
    },
  ];
  await page.getByRole('button', { name: 'Confirmar alteração', exact: true }).click();
  const stock = page.getByRole('region', { name: 'Estoque do pedido' });
  await expect(stock).toContainText('Ingredientes baixados');
  await stock.getByText('Composição registrada na confirmação', { exact: true }).click();
  await expect(stock).toContainText('Carne histórica — 0,334 kg');
  expect(state.changes[0]).not.toHaveProperty('stockComponents');
});

for (const status of ['Confirmed', 'InPreparation'] as const) {
  test('estoque do pedido: revisão explica cancelamento em ' + status, async ({ page }) => {
    const state = await setup(page, 'Administrator');
    state.orders[0].status = status;
    state.orders[0].stockStatus = 'Consumed';
    await detail(page);
    await page.getByRole('button', { name: 'Cancelar pedido', exact: true }).click();
    const confirmation = page.getByRole('region', { name: 'Confirmar alteração do pedido' });
    await expect(confirmation).toContainText(
      status === 'Confirmed' ? 'serão devolvidos' : 'não haverá devolução automática',
    );
    await page.getByLabel('Motivo do cancelamento', { exact: true }).fill('Desistência');
    await page.getByRole('button', { name: 'Confirmar alteração', exact: true }).click();
    await expect(page.getByRole('region', { name: 'Estoque do pedido' })).toContainText(
      status === 'Confirmed' ? 'devolvidos ao estoque' : 'Consumo mantido',
    );
  });
}

test('estoque do pedido: pedido antigo não promete devolução', async ({ page }) => {
  const state = await setup(page, 'Administrator');
  state.orders[0].status = 'Confirmed';
  state.orders[0].stockStatus = 'Legacy';
  await detail(page);
  await expect(page.getByRole('region', { name: 'Estoque do pedido' })).toContainText(
    'sem baixa retroativa',
  );
  await page.getByRole('button', { name: 'Cancelar pedido', exact: true }).click();
  await expect(page.getByRole('region', { name: 'Confirmar alteração do pedido' })).toContainText(
    'não alterará o estoque',
  );
});

for (const code of ['OrderInsufficientStock', 'OrderRecipeRequired']) {
  test(
    'estoque do pedido: conflito ' + code + ' mantém novo e exige atualização',
    async ({ page }) => {
      const state = await setup(page);
      state.changeStatus = 409;
      state.changeCode = code;
      await detail(page);
      await page.getByRole('button', { name: 'Confirmar pedido', exact: true }).click();
      await page.getByRole('button', { name: 'Confirmar alteração', exact: true }).click();
      await expect(page.getByRole('alert')).toContainText(
        code === 'OrderInsufficientStock' ? 'Saldo insuficiente' : 'ficha técnica',
      );
      await expect(
        page.getByRole('button', { name: 'Confirmar pedido', exact: true }),
      ).toBeDisabled();
      await expect(page.getByRole('region', { name: 'Estoque do pedido' })).toContainText(
        'Baixa pendente',
      );
      expect(state.changes).toHaveLength(1);
    },
  );
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
  await expect(
    page.getByRole('region', { name: 'Histórico do pedido' }).locator('ol'),
  ).toBeHidden();
  await page.getByText('Histórico do pedido', { exact: true }).click();
  await expect(page.getByRole('region', { name: 'Histórico do pedido' })).toContainText('Novo');
  expect(state.created).toHaveLength(1);
  expect(state.created[0].requestId).toMatch(/^[0-9a-f-]{36}$/);
  expect(state.created[0].reviewToken).toBe('A'.repeat(64));
  expect(state.created[0].cart.deliveryFee).toBe(4.5);
  expect(state.created[0].cart.items[0]).not.toHaveProperty('unitPrice');
  expect(errors).toEqual([]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('pedidos: tentativa incerta sobrevive ao reload e ao login sem duplicar o envio', async ({
  page,
}) => {
  const state = await setup(page);
  state.createStatus = 503;
  await cart(page);
  await review(page);
  await page.getByRole('button', { name: 'Registrar pedido', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('Não foi possível confirmar o resultado');
  await page.reload();
  await expect(page).toHaveURL(/entrar\?returnUrl=/);
  await page.getByLabel('Login', { exact: true }).fill('staff');
  await page.getByLabel('Senha', { exact: true }).fill('Senha apenas para testes');
  await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await expect(page).toHaveURL(/\/equipe\/carrinho$/);
  await expect(page.getByLabel('Quantidade do item 1', { exact: true })).toBeDisabled();
  expect(state.created).toHaveLength(1);
  state.createStatus = 401;
  await page.getByRole('button', { name: 'Tentar registro novamente', exact: true }).click();
  await expect(page).toHaveURL(/entrar\?returnUrl=/);
  await page.getByLabel('Login', { exact: true }).fill('staff');
  await page.getByLabel('Senha', { exact: true }).fill('Senha apenas para testes');
  await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await expect(page).toHaveURL(/\/equipe\/carrinho$/);
  await expect(page.getByLabel('Quantidade do item 1', { exact: true })).toBeDisabled();
  state.createStatus = 201;
  await page.getByRole('button', { name: 'Tentar registro novamente', exact: true }).click();
  await expect(page).toHaveURL(/\/pedidos\/order-1$/);
  expect(state.created).toHaveLength(3);
  expect(state.created[1]).toEqual(state.created[0]);
  expect(state.created[2]).toEqual(state.created[0]);
  expect(
    await page.evaluate(() => sessionStorage.getItem('made-in-minas.staff-cart.v1.staff')),
  ).toBeNull();
});

test('pedidos: outro funcionário não recupera o rascunho da conta anterior', async ({ page }) => {
  const state = await setup(page);
  await cart(page);
  state.profile.id = 'another-worker';
  await login(page);
  await page.getByRole('link', { name: 'Carrinho', exact: true }).click();
  await expect(page.getByText('O carrinho está vazio.', { exact: false })).toBeVisible();
  await expect(page.getByText('Cliente selecionado:', { exact: false })).toHaveCount(0);
});

test('pedidos: fila detecta novos pedidos e pausa consultas com a aba oculta', async ({ page }) => {
  await page.clock.install();
  const state = await setup(page);
  await login(page);
  await page.getByRole('link', { name: 'Pedidos', exact: true }).click();
  await expect(
    page.getByRole('button', { name: 'Aguardando confirmação (1)', exact: true }),
  ).toBeVisible();
  state.orders.unshift({ ...structuredClone(original), id: 'new-order', number: 1543 });
  await page.clock.fastForward(10000);
  await expect(
    page.getByRole('button', { name: 'Aguardando confirmação (2)', exact: true }),
  ).toBeVisible();
  await expect(
    page.getByText('Chegou pedido novo. Confira a fila aguardando confirmação.', { exact: true }),
  ).toBeVisible();
  const queries = state.queries.length;
  await page.evaluate(() =>
    Object.defineProperty(document, 'hidden', { configurable: true, get: () => true }),
  );
  await page.clock.fastForward(30000);
  expect(state.queries.length).toBe(queries);
  await page.evaluate(() => {
    Object.defineProperty(document, 'hidden', { configurable: true, get: () => false });
    document.dispatchEvent(new Event('visibilitychange'));
  });
  await expect.poll(() => state.queries.length).toBeGreaterThan(queries);
});

test('pedidos: aviso sonoro toca para pedido público novo sem repetir e respeita desativação', async ({
  page,
}) => {
  await page.clock.install();
  await observeSound(page);
  const state = await setup(page);
  await login(page);
  await page.getByRole('link', { name: 'Pedidos', exact: true }).click();
  await expect(page.getByRole('link', { name: 'Abrir pedido 1542' })).toBeVisible();
  await page.getByRole('button', { name: 'Ativar aviso sonoro', exact: true }).click();
  await expect(
    page.getByRole('button', { name: 'Desativar aviso sonoro', exact: true }),
  ).toHaveAttribute('aria-pressed', 'true');
  expect(await soundState(page)).toEqual({ state: 'running', tones: 1 });
  await page.getByRole('button', { name: 'Prontos', exact: true }).click();
  await expect(page.getByText('Nenhum pedido nesta página.', { exact: false })).toBeVisible();
  state.orders.unshift({
    ...structuredClone(original),
    id: 'public-order',
    number: 1543,
    origin: 'DirectLink',
  });
  await page.clock.fastForward(10000);
  await expect(
    page.getByRole('button', { name: 'Aguardando confirmação (2)', exact: true }),
  ).toBeVisible();
  expect(await soundState(page)).toEqual({ state: 'running', tones: 2 });
  const queries = state.queries.length;
  await page.clock.fastForward(10000);
  await expect.poll(() => state.queries.length).toBeGreaterThan(queries);
  expect((await soundState(page)).tones).toBe(2);
  await page.getByRole('button', { name: 'Desativar aviso sonoro', exact: true }).click();
  state.orders.unshift({ ...structuredClone(original), id: 'muted-order', number: 1544 });
  await page.clock.fastForward(10000);
  await expect(
    page.getByRole('button', { name: 'Aguardando confirmação (3)', exact: true }),
  ).toBeVisible();
  expect((await soundState(page)).tones).toBe(2);
  await expect(
    page.getByText('Chegou pedido novo. Confira a fila aguardando confirmação.'),
  ).toBeVisible();
});

test('pedidos: aviso sonoro informa interrupção do navegador e permite reativar', async ({
  page,
}) => {
  await observeSound(page);
  await setup(page);
  await login(page);
  await page.getByRole('link', { name: 'Pedidos', exact: true }).click();
  await page.getByRole('button', { name: 'Ativar aviso sonoro', exact: true }).click();
  await expect(
    page.getByRole('button', { name: 'Desativar aviso sonoro', exact: true }),
  ).toBeVisible();
  await page.evaluate(() => (window as SoundWindow).orderSound.context.suspend());
  await expect(
    page.getByRole('button', { name: 'Ativar aviso sonoro', exact: true }),
  ).toBeVisible();
  await expect(
    page.getByText('O som foi interrompido pelo navegador.', { exact: false }),
  ).toBeVisible();
  await page.getByRole('button', { name: 'Ativar aviso sonoro', exact: true }).click();
  await expect(
    page.getByRole('button', { name: 'Desativar aviso sonoro', exact: true }),
  ).toBeVisible();
  expect((await soundState(page)).state).toBe('running');
  await expect(
    page.getByText('O som foi interrompido pelo navegador.', { exact: false }),
  ).toHaveCount(0);
});

test('pedidos: aviso sonoro bloqueado mantém avisos visuais e não aparece como ativo', async ({
  page,
}) => {
  await page.clock.install();
  await observeSound(page, true);
  const state = await setup(page);
  await login(page);
  await page.getByRole('link', { name: 'Pedidos', exact: true }).click();
  await expect(page.getByRole('link', { name: 'Abrir pedido 1542' })).toBeVisible();
  await page.getByRole('button', { name: 'Ativar aviso sonoro', exact: true }).click();
  await expect(
    page.getByText('O navegador não permitiu ativar o som.', { exact: false }),
  ).toBeVisible();
  await expect(
    page.getByRole('button', { name: 'Ativar aviso sonoro', exact: true }),
  ).toHaveAttribute('aria-pressed', 'false');
  state.orders.unshift({ ...structuredClone(original), id: 'silent-order', number: 1543 });
  await page.clock.fastForward(10000);
  await expect(
    page.getByRole('button', { name: 'Aguardando confirmação (2)', exact: true }),
  ).toBeVisible();
  await expect(
    page.getByText('Chegou pedido novo. Confira a fila aguardando confirmação.'),
  ).toBeVisible();
  await expect(
    page.getByText('O navegador não permitiu ativar o som.', { exact: false }),
  ).toBeVisible();
  expect((await soundState(page)).tones).toBe(0);
});

test('pedidos: aviso sonoro pausa na aba oculta e é encerrado ao sair da tela', async ({
  page,
}) => {
  await page.clock.install();
  await observeSound(page);
  const state = await setup(page);
  await login(page);
  await page.getByRole('link', { name: 'Pedidos', exact: true }).click();
  await expect(page.getByRole('link', { name: 'Abrir pedido 1542' })).toBeVisible();
  await page.getByRole('button', { name: 'Ativar aviso sonoro', exact: true }).click();
  await expect(
    page.getByRole('button', { name: 'Desativar aviso sonoro', exact: true }),
  ).toBeVisible();
  const queries = state.queries.length;
  await page.evaluate(() =>
    Object.defineProperty(document, 'hidden', { configurable: true, get: () => true }),
  );
  state.orders.unshift({ ...structuredClone(original), id: 'hidden-order', number: 1543 });
  await page.clock.fastForward(30000);
  expect(state.queries.length).toBe(queries);
  expect((await soundState(page)).tones).toBe(1);
  await page.evaluate(() => {
    Object.defineProperty(document, 'hidden', { configurable: true, get: () => false });
    document.dispatchEvent(new Event('visibilitychange'));
  });
  await expect(
    page.getByRole('button', { name: 'Aguardando confirmação (2)', exact: true }),
  ).toBeVisible();
  expect((await soundState(page)).tones).toBe(2);
  await page.getByRole('link', { name: 'Minha conta', exact: true }).click();
  await expect.poll(async () => (await soundState(page)).state).toBe('closed');
  const afterNavigation = state.queries.length;
  await page.clock.fastForward(30000);
  expect(state.queries.length).toBe(afterNavigation);
  await page.getByRole('link', { name: 'Pedidos', exact: true }).click();
  await expect(
    page.getByRole('button', { name: 'Ativar aviso sonoro', exact: true }),
  ).toBeVisible();
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
  state.createStatus = 429;
  await page.getByRole('button', { name: 'Tentar registro novamente', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('Não foi possível confirmar o resultado');
  await expect(page.getByLabel('Quantidade do item 1', { exact: true })).toBeDisabled();
  await expect(
    page.getByRole('button', { name: 'Tentar registro novamente', exact: true }),
  ).toBeEnabled();
  state.createStatus = 200;
  await page.getByRole('button', { name: 'Tentar registro novamente', exact: true }).click();
  await expect(page).toHaveURL(/\/pedidos\/order-1$/);
  expect(state.created).toHaveLength(3);
  expect(state.created[1]).toEqual(state.created[0]);
  expect(state.created[2]).toEqual(state.created[0]);
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

test('pedidos: origem e pagamento combinam com busca e paginação sem aplicar rascunhos', async ({
  page,
}) => {
  const state = await setup(page);
  for (let index = 0; index < 22; index++) {
    const id = 'site-' + index;
    state.orders.push({
      ...structuredClone(original),
      id,
      number: 1700 + index,
      origin: 'DirectLink',
    });
    state.paymentStatuses[id] = 'Received';
  }
  await login(page);
  await page.getByRole('link', { name: 'Pedidos', exact: true }).click();
  await expect(page.getByText('23 pedido(s) encontrado(s).', { exact: true })).toBeVisible();
  await page.getByRole('combobox', { name: 'Origem', exact: true }).selectOption('DirectLink');
  await page.getByRole('combobox', { name: 'Pagamento', exact: true }).selectOption('Received');
  await page.getByRole('button', { name: 'Buscar pedidos', exact: true }).click();
  await expect(page.getByText('22 pedido(s) encontrado(s).', { exact: true })).toBeVisible();
  const row = page
    .getByRole('row')
    .filter({ has: page.getByRole('link', { name: 'Abrir pedido 1700', exact: true }) });
  await expect(row.locator('td[data-label="Origem"]')).toHaveText('Site');
  await expect(row.locator('td[data-label="Pagamento"]')).toHaveText('Recebido');
  await page.getByRole('combobox', { name: 'Origem', exact: true }).selectOption('Manual');
  await expect(page.getByText('Filtros alterados.', { exact: false })).toBeVisible();
  await page.getByRole('button', { name: 'Próxima', exact: true }).click();
  await expect(page.getByText('Página 2', { exact: true })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Abrir pedido 1721', exact: true })).toBeVisible();
  const query = new URLSearchParams(state.queries.findLast((value) => value.includes('page=2')));
  expect(query.get('origin')).toBe('DirectLink');
  expect(query.get('paymentStatus')).toBe('Received');
  await page.getByRole('combobox', { name: 'Origem', exact: true }).selectOption('DirectLink');
  await page.getByLabel('Buscar por número, cliente ou telefone', { exact: true }).fill('1721');
  await page.getByRole('button', { name: 'Buscar pedidos', exact: true }).click();
  await expect(page.getByText('Página 1', { exact: true })).toBeVisible();
  await expect(page.getByText('1 pedido(s) encontrado(s).', { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Próxima', exact: true })).toBeDisabled();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('pedidos: atalho a receber limpa filtros e exclui recebidos e encerrados', async ({
  page,
}) => {
  const state = await setup(page);
  state.orders.push(
    { ...structuredClone(original), id: 'pending', number: 1543, origin: 'DirectLink' },
    { ...structuredClone(original), id: 'paid', number: 1544 },
    { ...structuredClone(original), id: 'cancelled', number: 1545, status: 'Cancelled' },
    { ...structuredClone(original), id: 'refunded', number: 1546, status: 'Confirmed' },
    { ...structuredClone(original), id: 'finalized', number: 1547, status: 'Finalized' },
  );
  Object.assign(state.paymentStatuses, {
    pending: 'Pending',
    paid: 'Received',
    refunded: 'Refunded',
    finalized: 'Refunded',
  });
  await login(page);
  await page.getByRole('link', { name: 'Pedidos', exact: true }).click();
  await page.getByRole('combobox', { name: 'Origem', exact: true }).selectOption('Manual');
  await page
    .getByLabel('Buscar por número, cliente ou telefone', { exact: true })
    .fill('sem resultado');
  await page.getByRole('button', { name: 'A receber', exact: true }).click();
  await expect(page.getByText('3 pedido(s) encontrado(s).', { exact: true })).toBeVisible();
  for (const number of [1542, 1543, 1546]) {
    await expect(
      page.getByRole('link', { name: 'Abrir pedido ' + number, exact: true }),
    ).toBeVisible();
  }
  for (const number of [1544, 1545, 1547]) {
    await expect(
      page.getByRole('link', { name: 'Abrir pedido ' + number, exact: true }),
    ).toHaveCount(0);
  }
  await expect(page.getByRole('button', { name: 'A receber', exact: true })).toHaveAttribute(
    'aria-pressed',
    'true',
  );
  await expect(page.getByRole('combobox', { name: 'Origem', exact: true })).toHaveValue('');
  await expect(
    page.getByRole('button', { name: 'Aguardando confirmação (3)', exact: true }),
  ).toBeVisible();
  await page.getByRole('button', { name: 'Todos os pedidos', exact: true }).click();
  await expect(page.getByText('6 pedido(s) encontrado(s).', { exact: true })).toBeVisible();
  await expect(page.getByRole('combobox', { name: 'Pagamento', exact: true })).toHaveValue('');
  await expect(
    page.locator('td[data-label="Pagamento"]').filter({ hasText: 'Sem cobrança' }),
  ).toBeVisible();
});

test('pedidos: atualização usa filtros aplicados e pagamento recebido deixa a fila a receber', async ({
  page,
}) => {
  await page.clock.install();
  const state = await setup(page);
  state.orders[0].origin = 'DirectLink';
  state.paymentStatuses[original.id] = 'Pending';
  await login(page);
  await page.getByRole('link', { name: 'Pedidos', exact: true }).click();
  await page.getByRole('button', { name: 'A receber', exact: true }).click();
  await expect(page.locator('td[data-label="Pagamento"]')).toHaveText('Aguardando recebimento');
  await page.getByRole('combobox', { name: 'Origem', exact: true }).selectOption('Manual');
  await page.getByRole('combobox', { name: 'Pagamento', exact: true }).selectOption('Received');
  const before = state.queries.length;
  let release!: () => void;
  state.listGate = new Promise<void>((resolve) => (release = resolve));
  try {
    await page.clock.fastForward(10000);
    await expect.poll(() => state.queries.length).toBe(before + 2);
    await expect(page.getByRole('button', { name: 'Buscar pedidos', exact: true })).toBeDisabled();
  } finally {
    release();
  }
  await expect(page.getByRole('button', { name: 'Buscar pedidos', exact: true })).toBeEnabled();
  const applied = state.queries.slice(before).map((value) => new URLSearchParams(value));
  expect(applied.find((value) => value.get('paymentStatus') === 'Unpaid')?.has('origin')).toBe(
    false,
  );
  expect(applied.some((value) => value.get('paymentStatus') === 'Received')).toBe(false);
  state.paymentStatuses[original.id] = 'Received';
  await page.clock.fastForward(10000);
  await expect(page.getByText('0 pedido(s) encontrado(s).', { exact: true })).toBeVisible();
  await expect(
    page.getByRole('button', { name: 'Aguardando confirmação (1)', exact: true }),
  ).toBeVisible();
  await expect(page.getByText('Filtros alterados.', { exact: false })).toBeVisible();
});

test('pedidos: falha automática conserva lista e repetição não aplica filtros em edição', async ({
  page,
}) => {
  await page.clock.install();
  const state = await setup(page);
  state.orders[0].origin = 'DirectLink';
  await login(page);
  await page.getByRole('link', { name: 'Pedidos', exact: true }).click();
  await page.getByRole('combobox', { name: 'Origem', exact: true }).selectOption('DirectLink');
  await page.getByRole('button', { name: 'Buscar pedidos', exact: true }).click();
  await expect(page.locator('td[data-label="Origem"]')).toHaveText('Site');
  await page.getByRole('combobox', { name: 'Origem', exact: true }).selectOption('Manual');
  state.listStatus = 503;
  await page.clock.fastForward(10000);
  await expect(page.getByRole('alert')).toBeVisible();
  await expect(page.getByRole('link', { name: 'Abrir pedido 1542' })).toBeVisible();
  state.listStatus = 200;
  const before = state.queries.length;
  await page.getByRole('button', { name: 'Tentar novamente', exact: true }).click();
  await expect(page.getByRole('alert')).toHaveCount(0);
  await expect(page.getByRole('link', { name: 'Abrir pedido 1542' })).toBeVisible();
  expect(
    state.queries
      .slice(before)
      .some((value) => new URLSearchParams(value).get('origin') === 'DirectLink'),
  ).toBe(true);
  await page.getByRole('button', { name: 'Buscar pedidos', exact: true }).click();
  await expect(page.getByText('Nenhum pedido nesta página.', { exact: false })).toBeVisible();
});

test('pedidos: origem pública e autoria do visitante aparecem sem funcionário fictício', async ({
  page,
}) => {
  const state = await setup(page);
  state.orders[0].origin = 'DirectLink';
  state.orders[0].history[0].actorId = null;
  state.orders[0].history[0].actorName = 'Cliente pelo site';
  await detail(page);
  await expect(page.getByText('LINK DIRETO · PEDIDO PELO SITE', { exact: true })).toBeVisible();
  await expect(page.getByText('sem verificação de titularidade', { exact: false })).toBeVisible();
  await expect(page.getByRole('region', { name: 'Histórico do pedido' })).toContainText(
    'Cliente pelo site',
  );
  await expect(page.getByRole('button', { name: 'Confirmar pedido', exact: true })).toBeVisible();
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
