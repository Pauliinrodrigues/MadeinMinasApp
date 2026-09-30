import { test, expect, Page } from '@playwright/test';

const ingredient = {
  id: 'ingredient-1',
  name: 'Carne',
  unit: 'kg',
  unitCost: 32.4567,
  minimumStock: 1.125,
  supplier: 'Fornecedor local',
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
  const state = { items: [{ ...ingredient }], listStatus: 200, saveStatus: 200, writes: 0 };
  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname;
    const json = (body: unknown, status = 200) => route.fulfill({ status, json: body });
    if (path === '/api/auth/login') {
      return json({
        accessToken: 'ingredient-test-token',
        tokenType: 'Bearer',
        expiresAt: new Date(Date.now() + 900000).toISOString(),
        user: profile,
      });
    }
    expect(request.headers()['authorization']).toBe('Bearer ingredient-test-token');
    if (path === '/api/auth/me') {
      return json(profile);
    }
    if (path === '/api/ingredients' && request.method() === 'GET') {
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
        .sort((a, b) => a.name.localeCompare(b.name));
      const current = Number(url.searchParams.get('page') ?? '1');
      return json({
        items: items.slice((current - 1) * 20, current * 20),
        page: current,
        pageSize: 20,
        totalCount: items.length,
      });
    }
    if (path === '/api/ingredients' && request.method() === 'POST') {
      state.writes++;
      if (state.saveStatus !== 200) {
        return json({ code: 'DuplicateIngredientName' }, state.saveStatus);
      }
      const created = { ...ingredient, ...request.postDataJSON(), id: 'new-ingredient' };
      state.items.push(created);
      return json(created, 201);
    }
    const match = path.match(/^\/api\/ingredients\/([^/]+)(\/status)?$/);
    if (match) {
      const item = state.items.find((item) => item.id === match[1]);
      if (!item) {
        return json({ code: 'IngredientNotFound' }, 404);
      }
      if (request.method() === 'GET') {
        return json(item);
      }
      state.writes++;
      if (state.saveStatus !== 200) {
        return json({ code: 'DuplicateIngredientName' }, state.saveStatus);
      }
      Object.assign(item, request.postDataJSON());
      return json(item);
    }
    return json({}, 404);
  });
  return state;
}

