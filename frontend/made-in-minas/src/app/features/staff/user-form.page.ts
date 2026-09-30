import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { finalize, forkJoin, of } from 'rxjs';
import { apiError } from '../../core/api-error';
import { StaffRole, StaffUser, UserInput } from '../../core/auth/auth.models';
import { AuthSession } from '../../core/auth/auth-session.service';
import { StaffApi } from '../../core/services/staff-api.service';

@Component({
  selector: 'app-user-form',
  imports: [FormsModule, RouterLink],
  templateUrl: './user-form.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UserFormPage {
  private readonly api = inject(StaffApi);
  readonly session = inject(AuthSession);
  private readonly destroyRef = inject(DestroyRef);
  private readonly route = inject(ActivatedRoute);
  readonly user = signal<StaffUser | null>(null);
  readonly roles = signal<StaffRole[]>([]);
  readonly loading = signal(true);
  readonly ready = signal(false);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  readonly resetOpen = signal(false);
  readonly id = signal<string | null>(this.route.snapshot.paramMap.get('id'));
  name = '';
  username = '';
  roleId = 0;
  isActive = true;
  password = '';
  confirmation = '';
  resetPassword = '';
  resetConfirmation = '';

  constructor() {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set('');
    const id = this.id();
    forkJoin({ roles: this.api.roles(), user: id ? this.api.user(id) : of(null) })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: ({ roles, user }) => {
          this.roles.set(roles);
          this.user.set(user);
          if (user) {
            this.fill(user);
          }
          this.ready.set(true);
        },
        error: (error) => this.error.set(apiError(error)),
      });
  }

  save(): void {
    if (this.busy()) {
      return;
    }
    this.busy.set(true);
    this.error.set('');
    this.notice.set('');
    const input: UserInput = {
      name: this.name.trim(),
      username: this.username.trim(),
      roleId: this.roleId,
      isActive: this.isActive,
    };
    const id = this.id();
    const original = this.user();
    const request = id
      ? this.api.update(id, input)
      : this.api.create({ ...input, password: this.password });
    request
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: (user) => {
          this.password = '';
          this.confirmation = '';
          if (
            user.id === this.session.user()?.id &&
            original &&
            (original.username !== user.username ||
              original.roleId !== user.roleId ||
              original.isActive !== user.isActive)
          ) {
            this.session.end('Seu acesso foi alterado. Entre novamente.');
            return;
          }
          if (user.id === this.session.user()?.id) {
            const profile = this.session.user();
            if (profile) {
              this.session.updateProfile({ ...profile, name: user.name });
            }
          }
          this.id.set(user.id);
          this.user.set(user);
          this.fill(user);
          this.notice.set(
            id
              ? 'Cadastro atualizado.'
              : 'Funcionário criado. A senha já pode ser usada para entrar.',
          );
        },
        error: (error) => {
          this.password = '';
          this.confirmation = '';
          this.error.set(apiError(error));
        },
      });
  }

  reset(): void {
    const id = this.id();
    if (!id || this.busy() || this.resetPassword !== this.resetConfirmation) {
      return;
    }
    this.busy.set(true);
    this.error.set('');
    this.notice.set('');
    this.api
      .resetPassword(id, this.resetPassword)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: () => {
          this.closeReset();
          this.notice.set('Senha redefinida. As sessões do funcionário foram encerradas.');
        },
        error: (error) => {
          this.resetPassword = '';
          this.resetConfirmation = '';
          this.error.set(apiError(error));
        },
      });
  }

  closeReset(): void {
    this.resetOpen.set(false);
    this.resetPassword = '';
    this.resetConfirmation = '';
  }
  private fill(user: StaffUser): void {
    this.name = user.name;
    this.username = user.username;
    this.roleId = user.roleId;
    this.isActive = user.isActive;
  }
}
