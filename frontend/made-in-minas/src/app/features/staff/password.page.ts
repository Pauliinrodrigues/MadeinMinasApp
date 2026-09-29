import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { finalize } from 'rxjs';
import { AuthSession } from '../../core/auth/auth-session.service';
import { StaffApi } from '../../core/services/staff-api.service';
import { apiError } from '../../core/api-error';

@Component({
  selector: 'app-password', imports: [FormsModule],
  template: `<section class="panel narrow">
    <p class="eyebrow">SEGURANÇA</p><h1>Minha senha</h1>
    <p>Após salvar, todas as suas sessões serão encerradas. Entre novamente com a nova senha.</p>
    @if (error()) { <p role="alert" class="error">{{ error() }}</p> }
    <form #form="ngForm" (ngSubmit)="save()" class="form-stack">
      <label>Senha atual<input name="currentPassword" type="password" [(ngModel)]="currentPassword"
        required maxlength="128" autocomplete="current-password" [disabled]="busy()"></label>
      <label>Nova senha<input name="newPassword" type="password" [(ngModel)]="newPassword"
        required minlength="15" maxlength="128" autocomplete="new-password" [disabled]="busy()"></label>
      <p class="hint">Use de 15 a 128 caracteres. Uma frase longa é uma boa opção.</p>
      <label>Confirme a nova senha<input name="confirmation" type="password" [(ngModel)]="confirmation"
        required maxlength="128" autocomplete="new-password" [disabled]="busy()"></label>
      @if (confirmation && confirmation !== newPassword) { <p class="error">As senhas não coincidem.</p> }
      <button class="primary" [disabled]="busy() || form.invalid || !newPassword.trim() || newPassword !== confirmation">
        {{ busy() ? 'Salvando…' : 'Alterar senha' }}</button>
    </form>
  </section>`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PasswordPage {
  private readonly api = inject(StaffApi);
  private readonly session = inject(AuthSession);
  private readonly destroyRef = inject(DestroyRef);
  readonly error = signal('');
  readonly busy = signal(false);
  currentPassword = '';
  newPassword = '';
  confirmation = '';
  save(): void {
    if (this.busy() || this.newPassword !== this.confirmation || !this.newPassword.trim()) return;
    this.busy.set(true);
    this.error.set('');
    this.api.changePassword(this.currentPassword, this.newPassword).pipe(
      takeUntilDestroyed(this.destroyRef), finalize(() => this.busy.set(false)),
    ).subscribe({
      next: () => { this.clearPasswords(); this.session.end('Senha alterada. Entre novamente com sua nova senha.'); },
      error: error => { this.clearPasswords(); this.error.set(apiError(error)); },
    });
  }
  private clearPasswords(): void { this.currentPassword = ''; this.newPassword = ''; this.confirmation = ''; }
}

