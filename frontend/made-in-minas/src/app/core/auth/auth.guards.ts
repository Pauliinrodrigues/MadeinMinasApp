import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthSession } from './auth-session.service';

export const staffGuard: CanActivateFn = () => {
  const session = inject(AuthSession);
  return session.token() ? true : inject(Router).createUrlTree(['/entrar']);
};

export const administratorGuard: CanActivateFn = () => {
  const session = inject(AuthSession);
  const router = inject(Router);
  if (!session.token()) {
    return router.createUrlTree(['/entrar']);
  }
  return session.canManageUsers() ? true : router.createUrlTree(['/equipe']);
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
