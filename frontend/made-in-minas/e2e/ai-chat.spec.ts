import { expect, Page, test } from '@playwright/test';
import type {
  ChatAutomationMode,
  ChatAutomationState,
  ChatMessage,
  ChatOrderConfirmation,
  ChatOrderState,
  StaffChat,
} from '../src/app/core/services/chat-api.service';

const chatKey = 'made-in-minas.public-chat.v1';
const id = '11111111-1111-4111-8111-111111111111';
const productId = '22222222-2222-4222-8222-222222222222';
const access = { token: 'chat-ai-fixture', expiresAt: '2026-10-17T22:00:00Z' };
const createdAt = '2026-10-10T22:00:00Z';

const draft = (): ChatOrderState => ({
  draftVersion: 2,
  items: [
    {
      productId,
      name: 'Lanche cadastrado',
      quantity: 2,
      unitPrice: 20,
      lineTotal: 40,
      notes: null,
    },
  ],
  name: 'Maria',
  phone: '38999998888',
  fulfillment: 'Delivery',
  address: {
    areaId: id,
    neighborhood: 'Centro',
    street: 'Rua do cliente',
    number: '12',
    complement: null,
    postalCode: null,
    reference: null,
  },
  notes: null,
  quote: { items: [], notes: null, subtotal: 40, calculatedAt: createdAt },
  review: null,
  receipt: null,
  missingFields: [],
  needsReview: true,
});

