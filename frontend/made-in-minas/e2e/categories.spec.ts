import { test, expect, Page } from '@playwright/test';

const category = {
  id: 'category-1',
  name: 'Hambúrgueres',
  description: 'Feitos na hora',
  displayOrder: 0,
  isActive: true,
  createdAt: '2026-09-29T12:00:00Z',
  updatedAt: '2026-09-29T12:00:00Z',
};

async function setup(page: Page, role = 'Administrator') {
  const profile = {
    id: 'staff',
    name: 'Equipe Teste',
    username: 'equipe.teste',
    role,
    permissions: role === 'Administrator' ? ['catalog.manage', 'users.manage'] : [],
  };
  const state = { items: [{ ...category }], listStatus: 200, saveStatus: 200, writes: 0 };
  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname;
    const json = (body: unknown, status = 200) => route.fulfill({ status, json: body });
    if (path === '/api/auth/login') {
      return json({
        accessToken: 'category-test-token',
        tokenType: 'Bearer',
        expiresAt: new Date(Date.now() + 900000).toISOString(),
        user: profile,
      });
    }
    expect(request.headers()['authorization']).toBe('Bearer category-test-token');
    if (path === '/api/auth/me') {
      return json(profile);
    }
    if (path === '/api/categories' && request.method() === 'GET') {
      if (state.listStatus !== 200) {
        return json({}, state.listStatus);
      }
      const search = (url.searchParams.get('search') ?? '').toLowerCase();
      const active = url.searchParams.get('isActive');
      const items = state.items
        .filter(
          (item) =>
            item.name.toLowerCase().includes(search) &&
            (!active || String(item.isActive) === active),
        )
        .sort((a, b) => a.displayOrder - b.displayOrder || a.name.localeCompare(b.name));
      const current = Number(url.searchParams.get('page') ?? '1');
      return json({
        items: items.slice((current - 1) * 20, current * 20),
        page: current,
        pageSize: 20,
        totalCount: items.length,
      });
    }
    if (path === '/api/categories' && request.method() === 'POST') {
      state.writes++;
      if (state.saveStatus !== 200) {
        return json({ code: 'DuplicateCategoryName' }, state.saveStatus);
      }
      const created = { ...category, ...request.postDataJSON(), id: 'new-category' };
      state.items.push(created);
      return json(created, 201);
    }
    const match = path.match(/^\/api\/categories\/([^/]+)(\/status)?$/);
    if (match) {
      const item = state.items.find((item) => item.id === match[1]);
      if (!item) {
        return json({ code: 'CategoryNotFound' }, 404);
      }
      if (request.method() === 'GET') {
        return json(item);
      }
      state.writes++;
      if (state.saveStatus !== 200) {
        return json({ code: 'DuplicateCategoryName' }, state.saveStatus);
      }
      Object.assign(item, request.postDataJSON());
      return json(item);
    }
    return json({}, 404);
  });
  return state;
}

async function login(page: Page) {
  await page.goto('/entrar?returnUrl=%2Fequipe');
  await page.getByLabel('Login', { exact: true }).fill('equipe.teste');
  await page.getByLabel('Senha', { exact: true }).fill('Senha apenas para os testes');
  await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await expect(page).toHaveURL(/\/equipe$/);
}
async function navigate(page: Page, path: string) {
  await page.evaluate((path) => {
    history.pushState(null, '', path);
    dispatchEvent(new PopStateEvent('popstate'));
  }, path);
}

