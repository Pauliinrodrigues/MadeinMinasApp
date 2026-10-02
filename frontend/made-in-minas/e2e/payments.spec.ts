import { expect, Page, test } from '@playwright/test';
import type {
  Payment,
  PaymentCommand,
  PaymentMethod,
  PaymentPage,
} from '../src/app/core/services/payment-api.service';

const now = '2026-10-01T12:00:00Z';
const initial: PaymentPage = {
  orderId: 'order-1',
  orderNumber: 1542,
  orderStatus: 'New',
  orderVersion: 1,
  orderTotal: 59.8,
  receivedAmount: 0,
  balance: 59.8,
  activePayment: null,
  items: [],
  page: 1,
  pageSize: 20,
  totalCount: 0,
};

function pending(method: PaymentMethod = 'Pix'): Payment {
  return {
    id: 'payment-1',
    orderId: 'order-1',
    method,
    status: 'Pending',
    version: 1,
    amount: 59.8,
    cashTendered: null,
    changeAmount: null,
    createdAt: now,
    updatedAt: now,
    history: [
      {
        version: 1,
        fromStatus: null,
        toStatus: 'Pending',
        actorId: 'staff',
        actorName: 'Equipe Teste',
        reason: null,
        occurredAt: now,
      },
    ],
  };
}