async function setup(page: Page, order: ChatOrderState | null = null) {
  await page.clock.install({ time: new Date(createdAt) });
  await page.addInitScript(
    ({ key, access }) => {
      if (!sessionStorage.getItem(key)) {
        sessionStorage.setItem(
          key,
          JSON.stringify({
            version: 1,
            value: { access, pendingStart: null, pendingMessage: null, pendingOrder: null },
          }),
        );
      }
    },
    { key: chatKey, access },
  );
  const state = {
    mode: 'Automatic' as ChatAutomationMode,
    automationState: 'Idle' as ChatAutomationState,
    available: true,
    status: 'Waiting',
    assignedToId: null as string | null,
    version: 2,
    order,
    messages: [
      { sequence: 1, kind: 'Visitor', text: 'Boa noite! Qual é o cardápio?', createdAt },
      {
        sequence: 2,
        kind: 'Assistant',
        text: 'Boa noite! Vou consultar os produtos disponíveis.',
        createdAt,
      },
    ] as ChatMessage[],
    confirms: [] as ChatOrderConfirmation[],
    created: new Map<string, ChatOrderState>(),
    loseConfirm: false,
    exposeReceipt: false,
    reviewFailure: false,
    confirmFailure: '',
    handoffs: 0,
    claims: 0,
    automations: 0,
    sent: 0,
    linkedOrder: null as StaffChat['linkedOrder'],
    staffPermissions: ['chat.manage'],
  };
  const user = {
    id: 'staff',
    username: 'ana',
    name: 'Ana',
    role: 'Attendant',
    permissions: state.staffPermissions,
  };
  const transcript = (after: number) => ({
    status: state.status,
    version: state.version,
    createdAt,
    updatedAt: createdAt,
    messages: state.messages.filter((message) => message.sequence > after),
    hasMore: false,
    automationMode: state.mode,
    automationState: state.automationState,
    automationAvailable: state.available,
    order: state.order,
  });
  const summary = () => ({
    id,
    visitorName: 'Maria',
    status: state.status,
    assignedToId: state.assignedToId,
    assignedToName: state.assignedToId ? 'Ana' : null,
    version: state.version,
    createdAt,
    updatedAt: createdAt,
    expiresAt: access.expiresAt,
    automationMode: state.mode,
    automationAvailable: state.available,
  });
  const append = (kind: ChatMessage['kind'], text: string) => {
    state.messages.push({ sequence: state.messages.length + 1, kind, text, createdAt });
    state.version++;
  };
  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname;
    if (path === '/api/auth/login') {
      return route.fulfill({
        json: {
          user,
          accessToken: 'staff-token',
          tokenType: 'Bearer',
          expiresAt: '2026-10-10T22:15:00Z',
        },
      });
    }
    if (path === '/api/auth/me') {
      return route.fulfill({ json: user });
    }
    if (path.startsWith('/api/public-chat')) {
      expect(request.headers()['authorization']).toBeUndefined();
      expect(request.headers()['x-chat-access']).toBe(access.token);
      expect(url.href).not.toContain(access.token);
    }
    if (path === '/api/public-chat' && request.method() === 'GET') {
      if (state.exposeReceipt && state.created.size) {
        state.order = [...state.created.values()][0];
      }
      return route.fulfill({ json: transcript(Number(url.searchParams.get('after') ?? 0)) });
    }
    if (path === '/api/public-chat/messages') {
      const input = request.postDataJSON() as { text: string };
      state.sent++;
      append('Visitor', input.text);
      if (state.order) {
        state.order = {
          ...state.order,
          draftVersion: state.order.draftVersion + 1,
          review: null,
          needsReview: true,
          items: state.order.items.map((item) => ({ ...item, quantity: 3, lineTotal: 60 })),
          quote: { items: [], subtotal: 60, notes: null, calculatedAt: createdAt },
        };
      }
      return route.fulfill({ status: 201, json: state.messages.at(-1) });
    }
    if (path === '/api/public-chat/handoff') {
      expect(request.postDataJSON()).toEqual({ expectedVersion: state.version });
      state.handoffs++;
      state.mode = 'Manual';
      state.automationState = 'Idle';
      append('System', 'Solicitação de atendimento humano registrada.');
      return route.fulfill({ json: transcript(0) });
    }
    if (path === '/api/public-chat/order/review') {
      expect(request.postDataJSON()).toEqual({ expectedDraftVersion: state.order!.draftVersion });
      if (state.reviewFailure) {
        return route.fulfill({ status: 409, json: { code: 'CartProductUnavailable' } });
      }
      const current = state.order!;
      const subtotal = current.items.reduce((sum, item) => sum + item.lineTotal, 0);
      state.order = {
        ...current,
        needsReview: false,
        review: {
          items: current.items,
          notes: null,
          subtotal,
          calculatedAt: createdAt,
          name: current.name,
          phone: current.phone!,
          fulfillment: 'Delivery',
          address: {
            neighborhood: 'Centro',
            street: 'Rua do cliente',
            number: '12',
            complement: null,
            postalCode: null,
            reference: null,
            city: 'Corinto',
            state: 'MG',
          },
          deliveryFee: 5,
          total: subtotal + 5,
          reviewToken: 'review-fixture-' + current.draftVersion,
        },
      };
      return route.fulfill({ json: state.order });
    }
    if (path === '/api/public-chat/order/confirm') {
      const input = request.postDataJSON() as ChatOrderConfirmation;
      expect(Object.keys(input).sort()).toEqual([
        'expectedDraftVersion',
        'requestId',
        'reviewToken',
      ]);
      const stored = await page.evaluate(
        (key) => JSON.parse(sessionStorage.getItem(key)!).value.pendingOrder,
        chatKey,
      );
      expect(stored).toEqual(input);
      state.confirms.push(input);
      if (state.confirmFailure) {
        return route.fulfill({ status: 409, json: { code: state.confirmFailure } });
      }
      let result = state.created.get(input.requestId);
      if (!result) {
        result = {
          ...state.order!,
          receipt: {
            number: 1542,
            fulfillment: 'Delivery',
            total: state.order!.review!.total,
            createdAt,
            tracking: { token: 'tracking-ai-fixture', expiresAt: access.expiresAt },
          },
        };
        state.created.set(input.requestId, result);
      }
      if (state.loseConfirm) {
        return route.abort('failed');
      }
      state.order = result;
      return route.fulfill({ status: 201, json: result });
    }
    if (path === '/api/chat') {
      return route.fulfill({ json: { items: [summary()], page: 1, pageSize: 20, totalCount: 1 } });
    }
    if (path === '/api/chat/' + id && request.method() === 'GET') {
      return route.fulfill({
        json: {
          conversation: summary(),
          transcript: transcript(Number(url.searchParams.get('after') ?? 0)),
          linkedOrder: state.linkedOrder,
        },
      });
    }
    if (path === '/api/chat/' + id + '/claim') {
      state.claims++;
      state.status = 'InService';
      state.assignedToId = 'staff';
      state.mode = 'Manual';
      append('System', 'Um atendente assumiu a conversa.');
      return route.fulfill({ json: summary() });
    }
    if (path === '/api/chat/' + id + '/automation') {
      expect(request.postDataJSON()).toEqual({ expectedVersion: state.version });
      state.automations++;
      state.mode = 'Automatic';
      state.status = 'Waiting';
      state.assignedToId = null;
      state.version++;
      return route.fulfill({ json: summary() });
    }
    return route.fulfill({ status: 404, json: {} });
  });
  return state;
}

