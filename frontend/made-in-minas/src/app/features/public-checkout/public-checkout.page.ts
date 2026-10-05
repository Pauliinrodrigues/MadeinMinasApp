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
  readonly price = formatProductPrice;
  private reviewedInput: PublicCheckoutInput | null = null;
  name = '';
  phone = '';
  recoveryAcknowledged = false;

  change(): void {
    this.review.set(null);
    this.reviewedInput = null;
    this.error.set('');
  }

  revise(): void {
    if (this.busy() || this.state.locked()) {
      return;
    }
    this.change();
    const validation = this.cart.validationError();
    if (
      validation ||
      !this.name.trim() ||
      this.name.length > 120 ||
      !validBrazilianPhone(this.phone)
    ) {
      this.error.set(validation ?? 'Informe seu nome e um telefone brasileiro válido com DDD.');
      return;
    }
    const input = { name: this.name, phone: this.phone, cart: this.cart.input() };
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
          this.review.set(null);
          this.reviewedInput = null;
        },
        error: (error: unknown) => {
          const code = error instanceof HttpErrorResponse ? error.error?.code : null;
          if (
            error instanceof HttpErrorResponse &&
            error.status === 409 &&
            ['OrderReviewChanged', 'CartProductUnavailable', 'PublicCheckoutUnavailable'].includes(
              code,
            )
          ) {
            const pending = this.state.pending()!;
            const names = this.state.names();
            try {
              this.state.clear();
              this.cart.restoreRejectedCheckout(pending.checkout.cart, names);
              this.name = pending.checkout.name;
              this.phone = pending.checkout.phone;
              this.change();
              this.error.set(
                code === 'PublicCheckoutUnavailable'
                  ? 'O pedido não foi enviado. Procure o atendimento para continuar.'
                  : 'O pedido não foi enviado porque os dados mudaram. Confira o carrinho e faça uma nova revisão.',
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
