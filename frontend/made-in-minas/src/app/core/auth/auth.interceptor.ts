import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError, timeout } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthSession } from './auth-session.service';

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const api = new URL(environment.apiBaseUrl, window.location.origin);
  const target = new URL(request.url, window.location.origin);
  const apiPath = api.pathname.replace(/\/$/, '');
  if (target.origin !== api.origin || !target.pathname.startsWith(apiPath + '/')) return next(request);

  const session = inject(AuthSession);
  const isLogin = target.pathname === apiPath + '/auth/login';
  const token = isLogin ? null : session.token();
  const authenticated = token ? request.clone({ setHeaders: { Authorization: 'Bearer ' + token } }) : request;
  return next(authenticated).pipe(
    timeout(15000),
    catchError((error: unknown) => {
      if (token && typeof error === 'object' && error !== null && 'status' in error &&
          error.status === 401 && session.token() === token) {
        session.end('Sua sessão expirou ou foi revogada. Entre novamente.');
      }
      return throwError(() => error);
    }),
  );
};

