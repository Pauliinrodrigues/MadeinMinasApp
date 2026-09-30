import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { apiError } from '../../core/api-error';
import {
  Customer,
  CustomerApi,
  CustomerInput,
  validBrazilianPhone,
} from '../../core/services/customer-api.service';

@Component({
  selector: 'app-customer-form',
  imports: [FormsModule, RouterLink],
  templateUrl: './customer-form.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CustomerFormPage {
  private readonly api = inject(CustomerApi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly router = inject(Router);
  readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id');
  readonly loading = signal(false);
  readonly ready = signal(!this.id);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  readonly validPhone = validBrazilianPhone;
  name = '';
  phone = '';
  isActive = true;

  constructor() {
    if (this.id) {
      this.load();
    }
    if (this.router.currentNavigation()?.extras.state?.['customerCreated']) {
      this.notice.set('Cliente criado. Você já pode cadastrar seus endereços.');
    }
  }

  load(): void {
    if (!this.id || this.loading()) {
      return;
    }
    this.loading.set(true);
    this.ready.set(false);
    this.error.set('');
    this.api
      .get(this.id)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: (customer) => {
          this.assign(customer);
          this.ready.set(true);
        },
        error: (error) => this.error.set(apiError(error)),
      });
  }

  save(): void {
    if (this.busy() || !this.ready() || !this.name.trim() || !this.validPhone(this.phone)) {
      return;
    }
    this.busy.set(true);
    this.error.set('');
    this.notice.set('');
    const input: CustomerInput = {
      name: this.name.trim(),
      phone: this.phone.trim(),
      isActive: this.isActive,
    };
    const request = this.id ? this.api.update(this.id, input) : this.api.create(input);
    request
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: (customer) => {
          if (!this.id) {
            void this.router.navigate(['/equipe/clientes', customer.id], {
              replaceUrl: true,
              state: { customerCreated: true },
            });
            return;
          }
          this.assign(customer);
          this.notice.set('Cliente atualizado.');
        },
        error: (error) => this.error.set(apiError(error)),
      });
  }

  private assign(customer: Customer): void {
    this.name = customer.name;
    this.phone = customer.phone;
    this.isActive = customer.isActive;
  }
}
