import { expect, Page, test } from '@playwright/test';

async function auth(page: Page, role: string, permissions: string[]) {
  await page.route('**/api/**', async (route) => {
    const path = new URL(route.request().url()).pathname;
    const user = { id: 'worker-1', name: 'Equipe', username: 'equipe', role, permissions };
    if (path === '/api/auth/login') {
      return route.fulfill({
        json: {
          accessToken: 'test-token',
          tokenType: 'Bearer',
          expiresAt: new Date(Date.now() + 900000).toISOString(),
          user,
        },
      });
    }
    if (path === '/api/auth/me') {
      return route.fulfill({ json: user });
    }
    if (path === '/api/orders') {
      return route.fulfill({ json: { items: [], totalCount: 0, page: 1, pageSize: 20 } });
    }
    return route.fulfill({ status: 503, json: {} });
  });
}

async function submitLogin(page: Page) {
  await page.getByLabel('Login', { exact: true }).fill('equipe');
  await page.getByLabel('Senha', { exact: true }).fill('Test-password-123!');
  await page.getByRole('button', { name: 'Entrar', exact: true }).click();
}

for (const [role, permission, destination] of [
  ['Administrator', 'dashboard.view', 'dashboard'],
  ['Attendant', 'orders.manage', 'pedidos'],
  ['Kitchen', 'kitchen.work', 'cozinha'],
  ['Dispatch', 'dispatch.work', 'expedicao'],
]) {
  test(`login abre a área de trabalho do perfil ${role}`, async ({ page }) => {
    await auth(page, role, [permission]);
    await page.goto('/entrar');
    await submitLogin(page);
    await expect(page).toHaveURL(new RegExp(`/equipe/${destination}$`));
    expect(await page.evaluate(() => localStorage.length)).toBe(0);
  });
}

test('login retorna à rota interrompida sem aceitar redirecionamento externo', async ({ page }) => {
  await auth(page, 'Attendant', ['orders.manage']);
  await page.goto('/equipe/carrinho');
  await expect(page).toHaveURL(/entrar\?returnUrl=/);
  await submitLogin(page);
  await expect(page).toHaveURL(/\/equipe\/carrinho$/);
  await page.reload();
  await expect(page).toHaveURL(/entrar\?returnUrl=/);
  await submitLogin(page);
  await expect(page).toHaveURL(/\/equipe\/carrinho$/);
  await page.goto('/entrar?returnUrl=https%3A%2F%2Fexample.com');
  await submitLogin(page);
  await expect(page).toHaveURL(/\/equipe\/pedidos$/);
});

test('senha pode ser conferida sem alterar o campo e o aviso de Caps Lock é acessível', async ({
  page,
}) => {
  await auth(page, 'Attendant', ['orders.manage']);
  await page.goto('/entrar');
  const field = page.getByLabel('Senha', { exact: true });
  await field.fill('Minha senha');
  await page.getByRole('button', { name: 'Mostrar senha', exact: true }).click();
  await expect(field).toHaveAttribute('type', 'text');
  await expect(field).toHaveValue('Minha senha');
  await field.dispatchEvent('keyup', { key: 'A', modifierCapsLock: true });
  await expect(page.getByText('Caps Lock está ativado.', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Ocultar senha', exact: true }).click();
  await expect(field).toHaveAttribute('type', 'password');
});

test('cardápio mostra cobertura, busca no servidor e preserva rascunho sem preço antigo', async ({
  page,
}) => {
  let price = 29.9;
  const searches: string[] = [];
  await page.route('**/api/**', async (route) => {
    const url = new URL(route.request().url());
    if (url.pathname === '/api/public-checkout/delivery-areas') {
      return route.fulfill({ json: [] });
    }
    if (url.pathname === '/api/menu') {
      searches.push(url.searchParams.get('search') ?? '');
      return route.fulfill({
        json: {
          categories: [{ id: 'food', name: 'Lanches' }],
          page: 1,
          pageSize: 24,
          totalCount: 1,
          items: [
            {
              id: 'uai',
              categoryId: 'food',
              name: 'Uai Sô',
              price,
              description: null,
              imageUrl: null,
              isAvailable: true,
            },
          ],
        },
      });
    }
    if (url.pathname === '/api/public-cart/quote') {
      const input = route.request().postDataJSON();
      return route.fulfill({
        json: {
          items: input.items.map(
            (item: { productId: string; quantity: number; notes: string | null }) => ({
              ...item,
              name: 'Uai Sô',
              unitPrice: price,
              lineTotal: price * item.quantity,
            }),
          ),
          notes: input.notes,
          subtotal: price * input.items[0].quantity,
          calculatedAt: new Date().toISOString(),
        },
      });
    }
    return route.fulfill({ status: 404, json: {} });
  });
  await page.goto('/pedido');
  await expect(
    page.getByText('No momento, os pedidos pelo site estão disponíveis somente para retirada.'),
  ).toBeVisible();
  await page.getByLabel('Buscar produto', { exact: true }).fill('uai');
  await page.getByRole('button', { name: 'Buscar', exact: true }).click();
  await expect.poll(() => searches.at(-1)).toBe('uai');
  await page.getByRole('button', { name: 'Adicionar Uai Sô', exact: true }).click();
  await page.getByRole('link', { name: 'Ver carrinho (1)', exact: true }).click();
  await page.getByLabel('Quantidade do item 1', { exact: true }).fill('2');
  await page.getByLabel('Observações do item 1', { exact: true }).fill('Sem cebola');
  await page.getByRole('button', { name: 'Revisar carrinho', exact: true }).click();
  await expect(page.getByRole('region', { name: 'Valores revisados' })).toContainText('59,80');
  price = 31;
  await page.reload();
  await expect(page.getByLabel('Quantidade do item 1', { exact: true })).toHaveValue('2');
  await expect(page.getByLabel('Observações do item 1', { exact: true })).toHaveValue('Sem cebola');
  await page.getByRole('button', { name: 'Revisar carrinho', exact: true }).click();
  await expect(page.getByRole('region', { name: 'Valores revisados' })).toContainText('62,00');
  const draft = await page.evaluate(() => sessionStorage.getItem('made-in-minas.public-cart.v1'));
  expect(draft).not.toContain('unitPrice');
  expect(draft).not.toContain('accessToken');
});