async function setup(page: Page, role = 'Attendant') {
  const state = {
    data: structuredClone(initial),
    commands: [] as PaymentCommand[],
    queries: [] as number[],
    writesStatus: 200,
    writesCode: 'PaymentVersionConflict',
    listStatus: 200,
    uncertainNext: false,
    gate: null as Promise<void> | null,
    requests: 0,
  };
  const replies = new Map<string, Payment>();
  const profile = {
    id: 'staff',
    name: 'Equipe Teste',
    username: 'staff',
    role,
    permissions:
      role === 'Administrator'
        ? ['orders.manage', 'payments.manage', 'payments.refund']
        : role === 'Attendant'
          ? ['orders.manage', 'payments.manage']
          : [],
  };
  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const json = (body: unknown, status = 200) => route.fulfill({ status, json: body });
    if (url.pathname === '/api/auth/login') {
      return json({
        accessToken: 'payments-token',
        expiresAt: new Date(Date.now() + 900000).toISOString(),
        tokenType: 'Bearer',
        user: profile,
      });
    }
    expect(request.headers()['authorization']).toBe('Bearer payments-token');
    if (url.pathname === '/api/auth/me') {
      return json(profile);
    }
    if (url.pathname === '/api/auth/logout') {
      return json({});
    }
    if (url.pathname === '/api/orders') {
      return json({
        items: [
          {
            id: 'order-1',
            number: 1542,
            customerName: 'Maria',
            fulfillment: 'Pickup',
            status: 'New',
            total: 59.8,
            createdAt: now,
          },
        ],
        page: 1,
        pageSize: 20,
        totalCount: 1,
      });
    }
    if (url.pathname === '/api/orders/order-1') {
      return json({
        id: 'order-1',
        number: 1542,
        origin: 'Manual',
        status: state.data.orderStatus,
        version: 1,
        stockStatus: 'Pending',
        stockComponents: [],
        customer: { id: 'customer', name: 'Maria', phone: '+5531999991234' },
        fulfillment: 'Pickup',
        address: null,
        items: [],
        notes: null,
        subtotal: 59.8,
        deliveryFee: 0,
        total: 59.8,
        createdAt: now,
        updatedAt: now,
        history: [],
      });
    }
    if (!url.pathname.startsWith('/api/orders/order-1/payments')) {
      return json({}, 404);
    }
    state.requests++;
    if (request.method() === 'GET') {
      const current = Number(url.searchParams.get('page') ?? 1);
      state.queries.push(current);
      if (state.listStatus !== 200) {
        return json({}, state.listStatus);
      }
      const active =
        state.data.items.find((payment) => ['Pending', 'Received'].includes(payment.status)) ??
        null;
      return json({
        ...state.data,
        activePayment: active,
        receivedAmount: active?.status === 'Received' ? active.amount : 0,
        balance:
          state.data.orderStatus === 'Cancelled' || active?.status === 'Received'
            ? 0
            : state.data.orderTotal,
        page: current,
        totalCount: state.data.items.length,
        items: state.data.items.slice((current - 1) * 20, current * 20),
      });
    }
    const parts = url.pathname.split('/');
    const command = (
      request.method() === 'POST'
        ? { action: 'create', input: request.postDataJSON() }
        : { action: parts[6], id: parts[5], input: request.postDataJSON() }
    ) as PaymentCommand;
    state.commands.push(command);
    if (state.gate) {
      await state.gate;
    }
    if (state.writesStatus !== 200) {
      return json({ code: state.writesCode }, state.writesStatus);
    }
    const key = JSON.stringify(command);
    const previous = replies.get(key);
    if (previous) {
      return json(previous);
    }
    let payment: Payment;
    if (command.action === 'create') {
      payment = pending(command.input.method);
      payment.id = 'payment-' + (state.data.items.length + 1);
      state.data.items.unshift(payment);
    } else {
      payment = state.data.items.find((payment) => payment.id === command.id)!;
      const fromStatus = payment.status;
      payment.status =
        command.action === 'receive'
          ? 'Received'
          : command.action === 'cancel'
            ? 'Cancelled'
            : 'Refunded';
      payment.version++;
      if (command.action === 'receive') {
        payment.cashTendered = command.input.cashTendered;
        payment.changeAmount = payment.cashTendered === null ? null : 40.2;
      }
      payment.history.push({
        version: payment.version,
        fromStatus,
        toStatus: payment.status,
        actorId: profile.id,
        actorName: profile.name,
        reason: command.action === 'receive' ? null : command.input.reason,
        occurredAt: now,
      });
    }
    replies.set(key, structuredClone(payment));
    if (state.uncertainNext) {
      state.uncertainNext = false;
      return json({}, 503);
    }
    return json(payment, command.action === 'create' ? 201 : 200);
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
async function openPayments(page: Page) {
  await login(page);
  await page.getByRole('link', { name: 'Pedidos', exact: true }).click();
  await page.getByRole('link', { name: 'Abrir pedido 1542', exact: true }).click();
  await page.getByRole('link', { name: 'Pagamentos', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Pagamentos', exact: true })).toBeVisible();
}
async function create(page: Page, method = 'Pix') {
  await page
    .getByRole('combobox', { name: 'Forma de pagamento', exact: true })
    .selectOption(method);
  await page.getByRole('button', { name: 'Definir pagamento', exact: true }).click();
  await expect(page.getByRole('region', { name: 'Pagamento atual', exact: true })).toContainText(
    'Pendente',
  );
}

test('pagamentos: dinheiro com troco vindo da API, histórico e devolução administrativa', async ({
  page,
}) => {
  const errors: string[] = [];
  page.on('pageerror', (error) => errors.push(error.message));
  const state = await setup(page, 'Administrator');
  await openPayments(page);
  await create(page, 'Cash');
  await page.getByRole('button', { name: 'Registrar recebimento', exact: true }).click();
  await page.getByLabel('Confirmei o recebimento e conferi o valor.', { exact: true }).check();
  for (const cash of ['', '-1', '59.79', '60.001', '10000000000']) {
    await page.getByLabel('Dinheiro entregue pelo cliente (R$)', { exact: true }).fill(cash);
    await expect(
      page.getByRole('button', { name: 'Confirmar registro', exact: true }),
    ).toBeDisabled();
  }
  await page.getByLabel('Dinheiro entregue pelo cliente (R$)', { exact: true }).fill('100');
  await page.getByRole('button', { name: 'Confirmar registro', exact: true }).click();
  await expect(page.getByRole('region', { name: 'Pagamento atual', exact: true })).toContainText(
    'Troco: R$ 40,20',
  );
  await expect(page.getByRole('region', { name: 'Pedido #1542' })).toContainText(
    'Status do pedido: Novo',
  );
  expect(state.commands[0].input).not.toHaveProperty('amount');
  expect(state.commands[1]).toEqual({
    action: 'receive',
    id: 'payment-1',
    input: { expectedVersion: 1, receivedConfirmed: true, cashTendered: 100 },
  });
  await page.getByRole('button', { name: 'Registrar devolução realizada', exact: true }).click();
  await page.getByLabel('Motivo', { exact: true }).fill('Devolvido em dinheiro');
  await expect(
    page.getByRole('button', { name: 'Confirmar registro', exact: true }),
  ).toBeDisabled();
  await page
    .getByLabel('Já devolvi o valor integral e conferi a devolução.', { exact: true })
    .check();
  await page.getByRole('button', { name: 'Confirmar registro', exact: true }).click();
  await expect(page.getByRole('region', { name: 'Histórico de pagamentos' })).toContainText(
    'Devolvido em dinheiro',
  );
  await expect(page.getByRole('region', { name: 'Pedido #1542' })).toContainText(
    'Saldo a receber: R$ 59,80',
  );
  await expect(page.getByRole('button', { name: 'Definir pagamento', exact: true })).toBeVisible();
  expect(state.commands[2]).toEqual({
    action: 'refund',
    id: 'payment-1',
    input: { expectedVersion: 2, reason: 'Devolvido em dinheiro', refundedConfirmed: true },
  });
  expect(errors).toEqual([]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

for (const method of ['Pix', 'CreditCard', 'DebitCard']) {
  test(
    'pagamentos: atendente registra ' + method + ' com confirmação explícita',
    async ({ page }) => {
      const state = await setup(page);
      await openPayments(page);
      await create(page, method);
      await page.getByRole('button', { name: 'Registrar recebimento', exact: true }).click();
      await expect(
        page.getByLabel('Dinheiro entregue pelo cliente (R$)', { exact: true }),
      ).toHaveCount(0);
      await expect(
        page.getByRole('button', { name: 'Confirmar registro', exact: true }),
      ).toBeDisabled();
      await page.getByLabel('Confirmei o recebimento e conferi o valor.', { exact: true }).check();
      await page.getByRole('button', { name: 'Confirmar registro', exact: true }).click();
      await expect(
        page.getByRole('region', { name: 'Pagamento atual', exact: true }),
      ).toContainText('Recebido');
      await expect(
        page.getByRole('button', { name: 'Registrar devolução realizada', exact: true }),
      ).toHaveCount(0);
      await expect(page.getByText('solicite ao administrador', { exact: false })).toBeVisible();
      expect(state.commands[1].input).toEqual({
        expectedVersion: 1,
        receivedConfirmed: true,
        cashTendered: null,
      });
    },
  );
}

test('pagamentos: cancela intenção com motivo e permite escolher outra forma', async ({ page }) => {
  const state = await setup(page);
  state.data.items = [pending()];
  await openPayments(page);
  await page.getByRole('button', { name: 'Cancelar intenção de pagamento', exact: true }).click();
  await expect(
    page.getByRole('button', { name: 'Confirmar registro', exact: true }),
  ).toBeDisabled();
  await page.getByLabel('Motivo', { exact: true }).fill('Troca para cartão');
  await page.getByRole('button', { name: 'Confirmar registro', exact: true }).click();
  await expect(page.getByRole('region', { name: 'Histórico de pagamentos' })).toContainText(
    'Troca para cartão',
  );
  await create(page, 'DebitCard');
  expect(state.commands[0]).toEqual({
    action: 'cancel',
    id: 'payment-1',
    input: { expectedVersion: 1, reason: 'Troca para cartão' },
  });
});

test('pagamentos: resposta perdida preserva criação e recebimento para repetição idêntica', async ({
  page,
}) => {
  const state = await setup(page);
  state.uncertainNext = true;
  await openPayments(page);
  await page.getByRole('button', { name: 'Definir pagamento', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('Não foi possível confirmar o resultado');
  await expect(
    page.getByRole('combobox', { name: 'Forma de pagamento', exact: true }),
  ).toBeDisabled();
  await expect(
    page.getByRole('button', { name: 'Atualizar pagamentos', exact: true }),
  ).toBeDisabled();
  await page.getByRole('button', { name: 'Repetir registro', exact: true }).click();
  await expect(page.getByRole('region', { name: 'Pagamento atual' })).toContainText('Pendente');
  expect(state.commands[0]).toEqual(state.commands[1]);
  expect(state.commands[0].input).toHaveProperty(
    'requestId',
    expect.stringMatching(/^[0-9a-f-]{36}$/),
  );
  state.uncertainNext = true;
  await page.getByRole('button', { name: 'Registrar recebimento', exact: true }).click();
  await page.getByLabel('Confirmei o recebimento e conferi o valor.', { exact: true }).check();
  await page.getByRole('button', { name: 'Confirmar registro', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText(
    'Não receba nem devolva o dinheiro novamente',
  );
  await expect(
    page.getByRole('button', { name: 'Confirmar registro', exact: true }),
  ).toBeDisabled();
  await page.getByRole('button', { name: 'Repetir registro', exact: true }).click();
  await expect(page.getByRole('region', { name: 'Pagamento atual' })).toContainText('Recebido');
  expect(state.commands[2]).toEqual(state.commands[3]);
  expect(state.data.items).toHaveLength(1);
  expect(state.data.items[0].history).toHaveLength(2);
});

test('pagamentos: envio bloqueia repetição e conflito exige nova consulta', async ({ page }) => {
  const state = await setup(page);
  let release = () => {};
  state.gate = new Promise<void>((resolve) => {
    release = resolve;
  });
  state.writesStatus = 409;
  state.writesCode = 'PaymentAlreadyActive';
  await openPayments(page);
  await page.getByRole('button', { name: 'Definir pagamento', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Registrando…', exact: true })).toBeDisabled();
  await expect(
    page.getByRole('combobox', { name: 'Forma de pagamento', exact: true }),
  ).toBeDisabled();
  release();
  await expect(page.getByRole('alert')).toContainText('já possui pagamento');
  await expect(page.getByRole('button', { name: 'Definir pagamento', exact: true })).toBeDisabled();
  state.data.items = [pending()];
  await page.getByRole('button', { name: 'Atualizar pagamentos', exact: true }).click();
  await expect(page.getByRole('region', { name: 'Pagamento atual' })).toContainText('Pendente');
  expect(state.commands).toHaveLength(1);
});

test('pagamentos: consulta falha após gravação e exige atualizar sem repetir comando', async ({
  page,
}) => {
  const state = await setup(page);
  await openPayments(page);
  await expect(page.getByRole('button', { name: 'Definir pagamento', exact: true })).toBeEnabled();
  state.listStatus = 503;
  await page.getByRole('button', { name: 'Definir pagamento', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('Atualize os pagamentos');
  await expect(page.getByRole('button', { name: 'Definir pagamento', exact: true })).toBeDisabled();
  await expect(page.getByRole('button', { name: 'Repetir registro', exact: true })).toHaveCount(0);
  state.listStatus = 200;
  await page.getByRole('button', { name: 'Atualizar pagamentos', exact: true }).click();
  await expect(page.getByRole('region', { name: 'Pagamento atual' })).toContainText('Pendente');
  expect(state.commands).toHaveLength(1);
});

test('pagamentos: pedido cancelado mantém histórico paginado sem novos registros', async ({
  page,
}) => {
  const state = await setup(page);
  state.data.orderStatus = 'Cancelled';
  for (let index = 0; index < 21; index++) {
    state.data.items.push({ ...pending(), id: 'old-' + index, status: 'Cancelled' });
  }
  state.listStatus = 503;
  await openPayments(page);
  await expect(page.getByRole('alert')).toBeVisible();
  state.listStatus = 200;
  await page.getByRole('button', { name: 'Atualizar pagamentos', exact: true }).click();
  await expect(
    page.getByText('Pedido cancelado. Novos pagamentos não estão disponíveis.', { exact: true }),
  ).toBeVisible();
  await expect(page.getByRole('button', { name: 'Definir pagamento', exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'Próxima', exact: true }).click();
  await expect(page.getByText('Página 2', { exact: true })).toBeVisible();
  await expect(
    page.getByRole('region', { name: 'Histórico de pagamentos' }).locator('article'),
  ).toHaveCount(1);
  expect(state.queries.at(-1)).toBe(2);
});

for (const role of ['Kitchen', 'Dispatch']) {
  test('pagamentos: ' + role + ' não acessa a rota', async ({ page }) => {
    const state = await setup(page, role);
    await login(page);
    await page.evaluate(() => {
      history.pushState(null, '', '/equipe/pedidos/order-1/pagamentos');
      dispatchEvent(new PopStateEvent('popstate'));
    });
    await expect(page).toHaveURL(/\/equipe$/);
    expect(state.requests).toBe(0);
  });
}

test('pagamentos: sessão revogada encerra tela sem novas operações', async ({ page }) => {
  const state = await setup(page);
  state.data.items = [pending()];
  await openPayments(page);
  state.writesStatus = 401;
  state.writesCode = 'InvalidSession';
  await page.getByRole('button', { name: 'Registrar recebimento', exact: true }).click();
  await page.getByLabel('Confirmei o recebimento e conferi o valor.', { exact: true }).check();
  await page.getByRole('button', { name: 'Confirmar registro', exact: true }).click();
  await expect(page).toHaveURL(/\/entrar$/);
  await expect(page.getByRole('heading', { name: 'Pagamentos', exact: true })).toHaveCount(0);
  expect(state.commands).toHaveLength(1);
});