async function openReview(page: Page) {
  await page.goto('/pedido/atendimento');
  await page.getByRole('button', { name: 'Revisar pedido', exact: true }).click();
  await expect(page.getByRole('region', { name: 'Resumo para confirmação' })).toBeVisible();
}

test('identifica IA, mostra processamento e permite transferência persistida para humano', async ({
  page,
}) => {
  const state = await setup(page);
  state.automationState = 'Processing';
  await page.goto('/pedido/atendimento');
  await expect(
    page.getByRole('heading', { name: 'Assistente virtual', exact: true }),
  ).toBeVisible();
  await expect(page.getByRole('list', { name: 'Mensagens da conversa' })).toContainText(
    'Assistente virtual',
  );
  await expect(page.getByRole('status')).toContainText('preparando a resposta');
  await page.getByRole('button', { name: 'Falar com atendente humano' }).click();
  await expect(
    page.getByRole('heading', { name: 'Aguardando atendente', exact: true }),
  ).toBeVisible();
  await expect(page.getByRole('button', { name: 'Falar com atendente humano' })).toHaveCount(0);
  await page.reload();
  await expect(
    page.getByRole('heading', { name: 'Aguardando atendente', exact: true }),
  ).toBeVisible();
  expect(state.handoffs).toBe(1);
});

test('dados faltantes impedem revisão e pedido exige resumo atualizado após mudar quantidade', async ({
  page,
}) => {
  const incomplete = draft();
  incomplete.phone = null;
  incomplete.missingFields = ['phone'];
  const state = await setup(page, incomplete);
  await page.goto('/pedido/atendimento');
  await expect(page.getByText('Informe na conversa: telefone com DDD.')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Confirmar pedido' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Revisar pedido', exact: true })).toHaveCount(0);
  state.order = draft();
  await page.clock.fastForward(5000);
  await page.getByRole('button', { name: 'Revisar pedido', exact: true }).click();
  const summary = page.getByRole('region', { name: 'Resumo para confirmação' });
  await expect(summary).toContainText('Maria · 38999998888');
  await expect(summary).toContainText('Rua do cliente, 12');
  await expect(summary).toContainText('Total: R$ 45,00');
  expect(state.confirms).toHaveLength(0);
  await page.getByLabel('Sua mensagem', { exact: true }).fill('Altere para três lanches');
  await page.getByRole('button', { name: 'Enviar mensagem', exact: true }).click();
  await expect(summary).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Confirmar pedido' })).toHaveCount(0);
  await expect(page.getByText('3 × Lanche cadastrado', { exact: false })).toBeVisible();
  await page.getByRole('button', { name: 'Revisar pedido', exact: true }).click();
  await expect(summary).toContainText('Total: R$ 65,00');
  await page.getByRole('button', { name: 'Confirmar pedido', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Pedido #1542 enviado' })).toBeVisible();
  expect(state.confirms[0].expectedDraftVersion).toBe(3);
});

