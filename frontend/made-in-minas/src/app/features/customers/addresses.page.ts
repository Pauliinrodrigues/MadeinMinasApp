import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { finalize, forkJoin, Subscription } from 'rxjs';
import { apiError } from '../../core/api-error';
import {
  Address,
  AddressInput,
  AddressPage,
  brazilianStates,
  Customer,
  CustomerApi,
} from '../../core/services/customer-api.service';

function emptyAddress(): AddressInput {
  return {
    street: '',
    number: '',
    neighborhood: '',
    city: '',
    state: '',
    complement: null,
    postalCode: null,
    reference: null,
    isActive: true,
  };
}

@Component({
  selector: 'app-addresses',
  imports: [FormsModule, RouterLink],
  templateUrl: './addresses.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AddressesPage {
  private readonly api = inject(CustomerApi);
  private readonly destroyRef = inject(DestroyRef);
  private request?: Subscription;
  readonly customerId = inject(ActivatedRoute).snapshot.paramMap.get('customerId')!;
  readonly returnToCart = inject(ActivatedRoute).snapshot.queryParamMap.get('returnTo') === 'cart';
  readonly customer = signal<Customer | null>(null);
  readonly result = signal<AddressPage | null>(null);
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  readonly pending = signal<Address | null>(null);
  readonly formOpen = signal(false);
  readonly states = brazilianStates;
  editingId: string | null = null;
  input = emptyAddress();
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
    this.customer.set(null);
    this.pending.set(null);
    this.request = forkJoin({
      customer: this.api.get(this.customerId),
      addresses: this.api.addresses(this.customerId, page, this.active),
    })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: ({ customer, addresses }) => {
          this.customer.set(customer);
          this.result.set(addresses);
        },
        error: (error) => this.error.set(apiError(error)),
      });
  }

  openForm(address?: Address): void {
    this.editingId = address?.id ?? null;
    this.input = address
      ? {
          street: address.street,
          number: address.number,
          neighborhood: address.neighborhood,
          city: address.city,
          state: address.state,
          complement: address.complement,
          postalCode: address.postalCode,
          reference: address.reference,
          isActive: address.isActive,
        }
      : emptyAddress();
    this.pending.set(null);
    this.error.set('');
    this.notice.set('');
    this.formOpen.set(true);
  }

  validAddress(): boolean {
    const input = this.input;
    return !!(
      input.street.trim() &&
      input.number.trim() &&
      input.neighborhood.trim() &&
      input.city.trim() &&
      this.states.includes(input.state) &&
      this.validPostalCode()
    );
  }

  validPostalCode(): boolean {
    const postalCode = this.input.postalCode?.trim();
    return !postalCode || /^[0-9]{5}-?[0-9]{3}$/.test(postalCode);
  }

  save(): void {
    if (this.busy() || !this.customer() || !this.validAddress()) {
      return;
    }
    this.busy.set(true);
    this.error.set('');
    const editing = this.editingId;
    const input: AddressInput = {
      ...this.input,
      street: this.input.street.trim(),
      number: this.input.number.trim(),
      neighborhood: this.input.neighborhood.trim(),
      city: this.input.city.trim(),
      complement: this.input.complement?.trim() || null,
      postalCode: this.input.postalCode?.trim() || null,
      reference: this.input.reference?.trim() || null,
    };
    const request = editing
      ? this.api.updateAddress(this.customerId, editing, input)
      : this.api.createAddress(this.customerId, input);
    request
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: () => {
          this.formOpen.set(false);
          this.notice.set(editing ? 'Endereço atualizado.' : 'Endereço criado.');
          this.load(this.page);
        },
        error: (error) => this.error.set(apiError(error)),
      });
  }

  requestStatus(address: Address): void {
    this.pending.set(address);
    this.error.set('');
    this.notice.set('');
  }

  changeStatus(): void {
    const address = this.pending();
    if (!address || this.busy()) {
      return;
    }
    this.busy.set(true);
    this.api
      .addressStatus(this.customerId, address.id, !address.isActive)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: () => {
          this.pending.set(null);
          this.notice.set(address.isActive ? 'Endereço inativado.' : 'Endereço ativado.');
          this.load(this.page);
        },
        error: (error) => {
          this.pending.set(null);
          this.error.set(apiError(error));
        },
      });
  }
}
