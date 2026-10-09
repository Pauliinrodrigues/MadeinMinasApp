import { expect, Page, test } from '@playwright/test';
import type { PublicMenu } from '../src/app/core/services/menu-api.service';

const original: PublicMenu = {
  categories: [
    { id: 'burgers', name: 'Hambúrgueres' },
    { id: 'drinks', name: 'Bebidas' },
  ],
  items: [
    {
      id: 'uai',
      categoryId: 'burgers',
      name: 'Uai Sô',
      description: 'Pão, blend e queijo.',
      price: 29.9,
      imageUrl: 'https://menu-images.test/uai.jpg',
      isAvailable: true,
    },
    {
      id: 'special',
      categoryId: 'burgers',
      name: 'Especial',
      description: null,
      price: 35.5,
      imageUrl: null,
      isAvailable: false,
    },
    {
      id: 'drink',
      categoryId: 'drinks',
      name: 'Guaraná',
      description: 'Bem gelado.',
      price: 5.75,
      imageUrl: null,
      isAvailable: true,
    },
  ],
  page: 1,
  pageSize: 24,
  totalCount: 3,
};

async function setup(page: Page) {
  const state = {
    menu: structuredClone(original),
    status: 200,
    queries: [] as URL[],
    writes: [] as string[],
    gate: null as Promise<void> | null,
    imageFailed: false,
  };
  const profile = {
    id: 'admin',
    name: 'Ana',
    username: 'ana',
    role: 'Administrator',
    permissions: ['users.manage'],
  };
  await page.route('https://menu-images.test/**', (route) =>
    state.imageFailed
      ? route.abort()
      : route.fulfill({
          contentType: 'image/svg+xml',
          body: '<svg xmlns="http://www.w3.org/2000/svg" width="400" height="300"><rect width="400" height="300" fill="#722f27"/></svg>',
        }),
  );
  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    if (url.pathname.startsWith('/api/product-images/')) {
      expect(request.headers()['authorization']).toBeUndefined();
      expect(url.origin).toBe('http://localhost:5080');
      return route.fulfill({
        contentType: 'image/png',
        body: Buffer.from(
          'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aDYsAAAAASUVORK5CYII=',
          'base64',
        ),
      });
    }
    if (url.pathname === '/api/menu') {
      expect(request.headers()['authorization']).toBeUndefined();
      expect(request.method()).toBe('GET');
      state.queries.push(url);
      if (state.gate) {
        await state.gate;
      }
      return route.fulfill({ status: state.status, json: state.status === 200 ? state.menu : {} });
    }
    if (url.pathname === '/api/system/status') {
      return route.fulfill({ json: { status: 'available' } });
    }
    if (url.pathname === '/api/auth/login') {
      return route.fulfill({
        json: {
          accessToken: 'staff-menu-token',
          tokenType: 'Bearer',
          expiresAt: new Date(Date.now() + 900000).toISOString(),
          user: profile,
        },
      });
    }
    if (url.pathname === '/api/auth/me') {
      expect(request.headers()['authorization']).toBe('Bearer staff-menu-token');
      return route.fulfill({ json: profile });
    }
    if (request.method() !== 'GET') {
      state.writes.push(url.pathname);
    }
    return route.fulfill({ status: 404, json: {} });
  });
  return state;
}

test('visitante abre o cardápio pelo início, sem login nem gravações', async ({ page }) => {
  const state = await setup(page);
  await page.goto('/');
  await page.getByRole('link', { name: 'Ver cardápio' }).click();
  await expect(page).toHaveURL(/\/pedido$/);
  await expect(page.getByRole('heading', { name: 'Nosso cardápio.' })).toBeVisible();
  await expect(page.getByRole('article', { name: 'Uai Sô' })).toContainText('R$ 29,90');
  await expect(page.getByRole('article', { name: 'Especial' })).toContainText('Indisponível');
  await expect(
    page.getByText('Monte seu carrinho e escolha retirada ou entrega nas regiões atendidas.', {
      exact: false,
    }),
  ).toBeVisible();
  await expect(page.getByRole('button', { name: /comprar|confirmar pedido/i })).toHaveCount(0);
  await expect(
    page.getByRole('button', { name: 'Adicionar Especial', exact: true }),
  ).toBeDisabled();
  expect(state.writes).toEqual([]);
  await page.reload();
  await expect(page.getByRole('article', { name: 'Uai Sô' })).toBeVisible();
});

