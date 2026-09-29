import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { tap } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthSession } from '../auth/auth-session.service';
import { LoginResponse, StaffProfile, StaffRole, StaffUser, UserInput, UserPage } from '../auth/auth.models';

@Injectable({ providedIn: 'root' })
export class StaffApi {
  private readonly http = inject(HttpClient);
  private readonly session = inject(AuthSession);
  private readonly url = environment.apiBaseUrl;

  login(username: string, password: string) {
    return this.http.post<LoginResponse>(this.url + '/auth/login', { username, password })
      .pipe(tap(response => this.session.start(response)));
  }
  me() { return this.http.get<StaffProfile>(this.url + '/auth/me'); }
  logout() { return this.http.post<void>(this.url + '/auth/logout', {}); }
  changePassword(currentPassword: string, newPassword: string) {
    return this.http.put<void>(this.url + '/auth/password', { currentPassword, newPassword });
  }
  roles() { return this.http.get<StaffRole[]>(this.url + '/roles'); }
  users(page: number, search: string, roleId: string, active: string) {
    let params = new HttpParams().set('page', page).set('pageSize', 20);
    if (search.trim()) params = params.set('search', search.trim());
    if (roleId) params = params.set('roleId', roleId);
    if (active) params = params.set('isActive', active);
    return this.http.get<UserPage>(this.url + '/users', { params });
  }
  user(id: string) { return this.http.get<StaffUser>(this.url + '/users/' + encodeURIComponent(id)); }
  create(input: UserInput & { password: string }) { return this.http.post<StaffUser>(this.url + '/users', input); }
  update(id: string, input: UserInput) { return this.http.put<StaffUser>(this.url + '/users/' + encodeURIComponent(id), input); }
  status(id: string, isActive: boolean) {
    return this.http.put<StaffUser>(this.url + '/users/' + encodeURIComponent(id) + '/status', { isActive });
  }
  resetPassword(id: string, newPassword: string) {
    return this.http.put<void>(this.url + '/users/' + encodeURIComponent(id) + '/password', { newPassword });
  }
}

