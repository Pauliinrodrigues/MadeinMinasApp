import { expect, Page, test } from '@playwright/test';
import { StoreAttendanceData } from '../src/app/core/services/store-attendance-api.service';
import { publicAttendanceFixture } from './fixtures/store-attendance';

async function setup(page: Page, administrator = true) {
  const fixture = publicAttendanceFixture();
  const state = {
    settings: {
      hours: fixture.hours,
      paymentMethods: fixture.paymentMethods,
      deliveryEstimateMessage: fixture.deliveryEstimateMessage,
      isPaused: false,
      receiveOrdersOutsideHours: false,
    } as StoreAttendanceData,
    revision: 'A'.repeat(64),
    saveStatus: 200,
    writes: [] as { expectedRevision: string; settings: StoreAttendanceData }[],
  };
  const profile = {
    id: 'staff',
    name: 'Equipe de teste',
    username: 'equipe',
    role: administrator ? 'Administrator' : 'Attendant',
    permissions: administrator ? ['delivery.manage'] : [],
  };
  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    const json = (body: unknown, status = 200) => route.fulfill({ json: body, status });
    if (path === '/api/auth/login') {
      return json({
        user: profile,
        accessToken: 'attendance-token',
        tokenType: 'Bearer',
        expiresAt: new Date(Date.now() + 900000).toISOString(),
      });
    }
    expect(request.headers()['authorization']).toBe('Bearer attendance-token');
    if (path === '/api/auth/me') {
      return json(profile);
    }
    const response = () => ({
      settings: state.settings,
      revision: state.revision,
      updatedAt: null,
      updatedBy: null,
    });
    if (path === '/api/store-attendance' && request.method() === 'GET') {
      return json(response());
    }
    if (path === '/api/store-attendance' && request.method() === 'PUT') {
      const input = request.postDataJSON() as (typeof state.writes)[number];
      state.writes.push(input);
      if (state.saveStatus !== 200) {
        return json(
          { code: state.saveStatus === 409 ? 'StoreSettingsChanged' : undefined },
          state.saveStatus,
        );
      }
      state.settings = structuredClone(input.settings);
      state.revision = 'B'.repeat(64);
      return json(response());
    }
    return json({}, 404);
  });
  return state;
}
async function open(page: Page) {
  await page.goto('/entrar?returnUrl=%2Fequipe');
  await page.getByLabel('Login', { exact: true }).fill('equipe');
  await page.getByLabel('Senha', { exact: true }).fill('Senha de teste');
  await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await page.getByRole('link', { name: 'Atendimento da loja', exact: true }).click();
  await expect(page.getByLabel('Abertura de Segunda-feira', { exact: true })).toHaveValue('18:30');
}
test('loja: horários de Brasília, meia-noite e pausa ficam persistidos após revisão', async ({
  page,
}) => {
  const state = await setup(page);
  await open(page);
  await expect(page.getByLabel('Fechamento de Quinta-feira', { exact: true })).toHaveValue('23:30');
  await expect(page.getByLabel('Fechamento de Sexta-feira', { exact: true })).toHaveValue('00:00');
  await expect(page.getByLabel('Fechamento de Domingo', { exact: true })).toHaveValue('00:00');
  await page.getByLabel('Pausar novos pedidos', { exact: true }).check();
  await page.getByRole('button', { name: 'Revisar configurações', exact: true }).click();
  expect(state.writes).toHaveLength(0);
  const review = page.getByRole('region', { name: 'Revisar atendimento' });
  await expect(review).toContainText('Novos pedidos ficarão pausados.');
  await expect(review).toContainText('Cartão, Dinheiro, Pix');
  await page.getByRole('button', { name: 'Confirmar configurações', exact: true }).click();
  await expect(page.getByRole('status')).toContainText('Configurações de atendimento salvas.');
  expect(state.writes[0].settings.isPaused).toBe(true);
  await page.reload();
  await expect(page).toHaveURL(/\/entrar/);
  await open(page);
  await expect(page.getByLabel('Pausar novos pedidos', { exact: true })).toBeChecked();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(
    true,
  );
});
test('loja: horários inválidos ou nenhum pagamento impedem o salvamento', async ({ page }) => {
  const state = await setup(page);
  await open(page);
  await page.getByLabel('Fechamento de Segunda-feira', { exact: true }).fill('17:00');
  await page.getByRole('button', { name: 'Revisar configurações', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('Confira os horários');
  await page.getByLabel('Fechamento de Segunda-feira', { exact: true }).fill('23:30');
  for (const name of ['Cartão', 'Dinheiro', 'Pix']) {
    await page.getByLabel(name, { exact: true }).uncheck();
  }
  await page.getByRole('button', { name: 'Revisar configurações', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('forma de pagamento');
  expect(state.writes).toHaveLength(0);
});
test('loja: alteração concorrente exige recarga e resposta perdida preserva a revisão', async ({
  page,
}) => {
  const state = await setup(page);
  await open(page);
  await page.getByLabel('Receber pedidos fora do horário', { exact: true }).check();
  await page.getByRole('button', { name: 'Revisar configurações', exact: true }).click();
  state.saveStatus = 503;
  await page.getByRole('button', { name: 'Confirmar configurações', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('Não foi possível confirmar o salvamento');
  await expect(page.getByRole('button', { name: 'Voltar à edição', exact: true })).toBeDisabled();
  state.saveStatus = 409;
  await page.getByRole('button', { name: 'Confirmar configurações', exact: true }).click();
  expect(state.writes[1]).toEqual(state.writes[0]);
  await expect(page.getByRole('alert')).toContainText('Outro administrador');
  await expect(
    page.getByRole('button', { name: 'Revisar configurações', exact: true }),
  ).toBeDisabled();
  await page.getByRole('button', { name: 'Recarregar configurações', exact: true }).click();
  await expect(
    page.getByRole('button', { name: 'Revisar configurações', exact: true }),
  ).toBeEnabled();
});
