import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { finalize, Subscription } from 'rxjs';
import { apiError } from '../../core/api-error';
import { StaffRole, StaffUser, UserPage, roleLabel } from '../../core/auth/auth.models';
import { AuthSession } from '../../core/auth/auth-session.service';
import { StaffApi } from '../../core/services/staff-api.service';

@Component({
  selector: 'app-users',
  imports: [FormsModule, RouterLink],
  templateUrl: './users.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UsersPage {
  private readonly api = inject(StaffApi);
  private readonly session = inject(AuthSession);
  private readonly destroyRef = inject(DestroyRef);
  private request?: Subscription;
  readonly roleLabel = roleLabel;
  readonly roles = signal<StaffRole[]>([]);
  readonly result = signal<UserPage | null>(null);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = signal('');
  readonly roleError = signal('');
  readonly notice = signal('');
  readonly pending = signal<StaffUser | null>(null);
  search = '';
  roleId = '';
  active = '';
  page = 1;

  constructor() {
    this.loadRoles();
    this.load();
  }

  loadRoles(): void {
    this.roleError.set('');
    this.api
      .roles()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (roles) => this.roles.set(roles),
        error: (error) => this.roleError.set(apiError(error)),
      });
  }

  load(page = 1): void {
    this.request?.unsubscribe();
    this.page = page;
    this.loading.set(true);
    this.error.set('');
    this.result.set(null);
    this.request = this.api
      .users(page, this.search, this.roleId, this.active)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: (result) => this.result.set(result),
        error: (error) => this.error.set(apiError(error)),
      });
  }

  requestStatus(user: StaffUser): void {
    this.pending.set(user);
    this.error.set('');
    this.notice.set('');
  }

  changeStatus(): void {
    const user = this.pending();
    if (!user || this.saving()) {
      return;
    }
    this.saving.set(true);
    this.api
      .status(user.id, !user.isActive)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.saving.set(false)),
      )
      .subscribe({
        next: () => {
          this.pending.set(null);
          if (user.id === this.session.user()?.id) {
            this.session.end('Seu acesso foi alterado. Entre novamente com uma conta ativa.');
            return;
          }
          this.notice.set(
            user.isActive
              ? 'Funcionário inativado. As sessões dele foram revogadas.'
              : 'Funcionário ativado.',
          );
          this.load(this.page);
        },
        error: (error) => {
          this.pending.set(null);
          this.error.set(apiError(error));
        },
      });
  }
}
