import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { IonContent } from '@ionic/angular/ion-content';
import { finalize } from 'rxjs';
import { AuthSession } from '../../core/auth/auth-session.service';
import { StaffApi } from '../../core/services/staff-api.service';
import { apiError } from '../../core/api-error';

@Component({
  selector: 'app-login',
  imports: [FormsModule, RouterLink, IonContent],
  host: { class: 'ion-page' },
  templateUrl: './login.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoginPage {
  readonly session = inject(AuthSession);
  private readonly api = inject(StaffApi);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  readonly busy = signal(false);
  readonly error = signal('');
  username = '';
  password = '';
  showPassword = false;
  capsLock = false;

  checkCapsLock(event: KeyboardEvent): void {
    this.capsLock = event.getModifierState('CapsLock');
  }

  private destination(): string {
    return this.session.returnUrl(this.route.snapshot.queryParamMap.get('returnUrl'));
  }

  constructor() {
    if (this.session.token()) {
      void this.router.navigateByUrl(this.destination());
    }
  }

  submit(): void {
    if (this.busy()) {
      return;
    }
    this.busy.set(true);
    this.error.set('');
    this.session.notice.set('');
    this.api
      .login(this.username.trim(), this.password)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: () => {
          this.password = '';
          void this.router.navigateByUrl(this.destination(), { replaceUrl: true });
        },
        error: (error) => {
          this.password = '';
          this.error.set(apiError(error, true));
        },
      });
  }
}
