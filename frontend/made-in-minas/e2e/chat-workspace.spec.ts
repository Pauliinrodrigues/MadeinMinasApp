import { expect, Page, test } from '@playwright/test';
import type { ChatMessage, ChatMessageInput } from '../src/app/core/services/chat-api.service';

async function setup(page: Page) {
  await page.clock.install({ time: new Date('2026-10-06T15:00:00Z') });
  const chats = ['Maria', 'José'].map((name, index) => ({
    id: `11111111-1111-4111-8111-11111111111${index}`,
    visitorName: name,
    version: 1,
    status: 'InService',
    assignedToId: 'staff',
    assignedToName: 'Ana',
    createdAt: '2026-10-06T14:00:00Z',
    updatedAt: '2026-10-06T14:00:00Z',
    expiresAt: '2026-10-12T14:00:00Z',
    messages: [
      { sequence: 1, kind: 'Visitor', text: `Olá, sou ${name}`, createdAt: '2026-10-06T14:00:00Z' },
    ] as ChatMessage[],
  }));
  const state = {
    chats,
    queueReads: 0,
    lose: false,
    sends: [] as { id: string; input: ChatMessageInput }[],
    sent: new Map<string, ChatMessage>(),
  };
  const user = {
    id: 'staff',
    username: 'ana',
    name: 'Ana',
    role: 'Attendant',
    permissions: ['chat.manage'],
  };
  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    if (url.pathname === '/api/auth/login') {
      return route.fulfill({
        json: {
          user,
          accessToken: 'staff-token',
          tokenType: 'Bearer',
          expiresAt: '2026-10-06T15:15:00Z',
        },
      });
    }
    expect(request.headers()['authorization']).toBe('Bearer staff-token');
    if (url.pathname === '/api/auth/me') {
      return route.fulfill({ json: user });
    }
    if (url.pathname === '/api/chat') {
      state.queueReads++;
      const items = chats.filter((chat) => chat.status === url.searchParams.get('status'));
      return route.fulfill({ json: { items, page: 1, pageSize: 20, totalCount: items.length } });
    }
    const chat = chats.find((chat) => url.pathname.startsWith('/api/chat/' + chat.id));
    if (chat && request.method() === 'GET') {
      return route.fulfill({
        json: {
          conversation: chat,
          transcript: {
            ...chat,
            messages: chat.messages.filter(
              (m) => m.sequence > Number(url.searchParams.get('after')),
            ),
            hasMore: false,
          },
        },
      });
    }
    if (chat && url.pathname.endsWith('/messages')) {
      const input = request.postDataJSON() as ChatMessageInput;
      state.sends.push({ id: chat.id, input });
      let message = state.sent.get(input.requestId);
      if (!message) {
        message = {
          sequence: chat.messages.length + 1,
          kind: 'Staff',
          text: input.text,
          createdAt: '2026-10-06T15:00:00Z',
        };
        state.sent.set(input.requestId, message);
        chat.messages.push(message);
        chat.version++;
      }
      return state.lose ? route.abort('failed') : route.fulfill({ status: 201, json: message });
    }
    return route.fulfill({ status: 404, json: {} });
  });
  await page.goto('/entrar?returnUrl=%2Fequipe%2Fatendimentos');
  await page.getByLabel('Login', { exact: true }).fill('ana');
  await page.getByLabel('Senha', { exact: true }).fill('Senha de teste');
  await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await page.getByRole('combobox', { name: 'Status', exact: true }).selectOption('InService');
  return state;
}

async function open(page: Page, name: string, mobile: boolean) {
  if (mobile && (await page.getByRole('link', { name: 'Voltar à fila', exact: true }).count())) {
    await page.getByRole('link', { name: 'Voltar à fila', exact: true }).click();
  }
  await page.getByRole('link', { name, exact: true }).click();
  await expect(
    page
      .getByRole('region', { name: 'Conversa selecionada' })
      .getByRole('heading', { name, exact: true }),
  ).toBeVisible();
}

