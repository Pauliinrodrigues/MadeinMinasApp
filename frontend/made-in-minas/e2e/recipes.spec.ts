import { test, expect, Page } from '@playwright/test';
import type { RecipeInput } from '../src/app/core/services/recipe-api.service';

const product = {
  id: 'product-1',
  name: 'Uai Sô',
  categoryId: 'category-1',
  categoryName: 'Lanches',
  categoryIsActive: true,
  price: 29.9,
  description: null,
  imageUrl: null,
  isActive: true,
  isAvailable: true,
  isAvailableForSale: true,
  createdAt: '2026-09-30T12:00:00Z',
  updatedAt: '2026-09-30T12:00:00Z',
};
const meat = {
  id: 'meat',
  name: 'Carne',
  unit: 'kg',
  unitCost: 30,
  minimumStock: 0,
  supplier: null,
  isActive: true,
  createdAt: product.createdAt,
  updatedAt: product.updatedAt,
};
const path = '/equipe/produtos/product-1/ficha-tecnica';

async function setup(page: Page, role = 'Administrator') {
  const profile = {
    id: 'staff',
    name: 'Equipe Teste',
    username: 'equipe.teste',
    role,
    permissions: role === 'Administrator' ? ['catalog.manage'] : [],
  };
  const state = {
    ingredients: [{ ...meat }, { ...meat, id: 'bread', name: 'Pão', unit: 'un' }],
    recipe: null as RecipeInput | null,
    writes: 0,
    ingredientPages: [] as number[],
    loadStatus: 200,
    saveCode: '',
    productMissing: false,
  };
  function response() {
    return {
      ...state.recipe,
      id: 'recipe-1',
      productId: product.id,
      createdAt: product.createdAt,
      updatedAt: product.updatedAt,
      hasInactiveIngredients: state.recipe!.items.some(
        (item) =>
          !state.ingredients.find((ingredient) => ingredient.id === item.ingredientId)!.isActive,
      ),
      items: state.recipe!.items.map((item) => {
        const ingredient = state.ingredients.find(
          (ingredient) => ingredient.id === item.ingredientId,
        )!;
        return {
          ...item,
          ingredientName: ingredient.name,
          unit: ingredient.unit,
          ingredientIsActive: ingredient.isActive,
        };
      }),
    };
  }
  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const json = (body: unknown, status = 200) => route.fulfill({ status, json: body });
    if (url.pathname === '/api/auth/login') {
      return json({
        accessToken: 'recipe-test-token',
        tokenType: 'Bearer',
        expiresAt: new Date(Date.now() + 900000).toISOString(),
        user: profile,
      });
    }
    expect(request.headers()['authorization']).toBe('Bearer recipe-test-token');
    if (url.pathname === '/api/auth/me') {
      return json(profile);
    }
    if (url.pathname === '/api/products') {
      return json({ items: [product], page: 1, pageSize: 20, totalCount: 1 });
    }
    if (url.pathname === '/api/products/product-1') {
      return state.productMissing ? json({ code: 'ProductNotFound' }, 404) : json(product);
    }
    if (url.pathname === '/api/categories') {
      return json({
        items: [{ id: 'category-1', name: 'Lanches', isActive: true }],
        page: 1,
        pageSize: 20,
        totalCount: 1,
      });
    }
    if (url.pathname === '/api/ingredients') {
      if (state.loadStatus !== 200) {
        return json({}, state.loadStatus);
      }
      const current = Number(url.searchParams.get('page') ?? 1);
      state.ingredientPages.push(current);
      return json({
        items: state.ingredients.slice((current - 1) * 20, current * 20),
        page: current,
        pageSize: 20,
        totalCount: state.ingredients.length,
      });
    }
    if (url.pathname === '/api/products/product-1/recipe') {
      if (state.productMissing) {
        return json({ code: 'ProductNotFound' }, 404);
      }
      if (request.method() === 'GET') {
        return state.recipe ? json(response()) : json({ code: 'RecipeNotFound' }, 404);
      }
      state.writes++;
      if (state.saveCode) {
        return json({ code: state.saveCode }, 400);
      }
      const created = !state.recipe;
      state.recipe = request.postDataJSON();
      return json(response(), created ? 201 : 200);
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
async function navigate(page: Page) {
  await page.evaluate((path) => {
    history.pushState(null, '', path);
    dispatchEvent(new PopStateEvent('popstate'));
  }, path);
}

test('ficha: criar, editar, remover item e reabrir pelo produto', async ({ page }) => {
  const errors: string[] = [];
  page.on('pageerror', (error) => errors.push(error.message));
  const state = await setup(page);
  await login(page);
  await page.getByRole('link', { name: 'Produtos', exact: true }).click();
  await page.getByRole('link', { name: 'Ficha técnica de Uai Sô' }).click();
  await expect(
    page.getByText('Este produto ainda não possui ficha técnica.', { exact: false }),
  ).toBeVisible();
  const save = page.getByRole('button', { name: 'Salvar ficha técnica' });
  await expect(save).toBeDisabled();
  await page.getByLabel('Rendimento (unidades do produto)').fill('2');
  await page.getByRole('combobox', { name: 'Ingrediente 1', exact: true }).selectOption('meat');
  await page.getByLabel('Quantidade 1 (kg)').fill('0,300');
  await page.getByRole('button', { name: 'Adicionar ingrediente' }).click();
  await page.getByRole('combobox', { name: 'Ingrediente 2', exact: true }).selectOption('bread');
  await page.getByLabel('Quantidade 2 (un)').fill('2');
  await page.getByLabel('Instruções de preparo (opcional)').fill('  Grelhar e montar  ');
  await save.click();
  await expect(page.getByRole('status')).toContainText('Ficha técnica salva');
  expect(state.recipe).toEqual({
    yieldQuantity: 2,
    instructions: 'Grelhar e montar',
    items: [
      { ingredientId: 'meat', quantity: 0.3 },
      { ingredientId: 'bread', quantity: 2 },
    ],
  });
  await page.getByRole('button', { name: 'Remover ingrediente 2', exact: true }).click();
  await page.getByLabel('Rendimento (unidades do produto)').fill('1');
  await page.getByLabel('Quantidade 1 (kg)').fill('0.150');
  await save.click();
  await expect(page.getByRole('status')).toContainText('Ficha técnica salva');
  await page.getByRole('link', { name: 'Produtos', exact: true }).click();
  await page.getByRole('link', { name: 'Ficha técnica de Uai Sô' }).click();
  await expect(page.getByLabel('Quantidade 1 (kg)')).toHaveValue('0,15');
  await expect(page.getByLabel('Rendimento (unidades do produto)')).toHaveValue('1');
  expect(state.writes).toBe(2);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  expect(errors).toEqual([]);
});

test('ficha: valida rendimento, decimais, repetição e ausência de itens', async ({ page }) => {
  await setup(page);
  await login(page);
  await navigate(page);
  await page.getByRole('combobox', { name: 'Ingrediente 1', exact: true }).selectOption('meat');
  const quantity = page.getByLabel('Quantidade 1 (kg)');
  const save = page.getByRole('button', { name: 'Salvar ficha técnica' });
  for (const invalid of ['0', '-1', '0,0001', '1e2', '1.234,56', '1000000']) {
    await quantity.fill(invalid);
    await expect(save).toBeDisabled();
    await expect(page.getByRole('alert')).toContainText('quantidade positiva');
  }
  await quantity.fill('0,001');
  await page.getByLabel('Rendimento (unidades do produto)').fill('1.5');
  await expect(save).toBeDisabled();
  await expect(page.getByRole('alert')).toContainText('rendimento inteiro');
  await page.getByLabel('Rendimento (unidades do produto)').fill('10000');
  await expect(save).toBeEnabled();
  await page.getByRole('button', { name: 'Adicionar ingrediente' }).click();
  await page.getByRole('combobox', { name: 'Ingrediente 2', exact: true }).selectOption('meat');
  await page.getByLabel('Quantidade 2 (kg)').fill('1');
  await expect(save).toBeDisabled();
  await expect(page.getByRole('alert').first()).toContainText('Ingrediente repetido');
  await page.getByRole('button', { name: 'Remover ingrediente 2', exact: true }).click();
  await page.getByRole('button', { name: 'Remover ingrediente 1', exact: true }).click();
  await expect(save).toBeDisabled();
  await expect(
    page.getByText('Adicione pelo menos um ingrediente', { exact: false }),
  ).toBeVisible();
});

test('ficha: seletor percorre todas as páginas de ingredientes', async ({ page }) => {
  const state = await setup(page);
  for (let i = 0; i < 23; i++) {
    state.ingredients.push({ ...meat, id: 'extra-' + i, name: 'Ingrediente ' + i });
  }
  await login(page);
  await navigate(page);
  await page.getByRole('combobox', { name: 'Ingrediente 1', exact: true }).selectOption('extra-22');
  await page.getByLabel('Quantidade 1 (kg)').fill('1');
  await page.getByRole('button', { name: 'Salvar ficha técnica' }).click();
  await expect(page.getByRole('status')).toContainText('Ficha técnica salva');
  expect(state.ingredientPages).toEqual([1, 2]);
  expect(state.recipe!.items[0].ingredientId).toBe('extra-22');
});

test('ficha: inativo existente pode ser mantido, novos vínculos ficam bloqueados', async ({
  page,
}) => {
  const state = await setup(page);
  state.ingredients.forEach((item) => (item.isActive = false));
  state.recipe = {
    yieldQuantity: 1,
    instructions: null,
    items: [{ ingredientId: 'meat', quantity: 0.15 }],
  };
  await login(page);
  await navigate(page);
  await expect(page.getByText('Ingrediente inativo.', { exact: false })).toBeVisible();
  const selector = page.getByRole('combobox', { name: 'Ingrediente 1', exact: true });
  await expect(selector.locator('option[value="bread"]')).toHaveJSProperty('disabled', true);
  await expect(selector.locator('option[value="meat"]')).toHaveJSProperty('disabled', false);
  await page.getByLabel('Quantidade 1 (kg)').fill('0,200');
  await page.getByRole('button', { name: 'Salvar ficha técnica' }).click();
  await expect(page.getByRole('status')).toContainText('Ficha técnica salva');
  expect(state.recipe.items[0].quantity).toBe(0.2);
});

test('ficha: ingrediente inativado durante edição preserva os campos e permite correção', async ({
  page,
}) => {
  const state = await setup(page);
  await login(page);
  await navigate(page);
  await page.getByRole('combobox', { name: 'Ingrediente 1', exact: true }).selectOption('meat');
  await page.getByLabel('Quantidade 1 (kg)').fill('0,150');
  state.saveCode = 'InactiveRecipeIngredient';
  await page.getByRole('button', { name: 'Salvar ficha técnica' }).click();
  await expect(page.getByRole('alert')).toContainText('Novos vínculos exigem ingredientes ativos');
  await expect(page.getByLabel('Quantidade 1 (kg)')).toHaveValue('0,150');
  state.saveCode = '';
  await page.getByRole('combobox', { name: 'Ingrediente 1', exact: true }).selectOption('bread');
  await page.getByLabel('Quantidade 1 (un)').fill('1');
  await page.getByRole('button', { name: 'Salvar ficha técnica' }).click();
  await expect(page.getByRole('status')).toContainText('Ficha técnica salva');
});

test('ficha: falha de carregamento, nova tentativa e produto inexistente', async ({ page }) => {
  const state = await setup(page);
  state.loadStatus = 503;
  await login(page);
  await navigate(page);
  await expect(page.getByRole('alert')).toContainText('servidor não conseguiu');
  await expect(page.getByRole('button', { name: 'Salvar ficha técnica' })).toHaveCount(0);
  state.loadStatus = 200;
  await page.getByRole('button', { name: 'Tentar novamente' }).click();
  await expect(page.getByRole('combobox', { name: 'Ingrediente 1', exact: true })).toBeVisible();
  await page.getByRole('link', { name: 'Minha conta', exact: true }).click();
  state.productMissing = true;
  await navigate(page);
  await expect(page.getByRole('alert')).toContainText('Produto não encontrado');
  await expect(page.getByRole('button', { name: 'Salvar ficha técnica' })).toHaveCount(0);
  expect(state.writes).toBe(0);
});

test('ficha: nenhum ingrediente cadastrado orienta o administrador', async ({ page }) => {
  const state = await setup(page);
  state.ingredients = [];
  await login(page);
  await navigate(page);
  await expect(page.getByRole('link', { name: 'Cadastre um ingrediente' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Salvar ficha técnica' })).toBeDisabled();
});

for (const role of ['Attendant', 'Kitchen', 'Dispatch']) {
  test('ficha: perfil ' + role + ' não acessa por URL', async ({ page }) => {
    const state = await setup(page, role);
    await login(page);
    await navigate(page);
    await expect(page).toHaveURL(/\/equipe$/);
    expect(state.writes).toBe(0);
  });
}

test('ficha: acesso direto sem sessão exige login', async ({ page }) => {
  await setup(page);
  await page.goto(path);
  await expect(page).toHaveURL(/\/entrar(?:\?.*)?$/);
});
