import { expect, Page, test } from '@playwright/test';
import type {
  ChatMessage,
  ChatMessageInput,
  ChatStatus,
  StartChatInput,
} from '../src/app/core/services/chat-api.service';

const publicKey = 'made-in-minas.public-chat.v1';
const id = '11111111-1111-4111-8111-111111111111';
const access = { token: 'chat-secret-fixture', expiresAt: '2026-10-12T14:00:00Z' };

async function setup(page: Page, role = 'Attendant') {
  await page.clock.install({ time: new Date('2026-10-05T14:00:00Z') });
  const state = {
    starts: [] as StartChatInput[],
    sends: [] as ChatMessageInput[],
    calls: 0,
    messages: [] as ChatMessage[],
    status: 'Waiting' as ChatStatus,
    assignedToId: null as string | null,
    readStatus: 200,
    loseStart: false,
    loseMessage: false,
    gate: null as Promise<void> | null,
    sent: new Map<string, ChatMessage>(),
    claimStatus: 200,
  };
  const user = {
    id: 'staff',
    name: 'Ana',
    username: 'ana',
    role,
    permissions: ['Administrator', 'Attendant'].includes(role) ? ['chat.manage'] : [],
  };
  const summary = () => ({
    id,
    visitorName: 'Maria',
    status: state.status,
    assignedToId: state.assignedToId,
    assignedToName: state.assignedToId ? 'Ana' : null,
    version: state.messages.length,
    createdAt: '2026-10-05T14:00:00Z',
    updatedAt: '2026-10-05T14:00:00Z',
    expiresAt: access.expiresAt,
  });
  const append = (kind: ChatMessage['kind'], text: string) => {
    const message = {
      sequence: state.messages.length + 1,
      kind,
      text,
      createdAt: '2026-10-05T14:00:00Z',
    };
    state.messages.push(message);
    return message;
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
          expiresAt: '2026-10-05T14:15:00Z',
        },
      });
    }
    if (path === '/api/auth/me') {
      expect(request.headers()['authorization']).toBe('Bearer staff-token');
      return route.fulfill({ json: user });
    }
    if (path === '/api/auth/logout') {
      return route.fulfill({ status: 204 });
    }
    const isPublic = path.startsWith('/api/public-chat');
    const isStaff = path.startsWith('/api/chat');
    if (isPublic) {
      expect(request.headers()['authorization']).toBeUndefined();
      expect(url.href).not.toContain(access.token);
    }
    if (isStaff) {
      expect(request.headers()['authorization']).toBe('Bearer staff-token');
    }
    if (path === '/api/public-chat' && request.method() === 'POST') {
      const input = request.postDataJSON() as StartChatInput;
      state.starts.push(input);
      if (!state.messages.length) {
        append('Visitor', input.text);
      }
      if (state.loseStart) {
        return route.abort('failed');
      }
      return route.fulfill({ status: 201, json: { access, createdAt: '2026-10-05T14:00:00Z' } });
    }
    if (path === '/api/public-chat' || path === '/api/chat/' + id) {
      if (isPublic) {
        expect(request.headers()['x-chat-access']).toBe(access.token);
      }
      state.calls++;
      if (state.gate) {
        await state.gate;
      }
      if (state.readStatus !== 200) {
        return route.fulfill({ status: state.readStatus, json: { code: 'ChatUnavailable' } });
      }
      const messages = state.messages.filter(
        (message) => message.sequence > Number(url.searchParams.get('after') || 0),
      );
      const transcript = {
        status: state.status,
        version: state.messages.length,
        createdAt: '2026-10-05T14:00:00Z',
        updatedAt: '2026-10-05T14:00:00Z',
        messages: messages.slice(0, 50),
        hasMore: messages.length > 50,
      };
      return route.fulfill({
        json: isPublic ? transcript : { conversation: summary(), transcript },
      });
    }
    if (path === '/api/public-chat/messages' || path === '/api/chat/' + id + '/messages') {
      if (isPublic) {
        expect(request.headers()['x-chat-access']).toBe(access.token);
      }
      const input = request.postDataJSON() as ChatMessageInput;
      state.sends.push(input);
      expect(Object.keys(input).sort()).toEqual(['requestId', 'text']);
      const message =
        state.sent.get(input.requestId) ?? append(isPublic ? 'Visitor' : 'Staff', input.text);
      state.sent.set(input.requestId, message);
      if (state.loseMessage) {
        return route.abort('failed');
      }
      return route.fulfill({ status: 201, json: message });
    }
    if (path === '/api/chat') {
      return route.fulfill({
        json: {
          items: state.status === url.searchParams.get('status') ? [summary()] : [],
          page: 1,
          pageSize: 20,
          totalCount: state.status === url.searchParams.get('status') ? 1 : 0,
        },
      });
    }
    if (path.endsWith('/claim')) {
      if (state.claimStatus !== 200) {
        return route.fulfill({ status: state.claimStatus, json: { code: 'ChatVersionConflict' } });
      }
      state.status = 'InService';
      state.assignedToId = 'staff';
      append('System', 'Um atendente assumiu a conversa.');
      return route.fulfill({ json: summary() });
    }
    if (path.endsWith('/close')) {
      state.status = 'Closed';
      append('System', 'Atendimento encerrado.');
      return route.fulfill({ json: summary() });
    }
    if (path === '/api/menu') {
      return route.fulfill({
        json: { categories: [], items: [], page: 1, pageSize: 24, totalCount: 0 },
      });
    }
    return route.fulfill({ status: 404, json: {} });
  });
  return { state, append };
}

