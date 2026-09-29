import { test as base, expect, Page } from '@playwright/test';

const test = base.extend<{ browserErrors: void }>({
  browserErrors: [async ({ page }, use) => {
    const errors: string[] = [];
    page.on('pageerror', error => errors.push(error.message));
    await use();
    expect(errors).toEqual([]);
  }, { auto: true }],
});
const password = 'Senha exclusiva de teste 2026';
const roles = [
  { id: 1, code: 'Administrator', name: 'Administrador' },
  { id: 2, code: 'Attendant', name: 'Atendente' },
  { id: 3, code: 'Kitchen', name: 'Cozinha' },
  { id: 4, code: 'Dispatch', name: 'Expedição' },
];
const admin = { id: 'admin', name: 'Ana Administradora', username: 'ana.admin', roleId: 1, role: 'Administrator', isActive: true, createdAt: '2026-01-01T12:00:00Z' };
const attendant = { id: 'attendant', name: 'João Atendente', username: 'joao', roleId: 2, role: 'Attendant', isActive: true, createdAt: admin.createdAt };

async function setup(page: Page, options: { role?: number; expiresIn?: number } = {}) {
  const role = roles[(options.role ?? 1) - 1];
  const profile = { ...(role.id === 1 ? admin : attendant), role: role.code, permissions: role.id === 1 ? ['users.manage'] : [] };
  const state = {
    users: [{ ...admin }, { ...attendant }],
    loginStatus: 200, usersStatus: 200, logoutOffline: false,
    statusConflict: false, mutations: 0, passwordChanges: 0, resets: 0,
    lastInput: null as Record<string, unknown> | null,
  };
  await page.route('**/api/**', async route => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname;
    const method = request.method();
    const json = (body: unknown, status = 200) => route.fulfill({ status, json: body });
    if (path === '/api/system/status') return json({ status: 'available' });
    if (path === '/api/auth/login') {
      expect(request.headers()['authorization']).toBeUndefined();
      if (state.loginStatus !== 200) return json({}, state.loginStatus);
      return json({ accessToken: 'test-token', tokenType: 'Bearer',
        expiresAt: new Date(Date.now() + (options.expiresIn ?? 900000)).toISOString(), user: profile });
    }
    expect(request.headers()['authorization']).toBe('Bearer test-token');
    if (path === '/api/auth/me') return json(profile);
    if (path === '/api/auth/logout') return state.logoutOffline ? route.abort('connectionrefused') : route.fulfill({ status: 204 });
    if (path === '/api/auth/password') {
      state.passwordChanges++;
      state.lastInput = request.postDataJSON();
      return route.fulfill({ status: 204 });
    }
    if (path === '/api/roles') return json(roles);
    if (path === '/api/users' && method === 'GET') {
      if (state.usersStatus !== 200) return json({}, state.usersStatus);
      const search = (url.searchParams.get('search') ?? '').toLowerCase();
      const active = url.searchParams.get('isActive');
      const roleId = url.searchParams.get('roleId');
      const users = state.users.filter(user => (user.name + user.username).toLowerCase().includes(search) &&
        (!roleId || user.roleId === +roleId) && (!active || String(user.isActive) === active));
      const pageNumber = Number(url.searchParams.get('page') ?? 1);
      return json({ items: users.slice((pageNumber - 1) * 20, pageNumber * 20), page: pageNumber, pageSize: 20, totalCount: users.length });
    }
    if (path === '/api/users' && method === 'POST') {
      state.mutations++;
      const input = request.postDataJSON();
      state.lastInput = input;
      if (state.users.some(user => user.username === input.username)) return json({ code: 'DuplicateUsername' }, 409);
      const user = { ...input, id: 'new', role: roles.find(role => role.id === input.roleId)!.code, createdAt: admin.createdAt };
      delete user.password;
      state.users.push(user);
      return json(user, 201);
    }
    const match = path.match(/^\/api\/users\/([^/]+)(?:\/(status|password))?$/);
    if (match) {
      const user = state.users.find(user => user.id === match[1]);
      if (!user) return json({ code: 'UserNotFound' }, 404);
      if (method === 'GET') return json(user);
      state.mutations++;
      const input = request.postDataJSON();
      state.lastInput = input;
      if (match[2] === 'password') { state.resets++; return route.fulfill({ status: 204 }); }
      if (state.statusConflict) return json({ code: 'LastAdministrator' }, 409);
      Object.assign(user, input);
      user.role = roles.find(role => role.id === user.roleId)!.code;
      return json(user);
    }
    return json({}, 404);
  });
  return state;
}

async function login(page: Page) {
  await page.goto('/entrar');
  await page.getByLabel('Login', { exact: true }).fill('ana.admin');
  await page.getByLabel('Senha', { exact: true }).fill(password);
  await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await expect(page).toHaveURL(/\/equipe$/);
}
async function navigate(page: Page, path: string) {
  await page.evaluate(path => {
    history.pushState(null, '', path);
    dispatchEvent(new PopStateEvent('popstate'));
  }, path);
}