test('fila e conversa lado a lado no computador, navegação por tela no celular e rascunhos isolados', async ({
  page,
  isMobile,
}) => {
  const state = await setup(page);
  await open(page, 'Maria', isMobile);
  const queue = page.getByRole('region', { name: 'Fila de atendimento' });
  const conversation = page.getByRole('region', { name: 'Conversa selecionada' });
  if (isMobile) {
    await expect(queue).toBeHidden();
  } else {
    const left = await queue.boundingBox();
    const right = await conversation.boundingBox();
    expect(left!.x + left!.width).toBeLessThanOrEqual(right!.x);
  }
  await page.getByLabel('Resposta ao cliente').fill('Rascunho para Maria');
  await open(page, 'José', isMobile);
  await expect(page.getByLabel('Resposta ao cliente')).toHaveValue('');
  await expect(page.getByRole('list', { name: 'Mensagens da conversa' })).not.toContainText(
    'sou Maria',
  );
  await page.getByLabel('Resposta ao cliente').fill('Rascunho para José');
  await open(page, 'Maria', isMobile);
  await expect(page.getByLabel('Resposta ao cliente')).toHaveValue('Rascunho para Maria');
  const reads = state.queueReads;
  await page.clock.fastForward(10000);
  await expect.poll(() => state.queueReads).toBeGreaterThan(reads);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.screenshot({
    path: `../../.local/chat-workspace-${isMobile ? 'mobile' : 'desktop'}.png`,
    fullPage: true,
  });
});

test('atividade da conversa não aberta permanece marcada; abrir atualiza a versão lida', async ({
  page,
  isMobile,
}) => {
  const state = await setup(page);
  await open(page, 'Maria', isMobile);
  state.chats[1].version++;
  await page.clock.fastForward(10000);
  if (isMobile) {
    await page.getByRole('link', { name: 'Voltar à fila', exact: true }).click();
  }
  const queue = page.getByRole('region', { name: 'Fila de atendimento' });
  const maria = queue
    .getByRole('listitem')
    .filter({ has: page.getByRole('link', { name: 'Maria', exact: true }) });
  const jose = queue
    .getByRole('listitem')
    .filter({ has: page.getByRole('link', { name: 'José', exact: true }) });
  await expect(maria).not.toContainText('Nova atividade');
  await expect(jose).toContainText('Nova atividade');
  await open(page, 'José', isMobile);
  if (isMobile) {
    await page.getByRole('link', { name: 'Voltar à fila', exact: true }).click();
  }
  await expect(jose).not.toContainText('Nova atividade');
});

test('envio incerto sobrevive à troca sem enviar a mensagem para outra conversa', async ({
  page,
  isMobile,
}) => {
  const state = await setup(page);
  await open(page, 'Maria', isMobile);
  state.lose = true;
  await page.getByLabel('Resposta ao cliente').fill('Resposta recuperável para Maria');
  await page.getByRole('button', { name: 'Enviar resposta', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('Envio sem confirmação');
  await open(page, 'José', isMobile);
  await expect(page.getByRole('button', { name: 'Conferir mensagem' })).toHaveCount(0);
  await expect(page.getByLabel('Resposta ao cliente')).toHaveValue('');
  state.lose = false;
  await open(page, 'Maria', isMobile);
  await page.getByRole('button', { name: 'Conferir mensagem' }).click();
  await expect(page.getByLabel('Resposta ao cliente')).toHaveValue('');
  expect(state.sends).toHaveLength(2);
  expect(state.sends[1]).toEqual(state.sends[0]);
  expect(state.sent.size).toBe(1);
});

test('mudar filtro não fecha conversa e voltar preserva o filtro', async ({ page, isMobile }) => {
  await setup(page);
  await open(page, 'Maria', isMobile);
  await page.getByRole('link', { name: 'Voltar à fila', exact: true }).click();
  await expect(page.getByRole('combobox', { name: 'Status', exact: true })).toHaveValue(
    'InService',
  );
  if (!isMobile) {
    await open(page, 'Maria', false);
    await page.getByRole('combobox', { name: 'Status', exact: true }).selectOption('Closed');
    await expect(page.getByText('Nenhuma conversa neste status.')).toBeVisible();
    await expect(page.getByLabel('Resposta ao cliente')).toBeVisible();
  }
});
