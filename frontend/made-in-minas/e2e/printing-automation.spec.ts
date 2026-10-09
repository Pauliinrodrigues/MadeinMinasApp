import { expect, Page, test } from '@playwright/test';

async function setup(page: Page, role = 'Administrator') {
  const now = new Date().toISOString();
  const profile = {
    id: 'staff',
    name: 'Equipe Teste',
    username: 'staff',
    role,
    permissions:
      role === 'Administrator'
        ? ['users.manage', 'printing.manage', 'printing.kitchen', 'printing.dispatch']
        : ['printing.kitchen', 'kitchen.work'],
  };
  const state = {
    settings: {
      registered: false,
      automatic: false,
      version: 1,
      lastSeenAt: null as string | null,
    },
    jobs: [
      {
        id: 'job-1',
        orderId: 'order-1',
        orderNumber: 123,
        mode: 'kitchen',
        state: 'Review',
        createdAt: now,
      },
    ],
    stationCalls: 0,
    sends: [] as { requestId: string; expectedVersion: number }[],
    failSend: true,
  };
  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (path === '/api/auth/login') {
      return route.fulfill({
        json: {
          accessToken: 'print-token',
          expiresAt: new Date(Date.now() + 900000).toISOString(),
          tokenType: 'Bearer',
          user: profile,
        },
      });
    }
    expect(request.headers()['authorization']).toBe('Bearer print-token');
    if (path === '/api/auth/me') {
      return route.fulfill({ json: profile });
    }
    if (path === '/api/printing/settings') {
      if (request.method() === 'PUT') {
        expect(request.postDataJSON().expectedVersion).toBe(state.settings.version);
        state.settings.automatic = request.postDataJSON().automatic;
        state.settings.version++;
      }
      return route.fulfill({ json: state.settings });
    }
    if (path === '/api/printing/station') {
      state.stationCalls++;
      state.settings.registered = true;
      state.settings.version++;
      return route.fulfill({ json: { settings: state.settings, token: '1'.repeat(64) } });
    }
    if (path === '/api/printing/jobs') {
      return route.fulfill({ json: state.jobs });
    }
    if (path === '/api/print/orders/order-1/kitchen') {
      return route.fulfill({
        json: {
          generatedAt: now,
          order: {
            id: 'order-1',
            number: 123,
            version: 2,
            fulfillment: 'Pickup',
            status: 'Confirmed',
            createdAt: now,
            notes: null,
            items: [{ name: 'Uai Sô', quantity: 1, notes: 'Sem cebola' }],
          },
        },
      });
    }
    if (path === '/api/printing/orders/order-1/kitchen') {
      state.sends.push(request.postDataJSON());
      if (state.failSend) {
        return route.fulfill({ status: 503, json: {} });
      }
      return route.fulfill({ json: { id: 'job-2', state: 'Queued' } });
    }
    return route.fulfill({ status: 404, json: {} });
  });
  await page.goto('/entrar?returnUrl=%2Fequipe');
  await page.getByLabel('Login', { exact: true }).fill('staff');
  await page.getByLabel('Senha', { exact: true }).fill('Senha apenas para testes');
  await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await expect(page).toHaveURL(/\/equipe$/);
  return state;
}
async function navigate(page: Page, path: string) {
  await page.evaluate((path) => {
    history.pushState(null, '', path);
    dispatchEvent(new PopStateEvent('popstate'));
  }, path);
}

test('estação: download, conexão, ativação e conferência da fila', async ({ page }) => {
  const state = await setup(page);
  await page.getByRole('link', { name: 'Impressão automática', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Ativar automação', exact: true })).toBeDisabled();
  const downloaded = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Baixar configuração da estação', exact: true }).click();
  expect((await downloaded).suggestedFilename()).toBe('MadeInMinas-printer.json');
  expect(state.stationCalls).toBe(1);
  state.settings.lastSeenAt = new Date().toISOString();
  await page.getByRole('button', { name: 'Atualizar fila', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Ativar automação', exact: true })).toBeEnabled();
  await page.getByRole('button', { name: 'Ativar automação', exact: true }).click();
  await expect(
    page.getByRole('button', { name: 'Desativar automação', exact: true }),
  ).toBeVisible();
  expect(state.settings.automatic).toBe(true);
  await expect(page.getByText('Conferir na impressora', { exact: false })).toBeVisible();
  await page.getByRole('button', { name: 'Substituir estação', exact: true }).click();
  expect(state.stationCalls).toBe(1);
  await expect(
    page.getByRole('button', { name: 'Confirmar substituição e baixar', exact: true }),
  ).toBeVisible();
  await page.getByRole('button', { name: 'Voltar', exact: true }).click();
  await expect(
    page.getByRole('button', { name: 'Confirmar substituição e baixar', exact: true }),
  ).toHaveCount(0);
});

test('envio direto preserva tentativa após falha e exige outra cópia explícita', async ({
  page,
}) => {
  const state = await setup(page, 'Kitchen');
  await navigate(page, '/comanda/order-1/kitchen');
  await expect(page.getByRole('article', { name: 'Prévia da comanda' })).toBeVisible();
  let printCalls = 0;
  await page.exposeFunction('trackPrint', () => printCalls++);
  await page.evaluate(() => {
    window.print = () => {
      void (window as unknown as { trackPrint(): Promise<void> }).trackPrint();
    };
  });
  await page.getByRole('button', { name: 'Enviar à impressora', exact: true }).click();
  await expect(page.getByRole('alert')).toBeVisible();
  state.failSend = false;
  await page.getByRole('button', { name: 'Enviar à impressora', exact: true }).click();
  await expect(
    page.getByRole('button', { name: 'Na fila de impressão', exact: true }),
  ).toBeDisabled();
  expect(state.sends).toHaveLength(2);
  expect(state.sends[0].requestId).toBe(state.sends[1].requestId);
  expect(state.sends[1].expectedVersion).toBe(2);
  expect(printCalls).toBe(0);
  await page.getByRole('button', { name: 'Preparar outra cópia', exact: true }).click();
  await page.getByRole('button', { name: 'Enviar à impressora', exact: true }).click();
  await expect(
    page.getByRole('button', { name: 'Na fila de impressão', exact: true }),
  ).toBeDisabled();
  expect(state.sends[2].requestId).not.toBe(state.sends[1].requestId);
});

test('cozinha não configura estação e recebe somente a via de produção', async ({ page }) => {
  await setup(page, 'Kitchen');
  await expect(page.getByRole('link', { name: 'Impressão automática', exact: true })).toHaveCount(
    0,
  );
  await navigate(page, '/equipe/impressao');
  await expect(page).toHaveURL(/\/equipe$/);
  await navigate(page, '/comanda/order-1/kitchen');
  await expect(page.getByRole('article', { name: 'Prévia da comanda' })).not.toContainText(
    'Cliente:',
  );
});