test('rota privada exige login; credenciais inválidas e limite têm mensagens claras', async ({ page }) => {
  const state = await setup(page);
  await page.goto('/equipe/funcionarios');
  await expect(page).toHaveURL(/\/entrar$/);
  state.loginStatus = 401;
  await page.getByLabel('Login', { exact: true }).fill('ana.admin');
  await page.getByLabel('Senha', { exact: true }).fill(password);
  await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('Login ou senha inválidos');
  await expect(page.getByLabel('Senha', { exact: true })).toHaveValue('');
  state.loginStatus = 429;
  await page.getByLabel('Senha', { exact: true }).fill(password);
  await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('Aguarde um minuto');
});

test('sessão em memória, layout responsivo e recarregamento exigem login', async ({ page }) => {
  await setup(page);
  await login(page);
  await expect(page.getByRole('heading', { level: 1 })).toContainText('Ana Administradora');
  expect(await page.evaluate(() => ({ local: localStorage.length, session: sessionStorage.length }))).toEqual({ local: 0, session: 0 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.reload();
  await expect(page).toHaveURL(/\/entrar$/);
});

for (const role of [2, 3, 4]) {
  test('perfil ' + role + ' não acessa gestão mesmo por URL interna', async ({ page }) => {
    await setup(page, { role });
    await login(page);
    await expect(page.getByRole('link', { name: 'Funcionários', exact: true })).toHaveCount(0);
    await navigate(page, '/equipe/funcionarios/novo');
    await expect(page).toHaveURL(/\/equipe$/);
    await expect(page.getByRole('heading', { level: 1 })).toContainText('João');
  });
}

test('lista, filtros, paginação e cadastro com confirmação de senha', async ({ page }) => {
  const state = await setup(page);
  for (let i = 0; i < 21; i++) state.users.push({ ...attendant, id: 'extra' + i, name: 'Equipe ' + i, username: 'equipe' + i });
  await login(page);
  await page.getByRole('link', { name: 'Funcionários', exact: true }).click();
  await expect(page.getByRole('status')).toContainText('23 funcionário(s)');
  await page.getByRole('button', { name: 'Próxima' }).click();
  await expect(page.getByText('Página 2', { exact: true })).toBeVisible();
  await page.getByLabel('Buscar nome ou login').fill('João');
  await page.getByRole('combobox', { name: 'Perfil', exact: true }).selectOption('2');
  await page.getByRole('combobox', { name: 'Status', exact: true }).selectOption('true');
  await page.getByRole('button', { name: 'Buscar', exact: true }).click();
  await expect(page.getByRole('status')).toContainText('1 funcionário(s)');
  await page.getByRole('link', { name: 'Novo funcionário' }).click();
  await page.getByLabel('Nome', { exact: true }).fill('Maria Cozinha');
  await page.getByLabel('Login', { exact: true }).fill('maria.cozinha');
  await page.getByRole('combobox', { name: 'Perfil', exact: true }).selectOption({ label: 'Cozinha' });
  await page.getByLabel('Senha inicial').fill(password);
  await page.getByLabel('Confirme a senha', { exact: true }).fill('diferente');
  await expect(page.getByRole('button', { name: 'Salvar funcionário' })).toBeDisabled();
  await page.getByLabel('Confirme a senha', { exact: true }).fill(password);
  await page.getByRole('button', { name: 'Salvar funcionário' }).click();
  await expect(page.getByRole('status')).toContainText('Funcionário criado');
  expect(state.lastInput).toMatchObject({ username: 'maria.cozinha', roleId: 3, isActive: true });
  expect(state.mutations).toBe(1);
  await expect(page.getByLabel('Senha inicial')).toHaveCount(0);
});

test('login duplicado é explicado sem perder o cadastro', async ({ page }) => {
  await setup(page);
  await login(page);
  await navigate(page, '/equipe/funcionarios/novo');
  await page.getByLabel('Nome', { exact: true }).fill('Outra Ana');
  await page.getByLabel('Login', { exact: true }).fill('ana.admin');
  await page.getByRole('combobox', { name: 'Perfil', exact: true }).selectOption({ label: 'Atendente' });
  await page.getByLabel('Senha inicial').fill(password);
  await page.getByLabel('Confirme a senha', { exact: true }).fill(password);
  await page.getByRole('button', { name: 'Salvar funcionário' }).click();
  await expect(page.getByRole('alert')).toContainText('login já está em uso');
  await expect(page.getByLabel('Nome', { exact: true })).toHaveValue('Outra Ana');
  await expect(page.getByLabel('Senha inicial')).toHaveValue('');
});

test('editar, inativar com confirmação, reativar e redefinir senha', async ({ page }) => {
  const state = await setup(page);
  await login(page);
  await page.getByRole('link', { name: 'Funcionários', exact: true }).click();
  await page.getByRole('button', { name: 'Inativar João Atendente' }).click();
  expect(state.mutations).toBe(0);
  await page.getByRole('button', { name: 'Cancelar', exact: true }).click();
  expect(state.mutations).toBe(0);
  await page.getByRole('button', { name: 'Inativar João Atendente' }).click();
  await page.getByRole('button', { name: 'Confirmar', exact: true }).click();
  await expect(page.getByText('Funcionário inativado.', { exact: false })).toBeVisible();
  await page.getByRole('button', { name: 'Ativar João Atendente', exact: true }).click();
  await page.getByRole('button', { name: 'Confirmar', exact: true }).click();
  await expect(page.getByText('Funcionário ativado.', { exact: true })).toBeVisible();
  await page.getByRole('link', { name: 'Editar João Atendente' }).click();
  await page.getByLabel('Nome', { exact: true }).fill('João Silva');
  await page.getByRole('button', { name: 'Salvar funcionário' }).click();
  await expect(page.getByRole('status')).toContainText('Cadastro atualizado');
  await page.getByRole('button', { name: 'Redefinir senha', exact: true }).click();
  await page.getByLabel('Nova senha', { exact: true }).fill(password);
  await page.getByLabel('Confirme a nova senha', { exact: true }).fill(password);
  await page.getByRole('button', { name: 'Confirmar nova senha' }).click();
  await expect(page.getByRole('status')).toContainText('Senha redefinida');
  expect(state.resets).toBe(1);
});

test('proteção do último administrador e indisponibilidade com nova tentativa', async ({ page }) => {
  const state = await setup(page);
  state.usersStatus = 503;
  await login(page);
  await page.getByRole('link', { name: 'Funcionários', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('servidor não conseguiu');
  state.usersStatus = 200;
  await page.getByRole('button', { name: 'Tentar novamente' }).click();
  state.statusConflict = true;
  await page.getByRole('button', { name: 'Inativar Ana Administradora' }).click();
  await page.getByRole('button', { name: 'Confirmar', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('pelo menos um administrador ativo');
  expect(state.users[0].isActive).toBe(true);
});

test('troca da própria senha encerra sessão', async ({ page }) => {
  const state = await setup(page);
  await login(page);
  await page.getByRole('link', { name: 'Minha senha', exact: true }).click();
  await page.getByLabel('Senha atual').fill(password);
  await page.getByLabel('Nova senha', { exact: true }).fill(password + ' nova');
  await page.getByLabel('Confirme a nova senha', { exact: true }).fill(password + ' nova');
  await page.getByRole('button', { name: 'Alterar senha' }).click();
  await expect(page).toHaveURL(/\/entrar$/);
  await expect(page.getByRole('status')).toContainText('Senha alterada');
  expect(state.passwordChanges).toBe(1);
});

test('alterar o próprio login encerra sessão imediatamente', async ({ page }) => {
  await setup(page);
  await login(page);
  await navigate(page, '/equipe/funcionarios/admin');
  await expect(page.getByRole('button', { name: 'Redefinir senha', exact: true })).toHaveCount(0);
  await page.getByLabel('Login', { exact: true }).fill('ana.novo');
  await page.getByRole('button', { name: 'Salvar funcionário' }).click();
  await expect(page).toHaveURL(/\/entrar$/);
  await expect(page.getByRole('status')).toContainText('Seu acesso foi alterado');
});

test('403 mantém sessão e 401 revoga acesso e remove dados', async ({ page }) => {
  const state = await setup(page);
  await login(page);
  state.usersStatus = 403;
  await page.getByRole('link', { name: 'Funcionários', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('não tem permissão');
  await expect(page).toHaveURL(/\/equipe\/funcionarios$/);
  state.usersStatus = 401;
  await page.getByRole('button', { name: 'Tentar novamente' }).click();
  await expect(page).toHaveURL(/\/entrar$/);
  await expect(page.getByRole('status')).toContainText('expirou ou foi revogada');
  await page.goBack();
  await expect(page).not.toHaveURL(/\/equipe/);
  await expect(page.getByText('Ana Administradora', { exact: true })).toHaveCount(0);
});

test('expiração automática remove a tela protegida', async ({ page }) => {
  await setup(page, { expiresIn: 1200 });
  await login(page);
  await expect(page).toHaveURL(/\/entrar$/, { timeout: 7000 });
  await expect(page.getByRole('status')).toContainText('sessão expirou');
});

test('sair offline informa limite da revogação e impede retorno aos dados', async ({ page }) => {
  const state = await setup(page);
  await login(page);
  state.logoutOffline = true;
  await page.getByRole('button', { name: 'Sair', exact: true }).click();
  await expect(page).toHaveURL(/\/entrar$/);
  await expect(page.getByRole('status')).toContainText('Não foi possível confirmar');
  await navigate(page, '/equipe');
  await expect(page).toHaveURL(/\/entrar$/);
});