async function login(page: Page) {
  await page.goto('/entrar');
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

test('ingredientes: cadastro, edição e persistência ao reabrir a tela', async ({ page }) => {
  const errors: string[] = [];
  page.on('pageerror', (error) => errors.push(error.message));
  const state = await setup(page);
  await login(page);
  await page.getByRole('link', { name: 'Ingredientes', exact: true }).click();
  await page.getByRole('link', { name: 'Novo ingrediente', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Salvar ingrediente' })).toBeDisabled();
  await page.getByLabel('Nome', { exact: true }).fill('  ');
  await expect(page.getByRole('alert')).toContainText('Informe o nome');
  await page.getByLabel('Nome', { exact: true }).fill('Bebidas');
  await page.getByLabel('Custo por kg (R$)').fill('1,12345');
  await expect(page.getByRole('alert')).toContainText('quatro casas decimais');
  await expect(page.getByRole('button', { name: 'Salvar ingrediente' })).toBeDisabled();
  await page.getByLabel('Custo por kg (R$)').fill('32,4567');
  await page.getByLabel('Estoque mínimo (kg)').fill('1,125');
  await page.getByLabel('Fornecedor (opcional)').fill('Sucos e refrigerantes');
  await page.getByRole('button', { name: 'Salvar ingrediente' }).click();
  await expect(page).toHaveURL(/\/equipe\/ingredientes\/new-ingredient$/);
  await expect(page.getByRole('status').filter({ hasText: 'Ingrediente criado' })).toBeVisible();
  expect(state.writes).toBe(1);
  await page.getByLabel('Nome', { exact: true }).fill('Bebidas geladas');
  await page.getByRole('button', { name: 'Salvar ingrediente' }).click();
  await expect(page.getByRole('status')).toContainText('Ingrediente atualizado');
  await page.getByRole('link', { name: 'Ingredientes', exact: true }).click();
  await expect(page.getByRole('link', { name: 'Editar Bebidas geladas' })).toBeVisible();
  await page.getByRole('link', { name: 'Editar Bebidas geladas' }).click();
  await expect(page.getByLabel('Custo por kg (R$)')).toHaveValue('32,4567');
  await expect(page.getByLabel('Estoque mínimo (kg)')).toHaveValue('1,125');
  await expect(page.getByLabel('Unidade-base')).toBeDisabled();
  await expect(page.getByLabel('Fornecedor (opcional)')).toHaveValue('Sucos e refrigerantes');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  expect(errors).toEqual([]);
});

test('ingredientes: status com confirmação, filtros, paginação e estado vazio', async ({
  page,
}) => {
  const state = await setup(page);
  for (let i = 0; i < 21; i++) {
    state.items.push({ ...ingredient, id: 'extra-' + i, name: 'Ingrediente ' + i });
  }
  await login(page);
  await page.getByRole('link', { name: 'Ingredientes', exact: true }).click();
  await expect(page.getByRole('status')).toContainText('22 ingrediente(s)');
  await page.getByRole('button', { name: 'Próxima' }).click();
  await expect(page.getByText('Página 2', { exact: true })).toBeVisible();
  await page.getByLabel('Buscar ingrediente').fill('Carne');
  await page.getByRole('button', { name: 'Buscar', exact: true }).click();
  await expect(page.getByRole('status')).toContainText('1 ingrediente(s)');
  await page.getByRole('button', { name: 'Inativar Carne', exact: true }).click();
  await page.getByRole('button', { name: 'Cancelar', exact: true }).click();
  expect(state.writes).toBe(0);
  await page.getByRole('button', { name: 'Inativar Carne', exact: true }).click();
  await page.getByRole('button', { name: 'Confirmar', exact: true }).click();
  await expect(page.getByText('Ingrediente inativado.', { exact: true })).toBeVisible();
  await page.getByRole('combobox', { name: 'Status', exact: true }).selectOption('false');
  await page.getByRole('button', { name: 'Buscar', exact: true }).click();
  await page.getByRole('button', { name: 'Ativar Carne', exact: true }).click();
  await page.getByRole('button', { name: 'Confirmar', exact: true }).click();
  await expect(page.getByText('Ingrediente ativado.', { exact: true })).toBeVisible();
  await expect(page.getByText('Nenhum ingrediente nesta página.', { exact: false })).toBeVisible();
  expect(state.writes).toBe(2);
});

test('ingredientes: nome duplicado mantém campos e permite correção', async ({ page }) => {
  const state = await setup(page);
  await login(page);
  await navigate(page, '/equipe/ingredientes/novo');
  await page.getByLabel('Nome', { exact: true }).fill('Carne');
  await page.getByLabel('Custo por kg (R$)').fill('12.5');
  state.saveStatus = 409;
  await page.getByRole('button', { name: 'Salvar ingrediente' }).click();
  await expect(page.getByRole('alert')).toContainText('Verifique também os ingredientes inativos');
  await expect(page.getByLabel('Nome', { exact: true })).toHaveValue('Carne');
  state.saveStatus = 200;
  await page.getByLabel('Nome', { exact: true }).fill('Porções');
  await page.getByRole('button', { name: 'Salvar ingrediente' }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Ingrediente criado' })).toBeVisible();
});

test('ingredientes: falha da API, nova tentativa e ingrediente inexistente', async ({ page }) => {
  const state = await setup(page);
  state.listStatus = 503;
  await login(page);
  await page.getByRole('link', { name: 'Ingredientes', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('servidor não conseguiu');
  state.listStatus = 200;
  await page.getByRole('button', { name: 'Tentar novamente' }).click();
  await expect(page.getByRole('link', { name: 'Editar Carne' })).toBeVisible();
  await navigate(page, '/equipe/ingredientes/inexistente');
  await expect(page.getByRole('alert')).toContainText('Ingrediente não encontrado');
  await expect(page.getByRole('button', { name: 'Salvar ingrediente' })).toHaveCount(0);
});

for (const role of ['Attendant', 'Kitchen', 'Dispatch']) {
  test('ingredientes: perfil ' + role + ' não acessa tela administrativa', async ({ page }) => {
    await setup(page, role);
    await login(page);
    await expect(page.getByRole('link', { name: 'Ingredientes', exact: true })).toHaveCount(0);
    await navigate(page, '/equipe/ingredientes/novo');
    await expect(page).toHaveURL(/\/equipe$/);
  });
}

test('ingredientes: acesso direto sem sessão exige login', async ({ page }) => {
  await setup(page);
  await page.goto('/equipe/ingredientes');
  await expect(page).toHaveURL(/\/entrar$/);
});

test('ingredientes: unidades, limites decimais e valores enviados para a API', async ({ page }) => {
  const state = await setup(page);
  await login(page);
  await navigate(page, '/equipe/ingredientes/novo');
  await page.getByLabel('Nome', { exact: true }).fill('Molho');
  await page.getByLabel('Unidade-base').selectOption('l');
  const cost = page.getByLabel('Custo por L (R$)');
  const minimum = page.getByLabel('Estoque mínimo (L)');
  const save = page.getByRole('button', { name: 'Salvar ingrediente' });
  for (const invalid of ['-1', '1000000', '1.234,56', '1e2', '1,00001', '']) {
    await cost.fill(invalid);
    await expect(save).toBeDisabled();
  }
  await cost.fill('0,0001');
  await minimum.fill('0,0001');
  await expect(save).toBeDisabled();
  await minimum.fill('0.001');
  await save.click();
  await expect(page.getByRole('status').filter({ hasText: 'Ingrediente criado' })).toBeVisible();
  expect(state.items.find((item) => item.id === 'new-ingredient')).toMatchObject({
    unit: 'l',
    unitCost: 0.0001,
    minimumStock: 0.001,
    supplier: null,
  });
  await expect(page.getByLabel('Unidade-base')).toBeDisabled();
  await page.getByRole('link', { name: 'Ingredientes', exact: true }).click();
  await page.getByRole('link', { name: 'Novo ingrediente' }).click();
  await page.getByLabel('Nome', { exact: true }).fill('Embalagem');
  await page.getByLabel('Unidade-base').selectOption('un');
  await page.getByLabel('Custo por un (R$)').fill('0');
  await expect(save).toBeEnabled();
  await expect(page.getByText('O custo informado é zero.', { exact: false })).toBeVisible();
});

test('ingredientes: sessão expirada durante consulta volta ao login', async ({ page }) => {
  const state = await setup(page);
  await login(page);
  state.listStatus = 401;
  await page.getByRole('link', { name: 'Ingredientes', exact: true }).click();
  await expect(page).toHaveURL(/\/entrar$/);
  expect(state.writes).toBe(0);
});
