import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { AuthSession } from '../../core/auth/auth-session.service';
import { roleLabel } from '../../core/auth/auth.models';
import { StaffApi } from '../../core/services/staff-api.service';
import { apiError } from '../../core/api-error';

@Component({
  selector: 'app-account',
  imports: [RouterLink],
  template: `<section class="panel">
    <p class="eyebrow">MINHA CONTA</p>
    <h1>Olá, {{ session.user()?.name }}.</h1>
    <p>
      Perfil: <strong>{{ roleLabel(session.user()?.role ?? '') }}</strong>
    </p>
    <p>Login: {{ session.user()?.username }}</p>
    @if (error()) {
      <p role="alert" class="error">{{ error() }}</p>
      <button (click)="refresh()">Tentar novamente</button>
    }
    @if (session.canManageUsers()) {
      <a class="button primary" routerLink="/equipe/funcionarios">Gerenciar funcionários</a>
    }
    <p class="hint">Use o menu para acessar os módulos disponíveis para seu perfil.</p>
  </section>`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AccountPage {
  readonly session = inject(AuthSession);
  readonly roleLabel = roleLabel;
  readonly error = signal('');
  private readonly api = inject(StaffApi);
  private readonly destroyRef = inject(DestroyRef);
  constructor() {
    this.refresh();
  }
  refresh(): void {
    this.error.set('');
    this.api
      .me()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (profile) => this.session.updateProfile(profile),
        error: (error) => this.error.set(apiError(error)),
      });
  }
}
