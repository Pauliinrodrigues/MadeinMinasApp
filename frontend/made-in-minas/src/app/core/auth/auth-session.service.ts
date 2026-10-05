import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { LoginResponse, StaffProfile } from './auth.models';

@Injectable({ providedIn: 'root' })
export class AuthSession {
  private readonly router = inject(Router);
  private readonly profile = signal<StaffProfile | null>(null);
  private accessToken: string | null = null;
  private expiration = 0;
  private timer?: ReturnType<typeof setTimeout>;
  readonly user = this.profile.asReadonly();
  readonly canManageChat = computed(
    () => this.profile()?.permissions.includes('chat.manage') ?? false,
  );
  readonly canViewReports = computed(
    () => this.profile()?.permissions.includes('reports.view') ?? false,
  );
  readonly canViewDashboard = computed(
    () => this.profile()?.permissions.includes('dashboard.view') ?? false,
  );
  readonly canManageUsers = computed(
    () => this.profile()?.permissions.includes('users.manage') ?? false,
  );
  readonly canManageCatalog = computed(
    () => this.profile()?.permissions.includes('catalog.manage') ?? false,
  );
  readonly notice = signal('');
  readonly canWorkDispatch = computed(
    () => this.profile()?.permissions.includes('dispatch.work') ?? false,
  );
  readonly canPrintKitchen = computed(
    () => this.profile()?.permissions.includes('printing.kitchen') ?? false,
  );
  readonly canPrintDispatch = computed(
    () => this.profile()?.permissions.includes('printing.dispatch') ?? false,
  );
  readonly canWorkKitchen = computed(
    () => this.profile()?.permissions.includes('kitchen.work') ?? false,
  );
  readonly canManageCustomers = computed(
    () => this.profile()?.permissions.includes('customers.manage') ?? false,
  );
  readonly canManageOrders = computed(
    () => this.profile()?.permissions.includes('orders.manage') ?? false,
  );
  readonly canManagePayments = computed(
    () => this.profile()?.permissions.includes('payments.manage') ?? false,
  );
  readonly canRefundPayments = computed(
    () => this.profile()?.permissions.includes('payments.refund') ?? false,
  );

  token(): string | null {
    if (this.accessToken && Date.now() >= this.expiration) {
      this.end('Sua sessão expirou. Entre novamente.');
    }
    return this.accessToken;
  }

  start(response: LoginResponse): void {
    const expiration = Date.parse(response.expiresAt);
    if (!response.accessToken || !Number.isFinite(expiration) || expiration <= Date.now()) {
      throw new Error('Invalid session expiration');
    }
    clearTimeout(this.timer);
    this.accessToken = response.accessToken;
    this.expiration = expiration;
    this.profile.set(response.user);
    this.notice.set('');
    this.timer = setTimeout(
      () => this.end('Sua sessão expirou. Entre novamente.'),
      expiration - Date.now(),
    );
  }

  updateProfile(profile: StaffProfile): void {
    this.profile.set(profile);
  }

  end(message: string): void {
    clearTimeout(this.timer);
    this.accessToken = null;
    this.expiration = 0;
    this.profile.set(null);
    this.notice.set(message);
    void this.router.navigateByUrl('/entrar', { replaceUrl: true });
  }
}
