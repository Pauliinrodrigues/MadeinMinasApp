import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthSession } from './auth-session.service';

export const staffGuard: CanActivateFn = (_, state) => {
  const session = inject(AuthSession);
  return session.token()
    ? true
    : inject(Router).createUrlTree(['/entrar'], { queryParams: { returnUrl: state.url } });
};

export const chatGuard: CanActivateFn = () => {
  const session = inject(AuthSession);
  const router = inject(Router);
  if (!session.token()) {
    return router.createUrlTree(['/entrar']);
  }
  return session.canManageChat() ? true : router.createUrlTree(['/equipe']);
};

export const administratorGuard: CanActivateFn = () => {
  const session = inject(AuthSession);
  const router = inject(Router);
  if (!session.token()) {
    return router.createUrlTree(['/entrar']);
  }
  return session.canManageUsers() ? true : router.createUrlTree(['/equipe']);
};

export const deliveryGuard: CanActivateFn = () =>
  inject(AuthSession).canManageDelivery() ? true : inject(Router).createUrlTree(['/equipe']);

export const printingSettingsGuard: CanActivateFn = () =>
  inject(AuthSession).canManagePrinting() ? true : inject(Router).createUrlTree(['/equipe']);

export const dashboardGuard: CanActivateFn = () => {
  const session = inject(AuthSession);
  const router = inject(Router);
  if (!session.token()) {
    return router.createUrlTree(['/entrar']);
  }
  return session.canViewDashboard() ? true : router.createUrlTree(['/equipe']);
};

export const reportsGuard: CanActivateFn = () => {
  const session = inject(AuthSession);
  const router = inject(Router);
  if (!session.token()) {
    return router.createUrlTree(['/entrar']);
  }
  return session.canViewReports() ? true : router.createUrlTree(['/equipe']);
};

export const catalogGuard: CanActivateFn = () => {
  const session = inject(AuthSession);
  const router = inject(Router);
  if (!session.token()) {
    return router.createUrlTree(['/entrar']);
  }
  return session.canManageCatalog() ? true : router.createUrlTree(['/equipe']);
};

export const customersGuard: CanActivateFn = () => {
  const session = inject(AuthSession);
  const router = inject(Router);
  if (!session.token()) {
    return router.createUrlTree(['/entrar']);
  }
  return session.canManageCustomers() ? true : router.createUrlTree(['/equipe']);
};

export const ordersGuard: CanActivateFn = () => {
  const session = inject(AuthSession);
  const router = inject(Router);
  if (!session.token()) {
    return router.createUrlTree(['/entrar']);
  }
  return session.canManageOrders() ? true : router.createUrlTree(['/equipe']);
};

export const paymentsGuard: CanActivateFn = () => {
  const session = inject(AuthSession);
  const router = inject(Router);
  if (!session.token()) {
    return router.createUrlTree(['/entrar']);
  }
  return session.canManagePayments() ? true : router.createUrlTree(['/equipe']);
};

export const kitchenGuard: CanActivateFn = () => {
  const session = inject(AuthSession);
  const router = inject(Router);
  if (!session.token()) {
    return router.createUrlTree(['/entrar']);
  }
  return session.canWorkKitchen() ? true : router.createUrlTree(['/equipe']);
};

export const dispatchGuard: CanActivateFn = () => {
  const session = inject(AuthSession);
  const router = inject(Router);
  if (!session.token()) {
    return router.createUrlTree(['/entrar']);
  }
  return session.canWorkDispatch() ? true : router.createUrlTree(['/equipe']);
};

export const printGuard: CanActivateFn = (route, state) => {
  const session = inject(AuthSession);
  const router = inject(Router);
  if (!session.token()) {
    return router.createUrlTree(['/entrar'], { queryParams: { returnUrl: state.url } });
  }
  const mode = route.paramMap.get('mode');
  return (mode === 'kitchen' && session.canPrintKitchen()) ||
    (mode === 'dispatch' && session.canPrintDispatch())
    ? true
    : router.createUrlTree(['/equipe']);
};