async function begin(page: Page) {
  await page.goto('/pedido/atendimento');
  await page.getByLabel('Seu nome', { exact: true }).fill('Maria');
  await page.getByLabel('Sua mensagem', { exact: true }).fill('Tenho uma dúvida');
  await page.getByRole('button', { name: 'Solicitar atendimento' }).click();
}
async function login(page: Page) {
  await page.goto('/entrar');
  await page.getByLabel('Login', { exact: true }).fill('ana');
  await page.getByLabel('Senha', { exact: true }).fill('Senha teste 123!');
  await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await expect(page).toHaveURL(/\/equipe$/);
}
async function seedAccess(page: Page) {
  await page.addInitScript(
    ({ key, access }) => {
      if (!sessionStorage.getItem(key)) {
        sessionStorage.setItem(
          key,
          JSON.stringify({
            version: 1,
            value: { access, pendingStart: null, pendingMessage: null },
          }),
        );
      }
    },
    { key: publicKey, access },
  );
}

test('visitante pede ajuda pelo cardápio, recebe resposta e conserva acesso ao recarregar', async ({
  page,
}) => {
  const { state, append } = await setup(page);
  await page.goto('/pedido');
  await page.getByRole('link', { name: 'Falar com atendente' }).click();
  await page.getByLabel('Seu nome', { exact: true }).fill('Maria');
  await page.getByLabel('Sua mensagem', { exact: true }).fill('Tenho uma dúvida');
  await page.getByRole('button', { name: 'Solicitar atendimento' }).click();
  await expect(page.getByRole('heading', { name: 'Aguardando atendimento' })).toBeVisible();
  expect(state.starts).toHaveLength(1);
  state.status = 'InService';
  append('Staff', 'Olá, pode perguntar.');
  await page.clock.fastForward(5000);
  await expect(page.getByText('Olá, pode perguntar.', { exact: true })).toBeVisible();
  await page.getByLabel('Sua mensagem', { exact: true }).fill('Há opção sem queijo?');
  await page.getByRole('button', { name: 'Enviar mensagem', exact: true }).click();
  await expect(page.getByRole('list', { name: 'Mensagens da conversa' })).toContainText(
    'Há opção sem queijo?',
  );
  await page.reload();
  await expect(page.getByText('Olá, pode perguntar.', { exact: true })).toBeVisible();
  expect(state.starts).toHaveLength(1);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('solicitação perdida é recuperada com a mesma tentativa após reload', async ({ page }) => {
  const { state } = await setup(page);
  state.loseStart = true;
  await begin(page);
  await expect(page.getByRole('alert')).toContainText('confirmar a solicitação');
  await page.reload();
  state.loseStart = false;
  await page.getByRole('button', { name: 'Conferir solicitação' }).click();
  await expect(page.getByRole('heading', { name: 'Aguardando atendimento' })).toBeVisible();
  expect(state.starts[1]).toEqual(state.starts[0]);
  expect(state.messages).toHaveLength(1);
});

test('mensagem perdida é recuperada sem duplicar e sem liberar outro texto', async ({ page }) => {
  const { state } = await setup(page);
  await begin(page);
  await expect(page.getByRole('heading', { name: 'Aguardando atendimento' })).toBeVisible();
  state.loseMessage = true;
  await page.getByLabel('Sua mensagem', { exact: true }).fill('Minha pergunta');
  await page.getByRole('button', { name: 'Enviar mensagem', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('Envio sem confirmação');
  await expect(page.getByRole('textbox', { name: 'Sua mensagem' })).toHaveCount(0);
  await page.reload();
  state.loseMessage = false;
  await page.getByRole('button', { name: 'Conferir mensagem' }).click();
  await expect(page.getByRole('button', { name: 'Enviar mensagem', exact: true })).toBeVisible();
  expect(state.sends[1]).toEqual(state.sends[0]);
  expect(state.sent.size).toBe(1);
});

test('encerramento para consultas e permite solicitar outra conversa', async ({ page }) => {
  const { state, append } = await setup(page);
  await seedAccess(page);
  state.status = 'Closed';
  append('System', 'Atendimento encerrado.');
  await page.goto('/pedido/atendimento');
  await expect(page.getByRole('heading', { name: 'Encerrado', exact: true })).toBeVisible();
  const calls = state.calls;
  await page.clock.fastForward(30000);
  expect(state.calls).toBe(calls);
  await expect(page.getByRole('button', { name: 'Enviar mensagem', exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'Iniciar outro atendimento' }).click();
  await expect(page.getByRole('button', { name: 'Solicitar atendimento' })).toBeVisible();
});

test('mensagens paginadas são carregadas sem lacunas ou duplicatas', async ({ page }) => {
  const { append } = await setup(page);
  await seedAccess(page);
  for (let index = 0; index < 105; index++) {
    append('Visitor', 'Mensagem ' + index);
  }
  await page.goto('/pedido/atendimento');
  await expect(page.getByRole('listitem')).toHaveCount(105);
  await page.clock.fastForward(5000);
  await expect(page.getByRole('listitem')).toHaveCount(105);
});

test('falha na consulta bloqueia novo envio e retoma sem apagar histórico', async ({ page }) => {
  const { state } = await setup(page);
  await begin(page);
  await expect(page.getByRole('heading', { name: 'Aguardando atendimento' })).toBeVisible();
  state.readStatus = 503;
  await page.clock.fastForward(5000);
  await expect(page.getByRole('alert')).toContainText('sem atualização');
  await expect(page.getByRole('button', { name: 'Enviar mensagem', exact: true })).toBeDisabled();
  await expect(page.getByRole('listitem')).toHaveCount(1);
  state.readStatus = 200;
  await page.clock.fastForward(15000);
  await expect(page.getByRole('alert')).toHaveCount(0);
});

test('acesso inválido para consultas e informa recuperação', async ({ page }) => {
  const { state } = await setup(page);
  await seedAccess(page);
  state.readStatus = 404;
  await page.goto('/pedido/atendimento');
  await expect(page.getByRole('alert')).toContainText('acesso expirou');
  const calls = state.calls;
  await page.clock.fastForward(60000);
  expect(state.calls).toBe(calls);
  await expect(page.getByRole('button', { name: 'Recomeçar atendimento' })).toBeVisible();
});

test('armazenamento indisponível impede solicitação sem recuperação', async ({ page }) => {
  const { state } = await setup(page);
  await page.goto('/pedido/atendimento');
  await page.getByLabel('Seu nome', { exact: true }).fill('Maria');
  await page.getByLabel('Sua mensagem', { exact: true }).fill('Ajuda');
  await page.evaluate(() => {
    Storage.prototype.setItem = () => {
      throw new DOMException('Full', 'QuotaExceededError');
    };
  });
  await page.getByRole('button', { name: 'Solicitar atendimento' }).click();
  await expect(page.getByRole('alert')).toContainText('guardar a recuperação');
  expect(state.starts).toHaveLength(0);
});

test('HTML é exibido como texto e ocultar ou sair pausa consultas', async ({ page }) => {
  const { state, append } = await setup(page);
  await seedAccess(page);
  append('Staff', '<img src=x onerror=alert(1)>');
  await page.goto('/pedido/atendimento');
  await expect(page.getByText('<img src=x onerror=alert(1)>', { exact: true })).toBeVisible();
  await expect(page.locator('app-chat-messages img')).toHaveCount(0);
  await page.evaluate(() => {
    Object.defineProperty(document, 'hidden', { configurable: true, value: true });
    document.dispatchEvent(new Event('visibilitychange'));
  });
  const calls = state.calls;
  await page.clock.fastForward(30000);
  expect(state.calls).toBe(calls);
  await page.evaluate(() => {
    Object.defineProperty(document, 'hidden', { configurable: true, value: false });
    document.dispatchEvent(new Event('visibilitychange'));
  });
  await expect.poll(() => state.calls).toBe(calls + 1);
  await page.getByRole('link', { name: 'Voltar ao cardápio' }).click();
  await expect(page).toHaveURL(/\/pedido$/);
  const afterLeave = state.calls;
  await page.clock.fastForward(30000);
  expect(state.calls).toBe(afterLeave);
});

test('atendente assume, responde e confirma encerramento', async ({ page }) => {
  const { state, append } = await setup(page);
  append('Visitor', 'Olá');
  await login(page);
  await page.getByRole('link', { name: 'Atendimentos', exact: true }).click();
  await page.getByRole('link', { name: 'Maria', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Enviar resposta' })).toHaveCount(0);
  await page.getByRole('button', { name: 'Assumir atendimento' }).click();
  await page.getByLabel('Resposta ao cliente').fill('Olá, Maria!');
  await page.getByRole('button', { name: 'Enviar resposta', exact: true }).click();
  await expect(page.getByRole('list', { name: 'Mensagens da conversa' })).toContainText(
    'Olá, Maria!',
  );
  await page.getByRole('button', { name: 'Encerrar atendimento', exact: true }).click();
  expect(state.status).toBe('InService');
  await page.getByRole('button', { name: 'Confirmar encerramento' }).click();
  await expect(page.getByRole('list', { name: 'Mensagens da conversa' })).toContainText(
    'Atendimento encerrado.',
  );
  await expect(page.getByRole('textbox', { name: 'Resposta ao cliente' })).toHaveCount(0);
  expect(state.status).toBe('Closed');
});

test('resposta da equipe incerta conserva a chave mesmo após novo login', async ({ page }) => {
  const { state, append } = await setup(page);
  append('Visitor', 'Olá');
  state.status = 'InService';
  state.assignedToId = 'staff';
  await login(page);
  await page.getByRole('link', { name: 'Atendimentos', exact: true }).click();
  await page.getByRole('combobox', { name: 'Status', exact: true }).selectOption('InService');
  await page.getByRole('link', { name: 'Maria', exact: true }).click();
  state.loseMessage = true;
  await page.getByLabel('Resposta ao cliente').fill('Resposta recuperável');
  await page.getByRole('button', { name: 'Enviar resposta', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('Envio sem confirmação');
  await login(page);
  state.loseMessage = false;
  await page.getByRole('link', { name: 'Atendimentos', exact: true }).click();
  await page.getByRole('combobox', { name: 'Status', exact: true }).selectOption('InService');
  await page.getByRole('link', { name: 'Maria', exact: true }).click();
  await page.getByRole('button', { name: 'Conferir mensagem' }).click();
  await expect(page.getByRole('textbox', { name: 'Resposta ao cliente' })).toBeVisible();
  expect(state.sends[1]).toEqual(state.sends[0]);
  expect(state.sent.size).toBe(1);
});

for (const role of ['Kitchen', 'Dispatch']) {
  test(`${role} não acessa a fila nem conversa por rota direta`, async ({ page }) => {
    await setup(page, role);
    await login(page);
    await expect(page.getByRole('link', { name: 'Atendimentos', exact: true })).toHaveCount(0);
    await page.evaluate((id) => {
      history.pushState(null, '', '/equipe/atendimentos/' + id);
      dispatchEvent(new PopStateEvent('popstate'));
    }, id);
    await expect(page).toHaveURL(/\/equipe$/);
  });
}

test('conflito ao assumir exige atualização e não libera resposta', async ({ page }) => {
  const { state, append } = await setup(page);
  append('Visitor', 'Olá');
  await login(page);
  await page.getByRole('link', { name: 'Atendimentos', exact: true }).click();
  await page.getByRole('link', { name: 'Maria', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Assumir atendimento' })).toBeVisible();
  state.claimStatus = 409;
  state.status = 'InService';
  state.assignedToId = 'other-staff';
  await page.getByRole('button', { name: 'Assumir atendimento' }).click();
  await expect(page.getByRole('alert')).toContainText('A conversa mudou');
  await expect(page.getByRole('textbox', { name: 'Resposta ao cliente' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Assumir atendimento' })).toHaveCount(0);
});

test('chat público não envia JWT nem encerra sessão após falha', async ({ page }) => {
  const { state } = await setup(page);
  await seedAccess(page);
  await login(page);
  state.readStatus = 401;
  await page.evaluate(() => {
    history.pushState(null, '', '/pedido/atendimento');
    dispatchEvent(new PopStateEvent('popstate'));
  });
  await expect(page.getByRole('alert')).toContainText('sem atualização');
  await page.evaluate(() => {
    history.pushState(null, '', '/equipe');
    dispatchEvent(new PopStateEvent('popstate'));
  });
  await expect(page.getByRole('heading', { name: 'Olá, Ana.' })).toBeVisible();
});

test('administrador pode assumir conversa de outro atendente', async ({ page }) => {
  const { state, append } = await setup(page, 'Administrator');
  append('Visitor', 'Olá');
  state.status = 'InService';
  state.assignedToId = 'other-staff';
  await login(page);
  await page.getByRole('link', { name: 'Atendimentos', exact: true }).click();
  await page.getByRole('combobox', { name: 'Status', exact: true }).selectOption('InService');
  await page.getByRole('link', { name: 'Maria', exact: true }).click();
  await page.getByRole('button', { name: 'Assumir atendimento' }).click();
  await expect(page.getByRole('textbox', { name: 'Resposta ao cliente' })).toBeVisible();
  expect(state.assignedToId).toBe('staff');
});