test('categorias: cadastro, edição e persistência ao reabrir a tela', async ({ page }) => {
  const errors: string[] = [];
  page.on('pageerror', (error) => errors.push(error.message));
  const state = await setup(page);
  await login(page);
  await page.getByRole('link', { name: 'Categorias', exact: true }).click();
  await page.getByRole('link', { name: 'Nova categoria', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Salvar categoria' })).toBeDisabled();
  await page.getByLabel('Nome', { exact: true }).fill('  ');
  await expect(page.getByRole('alert')).toContainText('Informe o nome');
  await page.getByLabel('Nome', { exact: true }).fill('Bebidas');
  await page.getByLabel('Ordem de exibição').fill('1.5');
  await expect(page.getByRole('alert')).toContainText('ordem inteira');
  await expect(page.getByRole('button', { name: 'Salvar categoria' })).toBeDisabled();
  await page.getByLabel('Ordem de exibição').fill('2');
  await page.getByLabel('Descrição (opcional)').fill('Sucos e refrigerantes');
  await page.getByRole('button', { name: 'Salvar categoria' }).click();
  await expect(page).toHaveURL(/\/equipe\/categorias\/new-category$/);
  await expect(page.getByRole('status').filter({ hasText: 'Categoria criada' })).toBeVisible();
  expect(state.writes).toBe(1);
  await page.getByLabel('Nome', { exact: true }).fill('Bebidas geladas');
  await page.getByRole('button', { name: 'Salvar categoria' }).click();
  await expect(page.getByRole('status')).toContainText('Categoria atualizada');
  await page.getByRole('link', { name: 'Categorias', exact: true }).click();
  await expect(page.getByRole('link', { name: 'Editar Bebidas geladas' })).toBeVisible();
  await page.getByRole('link', { name: 'Editar Bebidas geladas' }).click();
  await expect(page.getByLabel('Ordem de exibição')).toHaveValue('2');
  await expect(page.getByLabel('Descrição (opcional)')).toHaveValue('Sucos e refrigerantes');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  expect(errors).toEqual([]);
});

test('categorias: status com confirmação, filtros, paginação e estado vazio', async ({ page }) => {
  const state = await setup(page);
  for (let i = 0; i < 21; i++) {
    state.items.push({
      ...category,
      id: 'extra-' + i,
      name: 'Categoria ' + i,
      displayOrder: i + 1,
    });
  }
  await login(page);
  await page.getByRole('link', { name: 'Categorias', exact: true }).click();
  await expect(page.getByRole('status')).toContainText('22 categoria(s)');
  await page.getByRole('button', { name: 'Próxima' }).click();
  await expect(page.getByText('Página 2', { exact: true })).toBeVisible();
  await page.getByLabel('Buscar categoria').fill('Hambúrgueres');
  await page.getByRole('button', { name: 'Buscar', exact: true }).click();
  await expect(page.getByRole('status')).toContainText('1 categoria(s)');
  await page.getByRole('button', { name: 'Inativar Hambúrgueres', exact: true }).click();
  await page.getByRole('button', { name: 'Cancelar', exact: true }).click();
  expect(state.writes).toBe(0);
  await page.getByRole('button', { name: 'Inativar Hambúrgueres', exact: true }).click();
  await page.getByRole('button', { name: 'Confirmar', exact: true }).click();
  await expect(page.getByText('Categoria inativada.', { exact: true })).toBeVisible();
  await page.getByRole('combobox', { name: 'Status', exact: true }).selectOption('false');
  await page.getByRole('button', { name: 'Buscar', exact: true }).click();
  await page.getByRole('button', { name: 'Ativar Hambúrgueres', exact: true }).click();
  await page.getByRole('button', { name: 'Confirmar', exact: true }).click();
  await expect(page.getByText('Categoria ativada.', { exact: true })).toBeVisible();
  await expect(page.getByText('Nenhuma categoria nesta página.', { exact: false })).toBeVisible();
  expect(state.writes).toBe(2);
});

test('categorias: nome duplicado mantém campos e permite correção', async ({ page }) => {
  const state = await setup(page);
  await login(page);
  await navigate(page, '/equipe/categorias/nova');
  await page.getByLabel('Nome', { exact: true }).fill('Hambúrgueres');
  state.saveStatus = 409;
  await page.getByRole('button', { name: 'Salvar categoria' }).click();
  await expect(page.getByRole('alert')).toContainText('Verifique também as categorias inativas');
  await expect(page.getByLabel('Nome', { exact: true })).toHaveValue('Hambúrgueres');
  state.saveStatus = 200;
  await page.getByLabel('Nome', { exact: true }).fill('Porções');
  await page.getByRole('button', { name: 'Salvar categoria' }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Categoria criada' })).toBeVisible();
});

test('categorias: falha da API, nova tentativa e categoria inexistente', async ({ page }) => {
  const state = await setup(page);
  state.listStatus = 503;
  await login(page);
  await page.getByRole('link', { name: 'Categorias', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('servidor não conseguiu');
  state.listStatus = 200;
  await page.getByRole('button', { name: 'Tentar novamente' }).click();
  await expect(page.getByRole('link', { name: 'Editar Hambúrgueres' })).toBeVisible();
  await navigate(page, '/equipe/categorias/inexistente');
  await expect(page.getByRole('alert')).toContainText('Categoria não encontrada');
  await expect(page.getByRole('button', { name: 'Salvar categoria' })).toHaveCount(0);
});

for (const role of ['Attendant', 'Kitchen', 'Dispatch']) {
  test('categorias: perfil ' + role + ' não acessa tela administrativa', async ({ page }) => {
    await setup(page, role);
    await login(page);
    await expect(page.getByRole('link', { name: 'Categorias', exact: true })).toHaveCount(0);
    await navigate(page, '/equipe/categorias/nova');
    await expect(page).toHaveURL(/\/equipe$/);
  });
}

test('categorias: acesso direto sem sessão exige login', async ({ page }) => {
  await setup(page);
  await page.goto('/equipe/categorias');
  await expect(page).toHaveURL(/\/entrar(?:\?.*)?$/);
});
