import { test as base, expect, Page } from '@playwright/test';

const test = base.extend<{ browserErrors: void }>({
  browserErrors: [
    async ({ page }, use) => {
      const errors: string[] = [];
      page.on('pageerror', (error) => errors.push(error.message));
      await use();
      expect(errors).toEqual([]);
    },
    { auto: true },
  ],
});
const initialCategory = {
  id: 'category-1',
  name: 'Lanches',
  description: null,
  displayOrder: 0,
  isActive: true,
  createdAt: '2026-09-29T12:00:00Z',
  updatedAt: '2026-09-29T12:00:00Z',
};
const initialProduct = {
  id: 'product-1',
  name: 'Uai Sô',
  description: 'Blend e queijo',
  categoryId: initialCategory.id,
  price: 29.9,
  imageUrl: null as string | null,
  isActive: true,
  isAvailable: true,
  createdAt: initialCategory.createdAt,
  updatedAt: initialCategory.updatedAt,
};

async function setup(page: Page, role = 'Administrator') {
  const profile = {
    id: 'staff',
    name: 'Equipe Teste',
    username: 'equipe.teste',
    role,
    permissions: role === 'Administrator' ? ['catalog.manage'] : [],
  };
  const state = {
    categories: [{ ...initialCategory }],
    items: [{ ...initialProduct }],
    listStatus: 200,
    saveCode: '',
    writes: 0,
    lastInput: null as Record<string, unknown> | null,
  };
  const responseProduct = (product: typeof initialProduct) => {
    const category = state.categories.find((category) => category.id === product.categoryId)!;
    return {
      ...product,
      categoryName: category.name,
      categoryIsActive: category.isActive,
      isAvailableForSale: product.isActive && product.isAvailable && category.isActive,
    };
  };
  await page.route('https://images.example.test/**', (route) => route.abort('failed'));
  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname;
    const json = (body: unknown, status = 200) => route.fulfill({ status, json: body });
    if (path === '/api/auth/login') {
      return json({
        accessToken: 'product-test-token',
        tokenType: 'Bearer',
        expiresAt: new Date(Date.now() + 900000).toISOString(),
        user: profile,
      });
    }
    expect(request.headers()['authorization']).toBe('Bearer product-test-token');
    if (path === '/api/auth/me') {
      return json(profile);
    }
    if (path === '/api/categories') {
      const current = Number(url.searchParams.get('page') ?? '1');
      return json({
        items: state.categories.slice((current - 1) * 20, current * 20),
        page: current,
        pageSize: 20,
        totalCount: state.categories.length,
      });
    }
    if (path === '/api/products' && request.method() === 'GET') {
      if (state.listStatus !== 200) {
        return json({}, state.listStatus);
      }
      const search = (url.searchParams.get('search') ?? '').toLowerCase();
      const categoryId = url.searchParams.get('categoryId');
      const active = url.searchParams.get('isActive');
      const available = url.searchParams.get('isAvailableForSale');
      const items = state.items
        .map(responseProduct)
        .filter(
          (product) =>
            product.name.toLowerCase().includes(search) &&
            (!categoryId || product.categoryId === categoryId) &&
            (!active || String(product.isActive) === active) &&
            (!available || String(product.isAvailableForSale) === available),
        );
      const current = Number(url.searchParams.get('page') ?? '1');
      return json({
        items: items.slice((current - 1) * 20, current * 20),
        page: current,
        pageSize: 20,
        totalCount: items.length,
      });
    }
    if (path === '/api/products' && request.method() === 'POST') {
      state.writes++;
      state.lastInput = request.postDataJSON();
      if (state.saveCode) {
        return json(
          { code: state.saveCode },
          state.saveCode === 'DuplicateProductName' ? 409 : 400,
        );
      }
      const product = { ...initialProduct, ...state.lastInput, id: 'new-product' };
      state.items.push(product);
      return json(responseProduct(product), 201);
    }
    const match = path.match(/^\/api\/products\/([^/]+)(?:\/(status|availability))?$/);
    if (match) {
      const product = state.items.find((product) => product.id === match[1]);
      if (!product) {
        return json({ code: 'ProductNotFound' }, 404);
      }
      if (request.method() === 'GET') {
        return json(responseProduct(product));
      }
      state.writes++;
      state.lastInput = request.postDataJSON();
      if (state.saveCode) {
        return json({ code: state.saveCode }, 409);
      }
      Object.assign(product, state.lastInput);
      return json(responseProduct(product));
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

test('produtos: cadastro em categoria além da primeira página, preço e edição', async ({
  page,
}) => {
  const state = await setup(page);
  for (let i = 1; i <= 21; i++) {
    state.categories.push({
      ...initialCategory,
      id: 'category-' + (i + 1),
      name: 'Categoria ' + i,
    });
  }
  await login(page);
  await page.getByRole('link', { name: 'Produtos', exact: true }).click();
  await expect(page.getByText(/R\$\s*29,90/)).toBeVisible();
  await page.getByRole('link', { name: 'Novo produto', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Salvar produto' })).toBeDisabled();
  await page.getByLabel('Nome', { exact: true }).fill('Especial da Casa');
  await page.getByRole('combobox', { name: 'Categoria', exact: true }).selectOption('category-22');
  await page.getByLabel('Preço (R$)', { exact: true }).fill('29,90');
  await page.getByLabel('Descrição (opcional)').fill('Blend, queijo e molho');
  await page.getByRole('button', { name: 'Salvar produto' }).click();
  await expect(page).toHaveURL(/\/produtos\/new-product$/);
  await expect(page.getByRole('status').filter({ hasText: 'Produto criado.' })).toBeVisible();
  expect(state.lastInput).toMatchObject({
    price: 29.9,
    categoryId: 'category-22',
    isActive: true,
    isAvailable: true,
  });
  expect(state.writes).toBe(1);
  await page.getByLabel('Preço (R$)', { exact: true }).fill('31.50');
  await page.getByRole('button', { name: 'Salvar produto' }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Produto atualizado.' })).toBeVisible();
  await page.getByRole('link', { name: 'Produtos', exact: true }).click();
  await page.getByRole('link', { name: 'Editar Especial da Casa' }).click();
  await expect(page.getByLabel('Preço (R$)', { exact: true })).toHaveValue('31,50');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('produtos: preço inválido e imagem insegura têm explicação; imagem quebrada não impede salvar', async ({
  page,
}) => {
  const state = await setup(page);
  await login(page);
  await navigate(page, '/equipe/produtos/novo');
  await page.getByLabel('Nome', { exact: true }).fill('Produto teste');
  await page
    .getByRole('combobox', { name: 'Categoria', exact: true })
    .selectOption(initialCategory.id);
  for (const price of ['0', '-1', '29,999', '1000000', '1.000,00']) {
    await page.getByLabel('Preço (R$)', { exact: true }).fill(price);
    await expect(page.getByRole('alert')).toContainText('preço positivo');
    await expect(page.getByRole('button', { name: 'Salvar produto' })).toBeDisabled();
  }
  await page.getByLabel('Preço (R$)', { exact: true }).fill('20,00');
  await page.getByLabel('Link da imagem (opcional)').fill('http://images.example.test/photo.png');
  await expect(page.getByRole('alert')).toContainText('HTTPS');
  await expect(page.getByRole('button', { name: 'Salvar produto' })).toBeDisabled();
  await page.getByLabel('Link da imagem (opcional)').fill('https://images.example.test/photo.png');
  await expect(
    page.getByText('Não foi possível carregar a imagem.', { exact: false }),
  ).toBeVisible();
  await page.getByRole('button', { name: 'Salvar produto' }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Produto criado.' })).toBeVisible();
  expect(state.lastInput?.['imageUrl']).toBe('https://images.example.test/photo.png');
});

test('produtos: pausa, ativação e confirmação preservam disponibilidade manual', async ({
  page,
}) => {
  const state = await setup(page);
  await login(page);
  await page.getByRole('link', { name: 'Produtos', exact: true }).click();
  await page.getByRole('button', { name: 'Pausar venda de Uai Sô', exact: true }).click();
  await page.getByRole('button', { name: 'Cancelar', exact: true }).click();
  expect(state.writes).toBe(0);
  await page.getByRole('button', { name: 'Pausar venda de Uai Sô', exact: true }).click();
  await page.getByRole('button', { name: 'Confirmar', exact: true }).click();
  await expect(page.getByText('Venda pausada', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Inativar Uai Sô', exact: true }).click();
  await page.getByRole('button', { name: 'Confirmar', exact: true }).click();
  await expect(page.getByText('Produto inativo', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Ativar Uai Sô', exact: true }).click();
  await page.getByRole('button', { name: 'Confirmar', exact: true }).click();
  await expect(page.getByText('Venda pausada', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Liberar venda de Uai Sô', exact: true }).click();
  await page.getByRole('button', { name: 'Confirmar', exact: true }).click();
  await expect(page.getByText('Venda liberada', { exact: true })).toBeVisible();
  expect(state.writes).toBe(4);
});

test('produtos: filtros, paginação, estado vazio e recuperação de falha', async ({ page }) => {
  const state = await setup(page);
  for (let i = 1; i <= 21; i++) {
    state.items.push({ ...initialProduct, id: 'extra-' + i, name: 'Produto ' + i });
  }
  state.listStatus = 503;
  await login(page);
  await page.getByRole('link', { name: 'Produtos', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('servidor não conseguiu');
  state.listStatus = 200;
  await page.getByRole('button', { name: 'Tentar novamente' }).click();
  await expect(page.getByRole('status')).toContainText('22 produto(s)');
  await page.getByRole('button', { name: 'Próxima' }).click();
  await expect(page.getByText('Página 2', { exact: true })).toBeVisible();
  await page.getByLabel('Buscar produto').fill('Uai');
  await page
    .getByRole('combobox', { name: 'Categoria', exact: true })
    .selectOption(initialCategory.id);
  await page.getByRole('combobox', { name: 'Status', exact: true }).selectOption('true');
  await page.getByRole('combobox', { name: 'Venda', exact: true }).selectOption('true');
  await page.getByRole('button', { name: 'Buscar', exact: true }).click();
  await expect(page.getByRole('status')).toContainText('1 produto(s)');
  await page.getByRole('combobox', { name: 'Venda', exact: true }).selectOption('false');
  await page.getByRole('button', { name: 'Buscar', exact: true }).click();
  await expect(page.getByText('Nenhum produto nesta página.', { exact: false })).toBeVisible();
});

test('produtos: categoria inativa pode ser mantida, e criação exige categoria ativa', async ({
  page,
}) => {
  const state = await setup(page);
  state.categories[0].isActive = false;
  await login(page);
  await page.getByRole('link', { name: 'Produtos', exact: true }).click();
  await expect(page.getByText('Categoria inativa', { exact: true })).toBeVisible();
  await page.getByRole('link', { name: 'Editar Uai Sô' }).click();
  await expect(page.getByText('Esta categoria está inativa.', { exact: false })).toBeVisible();
  await page.getByLabel('Nome', { exact: true }).fill('Uai Especial');
  await page.getByRole('button', { name: 'Salvar produto' }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Produto atualizado.' })).toBeVisible();
  await page.getByRole('link', { name: 'Produtos', exact: true }).click();
  await page.getByRole('link', { name: 'Novo produto', exact: true }).click();
  await expect(page.getByText('Não há categorias ativas', { exact: false })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Salvar produto' })).toBeDisabled();
  await expect(page.getByRole('link', { name: 'Cadastre ou ative uma categoria' })).toBeVisible();
});

test('produtos: conflito de nome e categoria inativada durante cadastro mantêm os dados', async ({
  page,
}) => {
  const state = await setup(page);
  await login(page);
  await navigate(page, '/equipe/produtos/novo');
  await page.getByLabel('Nome', { exact: true }).fill('Uai Sô');
  await page
    .getByRole('combobox', { name: 'Categoria', exact: true })
    .selectOption(initialCategory.id);
  await page.getByLabel('Preço (R$)', { exact: true }).fill('25,90');
  state.saveCode = 'DuplicateProductName';
  await page.getByRole('button', { name: 'Salvar produto' }).click();
  await expect(page.getByRole('alert')).toContainText('Já existe um produto');
  await expect(page.getByLabel('Preço (R$)', { exact: true })).toHaveValue('25,90');
  state.saveCode = 'InactiveProductCategory';
  await page.getByRole('button', { name: 'Salvar produto' }).click();
  await expect(page.getByRole('alert')).toContainText('Selecione uma categoria ativa');
  await expect(page.getByLabel('Nome', { exact: true })).toHaveValue('Uai Sô');
});

test('produtos: registro inexistente e acesso sem sessão', async ({ page }) => {
  await setup(page);
  await page.goto('/equipe/produtos');
  await expect(page).toHaveURL(/\/entrar(?:\?.*)?$/);
  await login(page);
  await navigate(page, '/equipe/produtos/inexistente');
  await expect(page.getByRole('alert')).toContainText('Produto não encontrado');
  await expect(page.getByRole('button', { name: 'Salvar produto' })).toHaveCount(0);
});

for (const role of ['Attendant', 'Kitchen', 'Dispatch']) {
  test('produtos: perfil ' + role + ' não acessa administração', async ({ page }) => {
    await setup(page, role);
    await login(page);
    await expect(page.getByRole('link', { name: 'Produtos', exact: true })).toHaveCount(0);
    await navigate(page, '/equipe/produtos/novo');
    await expect(page).toHaveURL(/\/equipe$/);
  });
}
