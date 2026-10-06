import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { IonContent } from '@ionic/angular/ion-content';
import { finalize } from 'rxjs';
import { validBrazilianPhone } from '../../core/services/customer-api.service';
import { formatProductPrice } from '../../core/services/product-api.service';
import { PublicCartState } from '../../core/services/public-cart-state.service';
import {
  PublicCheckoutApi,
  PublicCheckoutInput,
  PublicCheckoutReview,
  PublicDeliveryAddress,
  PublicDeliveryArea,
} from '../../core/services/public-checkout-api.service';
import { PublicCheckoutState } from '../../core/services/public-checkout-state.service';

@Component({
  selector: 'app-public-checkout',
  host: { class: 'ion-page' },
  imports: [FormsModule, RouterLink, IonContent],
  templateUrl: './public-checkout.page.html',
  styleUrl: './public-checkout.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PublicCheckoutPage {
  readonly cart = inject(PublicCartState);
  readonly state = inject(PublicCheckoutState);
  private readonly api = inject(PublicCheckoutApi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly router = inject(Router);
  readonly review = signal<PublicCheckoutReview | null>(null);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly attempted = signal(false);
  readonly price = formatProductPrice;
  private reviewedInput: PublicCheckoutInput | null = null;
  name = '';
  phone = '';
  fulfillment: 'Pickup' | 'Delivery' = 'Pickup';
  address: PublicDeliveryAddress = this.emptyAddress();
  readonly areas = signal<PublicDeliveryArea[]>([]);
  readonly loadingAreas = signal(false);
  readonly areasError = signal('');
  recoveryAcknowledged = false;

  constructor() {
    this.loadAreas();
  }

  private emptyAddress(): PublicDeliveryAddress {
    return {
      areaId: '',
      street: '',
      number: '',
      complement: null,
      postalCode: null,
      reference: null,
    };
  }

  loadAreas(): void {
    if (this.loadingAreas()) {
      return;
    }
    this.loadingAreas.set(true);
    this.areasError.set('');
    this.api
      .deliveryAreas()
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loadingAreas.set(false)),
      )
      .subscribe({
        next: (areas) => this.areas.set(areas),
        error: () =>
          this.areasError.set(
            'Não foi possível consultar as regiões atendidas. Tente novamente ou fale com o atendimento.',
          ),
      });
  }

  change(): void {
    this.review.set(null);
    this.reviewedInput = null;
    this.error.set('');
    this.attempted.set(false);
  }

  fieldError(field: string): string {
    if (!this.attempted()) {
      return '';
    }
    if (field === 'name' && !this.name.trim()) {
      return 'Informe seu nome.';
    }
    if (field === 'phone' && !validBrazilianPhone(this.phone)) {
      return 'Informe um telefone brasileiro válido com DDD.';
    }
    if (this.fulfillment !== 'Delivery') {
      return '';
    }
    if (field === 'area' && !this.areas().some((area) => area.id === this.address.areaId)) {
      return 'Selecione uma região atendida.';
    }
    if (field === 'street' && !this.address.street.trim()) {
      return 'Informe a rua ou avenida.';
    }
    if (field === 'number' && !this.address.number.trim()) {
      return 'Informe o número ou s/n.';
    }
    if (
      field === 'postal' &&
      this.address.postalCode?.trim() &&
      !/^[0-9]{5}-?[0-9]{3}$/.test(this.address.postalCode.trim())
    ) {
      return 'Use oito dígitos no CEP.';
    }
    return '';
  }

  private focusInvalidField(): void {
    const field = ['name', 'phone', 'area', 'street', 'number', 'postal'].find((field) =>
      this.fieldError(field),
    );
    if (field) {
      setTimeout(() => document.getElementById('checkout-' + field)?.focus());
    }
  }

  revise(): void {
    if (this.busy() || this.state.locked()) {
      return;
    }
    this.change();
    this.attempted.set(true);
    const validation = this.cart.validationError();
    if (
      validation ||
      !this.name.trim() ||
      this.name.length > 120 ||
      !validBrazilianPhone(this.phone)
    ) {
      this.error.set(validation ?? 'Informe seu nome e um telefone brasileiro válido com DDD.');
      this.focusInvalidField();
      return;
    }
    if (
      this.fulfillment === 'Delivery' &&
      (!this.areas().some((area) => area.id === this.address.areaId) ||
        !this.address.street.trim() ||
        !this.address.number.trim() ||
        (this.address.postalCode?.trim() &&
          !/^[0-9]{5}-?[0-9]{3}$/.test(this.address.postalCode.trim())))
    ) {
      this.error.set(
        'Selecione uma região atendida e informe rua e número (ou s/n). Se informar CEP, use oito dígitos.',
      );
      this.focusInvalidField();
      return;
    }
    const input: PublicCheckoutInput = {
      name: this.name,
      phone: this.phone,
      cart: this.cart.input(),
    };
    if (this.fulfillment === 'Delivery') {
      input.fulfillment = 'Delivery';
      input.address = { ...this.address };
    }
    this.busy.set(true);
    this.api
      .review(input)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: (result) => {
          this.reviewedInput = input;
          this.review.set(result);
        },
        error: (error: unknown) => {
          this.error.set(
            error instanceof HttpErrorResponse && error.error?.code === 'CartProductUnavailable'
              ? 'Um produto ficou indisponível. Volte ao carrinho e confira os itens.'
              : error instanceof HttpErrorResponse &&
                  error.error?.code === 'PublicDeliveryUnavailable'
                ? 'Essa região não está mais disponível. Atualize as regiões ou escolha retirada.'
                : error instanceof HttpErrorResponse && error.status === 400
                  ? 'Confira os dados de contato e endereço antes de revisar.'
                  : 'Não foi possível revisar. Confira sua conexão e tente novamente.',
          );
        },
      });
  }

  send(): void {
    if (this.busy() || this.state.receipt() || this.state.recoveryError()) {
      return;
    }
    this.error.set('');
    if (!this.state.pending()) {
      const review = this.review();
      if (!review || !this.reviewedInput) {
        return;
      }
      try {
        this.state.names.set(
          review.items.map((item) => ({ productId: item.productId, name: item.name })),
        );
        this.state.begin({
          requestId: crypto.randomUUID(),
          reviewToken: review.reviewToken,
          checkout: this.reviewedInput,
        });
      } catch {
        this.error.set(
          'Não foi possível guardar a tentativa nesta aba. Nenhum envio foi iniciado. Confira as permissões de armazenamento do navegador.',
        );
        return;
      }
    }
    this.busy.set(true);
    this.api
      .send(this.state.pending()!)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: (receipt) => {
          this.state.complete(receipt);
          this.cart.resetAfterCheckout();
          this.name = '';
          this.phone = '';
          this.address = this.emptyAddress();
          this.review.set(null);
          this.reviewedInput = null;
        },
        error: (error: unknown) => {
          const code = error instanceof HttpErrorResponse ? error.error?.code : null;
          if (
            error instanceof HttpErrorResponse &&
            error.status === 409 &&
            [
              'OrderReviewChanged',
              'CartProductUnavailable',
              'PublicCheckoutUnavailable',
              'PublicDeliveryUnavailable',
            ].includes(code)
          ) {
            const pending = this.state.pending()!;
            const names = this.state.names();
            try {
              this.state.clear();
              this.cart.restoreRejectedCheckout(pending.checkout.cart, names);
              this.name = pending.checkout.name;
              this.phone = pending.checkout.phone;
              this.fulfillment = pending.checkout.fulfillment ?? 'Pickup';
              this.address = pending.checkout.address
                ? { ...pending.checkout.address }
                : this.emptyAddress();
              this.change();
              this.error.set(
                code === 'PublicCheckoutUnavailable'
                  ? 'O pedido não foi enviado. Procure o atendimento para continuar.'
                  : code === 'PublicDeliveryUnavailable'
                    ? 'O pedido não foi enviado. A região não está disponível; atualize as regiões ou escolha retirada.'
                    : 'O pedido não foi enviado porque os dados mudaram. Confira o carrinho, endereço e taxa e faça uma nova revisão.',
              );
              return;
            } catch {
              // Preservar a tentativa se não for possível remover sua recuperação.
            }
          }
          this.error.set(
            'Ainda não conseguimos confirmar o resultado. Use “Conferir envio” para repetir a mesma tentativa, sem montar outro pedido.',
          );
        },
      });
  }

  newOrder(): void {
    if (this.busy() || (this.state.recoveryError() && !this.recoveryAcknowledged)) {
      return;
    }
    try {
      this.state.clear();
      this.cart.resetAfterCheckout();
      void this.router.navigateByUrl('/pedido');
    } catch {
      this.error.set(
        'Não foi possível limpar a recuperação nesta aba. Procure o atendimento antes de iniciar outro pedido.',
      );
    }
  }
}