test('confirmação incerta sobrevive ao reload e repete o mesmo UUID sem criar outro pedido', async ({
  page,
}) => {
  const state = await setup(page, draft());
  await openReview(page);
  state.loseConfirm = true;
  await page.getByRole('button', { name: 'Confirmar pedido', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('Envio sem confirmação');
  await expect(page.getByRole('heading', { name: 'Pedido #1542 enviado' })).toHaveCount(0);
  await expect(
    page
      .getByLabel('Pedido no chat')
      .getByText('Seu pedido ainda não foi enviado', { exact: false }),
  ).toHaveCount(0);
  await expect(page.getByRole('status')).toContainText(
    'O envio do pedido ainda não foi confirmado',
  );
  await expect(page.getByLabel('Sua mensagem', { exact: true })).toBeDisabled();
  await page.reload();
  await expect(page.getByRole('button', { name: 'Conferir envio', exact: true })).toBeVisible();
  await expect(
    page
      .getByLabel('Pedido no chat')
      .getByText('Seu pedido ainda não foi enviado', { exact: false }),
  ).toHaveCount(0);
  state.loseConfirm = false;
  await page.getByRole('button', { name: 'Conferir envio', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Pedido #1542 enviado' })).toBeVisible();
  expect(state.confirms).toHaveLength(2);
  expect(state.confirms[1]).toEqual(state.confirms[0]);
  expect(state.created.size).toBe(1);
  await expect(page.getByRole('link', { name: 'Acompanhar pedido #1542' })).toHaveAttribute(
    'href',
    '/pedido/acompanhar',
  );
  const history = await page.evaluate(() =>
    JSON.parse(sessionStorage.getItem('made-in-minas.public-order-history.v1')!),
  );
  expect(history).toHaveLength(1);
  expect(JSON.stringify(history)).not.toContain('Rua do cliente');
  expect(JSON.stringify(history)).not.toContain('38999998888');
});

test('resumo revisado aguarda o processamento da última mensagem antes de confirmar', async ({
  page,
}) => {
  const state = await setup(page, draft());
  await openReview(page);
  state.automationState = 'Processing';
  await page.clock.fastForward(5000);
  await expect(page.getByRole('status')).toContainText('preparando a resposta');
  await expect(page.getByRole('button', { name: 'Confirmar pedido', exact: true })).toBeDisabled();
  expect(state.confirms).toHaveLength(0);
  state.automationState = 'Idle';
  await page.clock.fastForward(5000);
  await expect(page.getByRole('button', { name: 'Confirmar pedido', exact: true })).toBeEnabled();
  await page.getByRole('button', { name: 'Confirmar pedido', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Pedido #1542 enviado' })).toBeVisible();
  expect(state.confirms).toHaveLength(1);
});

test('polling recupera recibo após perda da resposta e não exige outra criação', async ({
  page,
}) => {
  const state = await setup(page, draft());
  await openReview(page);
  state.loseConfirm = true;
  await page.getByRole('button', { name: 'Confirmar pedido', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Conferir envio', exact: true })).toBeVisible();
  state.exposeReceipt = true;
  await page.clock.fastForward(5000);
  await expect(page.getByRole('heading', { name: 'Pedido #1542 enviado' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Conferir envio', exact: true })).toHaveCount(0);
  expect(state.confirms).toHaveLength(1);
});

test('falha da ferramenta e produto indisponível não mostram pedido criado', async ({ page }) => {
  const state = await setup(page, draft());
  state.reviewFailure = true;
  await page.goto('/pedido/atendimento');
  await page.getByRole('button', { name: 'Revisar pedido', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('indisponível');
  await expect(page.getByRole('button', { name: 'Confirmar pedido' })).toHaveCount(0);
  state.reviewFailure = false;
  await page.getByRole('button', { name: 'Revisar pedido', exact: true }).click();
  state.confirmFailure = 'CartProductUnavailable';
  await page.getByRole('button', { name: 'Confirmar pedido', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('Pedido não enviado');
  await expect(page.getByRole('heading', { name: 'Pedido #1542 enviado' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Conferir envio' })).toHaveCount(0);
  expect(state.created.size).toBe(0);
});

test('modo manual e indisponibilidade preservam envio de mensagens', async ({ page }) => {
  const state = await setup(page);
  state.mode = 'Manual';
  state.available = false;
  state.automationState = 'Unavailable';
  state.messages = state.messages.filter((message) => message.kind === 'Visitor');
  await page.goto('/pedido/atendimento');
  await expect(
    page.getByRole('heading', { name: 'Aguardando atendente', exact: true }),
  ).toBeVisible();
  await expect(page.getByRole('status')).toContainText('indisponível');
  await expect(page.getByRole('button', { name: 'Falar com atendente humano' })).toHaveCount(0);
  await page.getByLabel('Sua mensagem', { exact: true }).fill('Preciso falar com uma pessoa');
  await page.getByRole('button', { name: 'Enviar mensagem', exact: true }).click();
  await expect(page.getByRole('list', { name: 'Mensagens da conversa' })).toContainText(
    'Preciso falar com uma pessoa',
  );
  expect(state.sent).toBe(1);
});

test('atendente assume e retoma automação com versão atual sem respostas concorrentes', async ({
  page,
}) => {
  const state = await setup(page);
  await page.goto('/entrar?returnUrl=%2Fequipe%2Fatendimentos');
  await page.getByLabel('Login', { exact: true }).fill('ana');
  await page.getByLabel('Senha', { exact: true }).fill('Senha de teste');
  await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await page.getByRole('link', { name: 'Maria', exact: true }).click();
  await page.getByRole('button', { name: 'Assumir atendimento', exact: true }).click();
  await expect(page.getByLabel('Resposta ao cliente')).toBeVisible();
  expect(state.mode).toBe('Manual');
  await page.getByRole('button', { name: 'Retomar assistente virtual', exact: true }).click();
  await expect(page.getByLabel('Resposta ao cliente')).toHaveCount(0);
  await expect(page.getByRole('region', { name: 'Conversa selecionada' })).toContainText(
    'Assistente virtual',
  );
  expect(state.claims).toBe(1);
  expect(state.automations).toBe(1);
});

async function openStaffConversation(page: Page) {
  await page.goto('/entrar?returnUrl=' + encodeURIComponent('/equipe/atendimentos/' + id));
  await page.getByLabel('Login', { exact: true }).fill('ana');
  await page.getByLabel('Senha', { exact: true }).fill('Senha de teste');
  await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await expect(page.getByRole('region', { name: 'Conversa selecionada' })).toBeVisible();
}

test('equipe distingue montagem e campos ausentes sem criar ou aceitar pedido', async ({
  page,
}) => {
  const order = {
    ...draft(),
    phone: null,
    fulfillment: null,
    address: null,
    missingFields: ['phone', 'fulfillment'],
  };
  const state = await setup(page, order);
  await openStaffConversation(page);
  const summary = page.getByRole('region', { name: 'Pedido desta conversa' });
  await expect(summary).toContainText('Pedido em montagem');
  await expect(summary).toContainText('Ainda não aparece na fila de Pedidos');
  await expect(summary).toContainText('telefone com DDD, entrega ou retirada');
  await expect(summary).toContainText('2 × Lanche cadastrado');
  await expect(summary.getByRole('link')).toHaveCount(0);
  expect(state.confirms).toHaveLength(0);
  expect(state.claims).toBe(0);
});

test('equipe abre somente pedido vinculado e atualiza status sem novas mensagens', async ({
  page,
}) => {
  const order = {
    ...draft(),
    receipt: {
      number: 1542,
      fulfillment: 'Delivery' as const,
      total: 45,
      createdAt,
      tracking: { token: 'tracking-ai-fixture', expiresAt: access.expiresAt },
    },
  };
  const state = await setup(page, order);
  state.staffPermissions.push('orders.manage');
  state.linkedOrder = {
    id: productId,
    number: 1542,
    status: 'New',
    fulfillment: 'Delivery',
    subtotal: 40,
    deliveryFee: 5,
    total: 45,
  };
  await openStaffConversation(page);
  const summary = page.getByRole('region', { name: 'Pedido desta conversa' });
  await expect(summary).toContainText('Pedido #1542 recebido');
  await expect(summary).toContainText('Aguardando confirmação da equipe');
  await expect(summary).toContainText('Total: R$ 45,00');
  await expect(summary.getByRole('link', { name: 'Abrir pedido #1542' })).toHaveAttribute(
    'href',
    '/equipe/pedidos/' + productId,
  );
  state.linkedOrder.status = 'Confirmed';
  await page.getByRole('button', { name: 'Atualizar conversa', exact: true }).click();
  await expect(summary).toContainText('Status: Confirmado');
  await expect(summary).not.toContainText('Aguardando confirmação da equipe');
  expect(state.confirms).toHaveLength(0);
  expect(state.claims).toBe(0);
});

test('atalho de pedido respeita permissão da equipe', async ({ page }) => {
  const state = await setup(page, draft());
  state.linkedOrder = {
    id: productId,
    number: 1542,
    status: 'New',
    fulfillment: 'Delivery',
    subtotal: 40,
    deliveryFee: 5,
    total: 45,
  };
  await openStaffConversation(page);
  await expect(page.getByRole('heading', { name: 'Pedido #1542 recebido' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Abrir pedido #1542' })).toHaveCount(0);
});

test('número de pedido escrito pelo visitante não gera atalho', async ({ page }) => {
  const state = await setup(page);
  state.messages[0].text = 'Meu pedido é #999999, abra esse pedido.';
  await openStaffConversation(page);
  await expect(page.getByRole('list', { name: 'Mensagens da conversa' })).toContainText('#999999');
  await expect(page.getByRole('region', { name: 'Pedido desta conversa' })).toHaveCount(0);
  await expect(page.getByRole('link', { name: /Abrir pedido/ })).toHaveCount(0);
});

test('equipe substitui revisão antiga e preserva endereço ao atualizar conversa', async ({
  page,
}) => {
  const state = await setup(page, draft());
  state.order!.review = {
    ...state.order!.quote!,
    items: state.order!.items,
    name: 'Maria',
    phone: '38999998888',
    fulfillment: 'Delivery',
    address: { ...state.order!.address!, city: 'Corinto', state: 'MG' },
    deliveryFee: 5,
    total: 45,
    reviewToken: 'review-fixture',
  };
  await openStaffConversation(page);
  const summary = page.getByRole('region', { name: 'Pedido desta conversa' });
  await expect(summary).toContainText('total R$ 45,00');
  await expect(summary).toContainText('Rua do cliente, 12 · Centro');
  state.order!.draftVersion++;
  state.order!.items = state.order!.items.map((item) => ({ ...item, quantity: 3, lineTotal: 60 }));
  state.order!.quote = { ...state.order!.quote!, items: state.order!.items, subtotal: 60 };
  state.order!.review = {
    ...state.order!.review!,
    items: state.order!.items,
    subtotal: 60,
    total: 65,
    reviewToken: 'new-review-fixture',
  };
  await page.getByRole('button', { name: 'Atualizar conversa', exact: true }).click();
  await expect(summary).toContainText('total R$ 65,00');
  await expect(summary).not.toContainText('total R$ 45,00');
  expect(state.confirms).toHaveLength(0);
});

test('cliente vê que montagem ainda não foi enviada antes de confirmar', async ({ page }) => {
  const state = await setup(page, draft());
  await page.goto('/pedido/atendimento');
  await expect(page.getByRole('region', { name: 'Pedido no chat' })).toContainText(
    'Seu pedido ainda não foi enviado',
  );
  expect(state.confirms).toHaveLength(0);
});
