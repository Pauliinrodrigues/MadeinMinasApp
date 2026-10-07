import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError, timeout } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthSession } from './auth-session.service';

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const api = new URL(environment.apiBaseUrl, window.location.origin);
  const target = new URL(request.url, window.location.origin);
  const apiPath = api.pathname.replace(/\/$/, '');
  if (target.origin !== api.origin || !target.pathname.startsWith(apiPath + '/')) {
    return next(request);
  }

  const session = inject(AuthSession);
  const isLogin = target.pathname === apiPath + '/auth/login';
  const isPublicMenu = target.pathname === apiPath + '/menu';
  const isPublicCart = target.pathname === apiPath + '/public-cart/quote';
  const isPublicTracking = target.pathname === apiPath + '/public-orders/tracking';
  const isPublicChat =
    target.pathname === apiPath + '/public-chat' ||
    target.pathname === apiPath + '/public-chat/messages';
  const isPublicCheckout =
    target.pathname === apiPath + '/public-checkout/delivery-areas' ||
    target.pathname === apiPath + '/public-checkout/review' ||
    target.pathname === apiPath + '/public-checkout/orders';
  const token =
    isLogin || isPublicMenu || isPublicCart || isPublicCheckout || isPublicTracking || isPublicChat
      ? null
      : session.token();
  const authenticated = token
    ? request.clone({ setHeaders: { Authorization: 'Bearer ' + token } })
    : request;
  return next(authenticated).pipe(
    timeout(
      target.pathname === apiPath + '/product-images' && request.method === 'POST' ? 60000 : 15000,
    ),
    catchError((error: unknown) => {
      if (
        token &&
        typeof error === 'object' &&
        error !== null &&
        'status' in error &&
        error.status === 401 &&
        session.token() === token
      ) {
        session.end('Sua sessão expirou ou foi revogada. Entre novamente.');
      }
      return throwError(() => error);
    }),
  );
};