test('foto própria aparece no cardápio público pela API sem login', async ({ page }) => {
  const state = await setup(page);
  const imageUrl = '/api/product-images/00000000000000000000000000000001.webp';
  state.menu.items[0].imageUrl = imageUrl;
  await page.goto('/pedido');
  const image = page
    .getByRole('article', { name: 'Uai Sô' })
    .getByRole('img', { name: 'Uai Sô', exact: true });
  await expect(image).toHaveAttribute('src', 'http://localhost:5080' + imageUrl);
  await expect(image).toBeVisible();
  await expect
    .poll(() => image.evaluate((element) => (element as HTMLImageElement).naturalWidth))
    .toBe(1);
  expect(state.writes).toEqual([]);
});

test('categoria e paginação usam filtros da API e retornam à primeira página', async ({ page }) => {
  const state = await setup(page);
  state.menu.pageSize = 1;
  state.menu.items = [original.items[0]];
  await page.goto('/pedido');
  await expect(page.getByRole('button', { name: 'Anterior', exact: true })).toBeDisabled();
  state.menu.page = 2;
  state.menu.items = [original.items[1]];
  await page.getByRole('button', { name: 'Próxima', exact: true }).click();
  await expect(page.getByRole('article', { name: 'Especial' })).toBeVisible();
  expect(state.queries.at(-1)?.searchParams.get('page')).toBe('2');
  state.menu = { ...structuredClone(original), items: [original.items[2]], totalCount: 1 };
  await page.getByRole('combobox', { name: 'Categoria', exact: true }).selectOption('drinks');
  await expect(page.getByRole('article', { name: 'Guaraná' })).toBeVisible();
  expect(state.queries.at(-1)?.searchParams.get('categoryId')).toBe('drinks');
  expect(state.queries.at(-1)?.searchParams.get('page')).toBe('1');
  await expect(page.getByRole('article', { name: 'Especial' })).toHaveCount(0);
});

test('carregamento remove valores antigos e bloqueia novas consultas', async ({ page }) => {
  const state = await setup(page);
  await page.goto('/pedido');
  await expect(page.getByRole('article', { name: 'Uai Sô' })).toBeVisible();
  let release!: () => void;
  state.gate = new Promise<void>((resolve) => {
    release = resolve;
  });
  try {
    await page.getByRole('button', { name: 'Atualizar cardápio' }).click();
    await expect(page.getByRole('status', { name: 'Estado do cardápio' })).toHaveText(
      'Carregando cardápio…',
    );
    await expect(page.getByRole('article')).toHaveCount(0);
    await expect(page.getByRole('combobox', { name: 'Categoria', exact: true })).toBeDisabled();
    await expect(page.getByRole('button', { name: 'Atualizar cardápio' })).toBeDisabled();
    state.menu.items[0].price = 31.25;
    state.menu.items[0].isAvailable = false;
  } finally {
    release();
  }
  await expect(page.getByRole('article', { name: 'Uai Sô' })).toContainText('R$ 31,25');
  await expect(page.getByRole('article', { name: 'Uai Sô' })).toContainText('Indisponível');
});

test('erro inicial pode ser retomado e falha ao atualizar não mantém produtos antigos', async ({
  page,
}) => {
  const state = await setup(page);
  state.status = 503;
  await page.goto('/pedido');
  await expect(page.getByRole('alert')).toContainText('Não foi possível carregar');
  state.status = 200;
  await page.getByRole('button', { name: 'Tentar novamente' }).click();
  await expect(page.getByRole('article', { name: 'Uai Sô' })).toBeVisible();
  state.status = 500;
  await page.getByRole('button', { name: 'Atualizar cardápio' }).click();
  await expect(page.getByRole('alert')).toBeVisible();
  await expect(page.getByRole('article')).toHaveCount(0);
});

test('cardápio vazio tem orientação sem produtos fictícios', async ({ page }) => {
  const state = await setup(page);
  state.menu = { categories: [], items: [], page: 1, pageSize: 24, totalCount: 0 };
  await page.goto('/pedido');
  await expect(page.getByRole('heading', { name: 'Nenhum produto por aqui' })).toBeVisible();
  await expect(page.getByRole('article')).toHaveCount(0);
  await expect(page.getByRole('navigation', { name: 'Páginas do cardápio' })).toHaveCount(0);
});

