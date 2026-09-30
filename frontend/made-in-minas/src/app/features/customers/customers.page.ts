import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { finalize, Subscription } from 'rxjs';
import { apiError } from '../../core/api-error';
import { Customer, CustomerApi, CustomerPage } from '../../core/services/customer-api.service';

@Component({
  selector: 'app-customers',
  imports: [FormsModule, RouterLink],
  templateUrl: './customers.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CustomersPage {
  private readonly api = inject(CustomerApi);
  private readonly destroyRef = inject(DestroyRef);
  private request?: Subscription;
  readonly result = signal<CustomerPage | null>(null);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  readonly pending = signal<Customer | null>(null);
  search = '';
  active = '';
  page = 1;

  constructor() {
    this.load();
  }

  load(page = 1): void {
    this.request?.unsubscribe();
    this.page = page;
    this.loading.set(true);
    this.error.set('');
    this.result.set(null);
    this.pending.set(null);
    this.request = this.api
      .list(page, this.search, this.active)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: (result) => this.result.set(result),
        error: (error) => this.error.set(apiError(error)),
      });
  }

  requestStatus(customer: Customer): void {
    this.pending.set(customer);
    this.error.set('');
    this.notice.set('');
  }

  changeStatus(): void {
    const customer = this.pending();
    if (!customer || this.saving()) {
      return;
    }
    this.saving.set(true);
    this.api
      .status(customer.id, !customer.isActive)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.saving.set(false)),
      )
      .subscribe({
        next: () => {
          this.pending.set(null);
          this.notice.set(customer.isActive ? 'Cliente inativado.' : 'Cliente ativado.');
          this.load(this.page);
        },
        error: (error) => {
          this.pending.set(null);
          this.error.set(apiError(error));
        },
      });
  }
}