test('categoria removida permite voltar ao cardápio completo', async ({ page }) => {
  const state = await setup(page);
  await page.goto('/pedido');
  await expect(page.getByRole('article', { name: 'Uai Sô' })).toBeVisible();
  state.menu = {
    ...structuredClone(original),
    categories: [original.categories[0]],
    items: [],
    totalCount: 0,
  };
  await page.getByRole('combobox', { name: 'Categoria', exact: true }).selectOption('drinks');
  await expect(page.getByRole('heading', { name: 'Nenhum produto por aqui' })).toBeVisible();
  await expect(page.getByRole('combobox')).toHaveValue('drinks');
  state.menu = structuredClone(original);
  await page.getByRole('button', { name: 'Ver todas as categorias' }).click();
  await expect(page.getByRole('article', { name: 'Uai Sô' })).toBeVisible();
  expect(state.queries.at(-1)?.searchParams.has('categoryId')).toBe(false);
});

test('imagem quebrada usa apresentação alternativa e atualização tenta a imagem novamente', async ({
  page,
}) => {
  const state = await setup(page);
  state.imageFailed = true;
  await page.goto('/pedido');
  const card = page.getByRole('article', { name: 'Uai Sô' });
  await expect(card).toBeVisible();
  await expect(card.locator('.image-placeholder')).toBeVisible();
  await expect(card.getByRole('img')).toHaveCount(0);
  state.imageFailed = false;
  await page.getByRole('button', { name: 'Atualizar cardápio' }).click();
  await expect(card.getByRole('img', { name: 'Uai Sô' })).toBeVisible();
});

test('texto comercial é escapado e conteúdo extenso cabe no celular', async ({ page }) => {
  const state = await setup(page);
  state.menu.items[0].name = 'Hambúrguer '.repeat(10);
  state.menu.items[0].description = '<script>window.alert("unsafe")</script>' + 'x'.repeat(900);
  state.menu.items[0].price = 999999.99;
  const errors: string[] = [];
  page.on('pageerror', (error) => errors.push(error.message));
  await page.goto('/pedido');
  await expect(page.getByText(state.menu.items[0].description, { exact: true })).toBeVisible();
  await expect(page.locator('article script')).toHaveCount(0);
  const width = await page
    .locator('main')
    .evaluate((main) => ({ content: main.scrollWidth, visible: main.clientWidth }));
  expect(width.content).toBeLessThanOrEqual(width.visible + 1);
  expect(errors).toEqual([]);
});

test('consulta pública não envia token da equipe nem encerra sessão após falha', async ({
  page,
}) => {
  const state = await setup(page);
  await page.goto('/entrar?returnUrl=%2Fequipe');
  await page.getByLabel('Login', { exact: true }).fill('ana');
  await page.getByLabel('Senha', { exact: true }).fill('Senha de teste 123!');
  await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await expect(page).toHaveURL(/\/equipe$/);
  // Navegação interna preserva a sessão em memória, como um link Angular.
  await page.evaluate(() => {
    history.pushState(null, '', '/pedido');
    dispatchEvent(new PopStateEvent('popstate'));
  });
  await expect(page.getByRole('article', { name: 'Uai Sô' })).toBeVisible();
  state.status = 401;
  await page.getByRole('button', { name: 'Atualizar cardápio' }).click();
  await expect(page.getByRole('alert')).toBeVisible();
  await expect(page).toHaveURL(/\/pedido$/);
  await page.getByRole('link', { name: 'Área da equipe', exact: true }).click();
  await expect(page).toHaveURL(/\/equipe$/);
});

test('consulta demorada termina com mensagem e permite tentar novamente', async ({ page }) => {
  const state = await setup(page);
  await page.clock.install();
  let release!: () => void;
  state.gate = new Promise<void>((resolve) => {
    release = resolve;
  });
  await page.goto('/pedido');
  await expect(page.getByRole('status', { name: 'Estado do cardápio' })).toHaveText(
    'Carregando cardápio…',
  );
  try {
    await page.clock.fastForward(16000);
    await expect(page.getByRole('alert')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Atualizar cardápio' })).toBeEnabled();
  } finally {
    release();
    state.gate = null;
  }
  await page.getByRole('button', { name: 'Tentar novamente' }).click();
  await expect(page.getByRole('article', { name: 'Uai Sô' })).toBeVisible();
});
