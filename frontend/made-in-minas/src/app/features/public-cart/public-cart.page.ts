import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { IonContent } from '@ionic/angular/ion-content';
import { finalize } from 'rxjs';
import { PublicCartApi, PublicCartQuote } from '../../core/services/public-cart-api.service';
import { PublicCartState } from '../../core/services/public-cart-state.service';
import { formatProductPrice } from '../../core/services/product-api.service';

@Component({
  selector: 'app-public-cart',
  host: { class: 'ion-page' },
  imports: [FormsModule, RouterLink, IonContent],
  templateUrl: './public-cart.page.html',
  styleUrl: './public-cart.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PublicCartPage {
  readonly cart = inject(PublicCartState);
  private readonly api = inject(PublicCartApi);
  private readonly destroyRef = inject(DestroyRef);
  readonly quote = signal<PublicCartQuote | null>(null);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly confirmClear = signal(false);
  readonly price = formatProductPrice;

  private invalidate(): void {
    this.quote.set(null);
    this.error.set('');
    this.confirmClear.set(false);
  }

  changeQuantity(key: number, value: number | null): void {
    if (this.busy()) {
      return;
    }
    this.invalidate();
    this.cart.setQuantity(key, value);
  }

  changeLineNotes(key: number, value: string): void {
    if (this.busy()) {
      return;
    }
    this.invalidate();
    this.cart.setLineNotes(key, value);
  }

  changeNotes(value: string): void {
    if (this.busy()) {
      return;
    }
    this.invalidate();
    this.cart.setNotes(value);
  }

  remove(key: number): void {
    if (this.busy()) {
      return;
    }
    this.invalidate();
    this.cart.remove(key);
  }

  clear(): void {
    if (this.busy() || !this.confirmClear()) {
      return;
    }
    this.invalidate();
    this.cart.clear();
  }

  review(): void {
    if (this.busy()) {
      return;
    }
    this.invalidate();
    const validation = this.cart.validationError();
    if (validation) {
      this.error.set(validation);
      return;
    }
    this.busy.set(true);
    this.api
      .quote(this.cart.input())
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: (result) => {
          this.cart.refreshNames(result);
          this.quote.set(result);
        },
        error: (error: unknown) => {
          const unavailable =
            error instanceof HttpErrorResponse &&
            error.status === 409 &&
            error.error?.code === 'CartProductUnavailable';
          this.error.set(
            unavailable
              ? 'Um produto ficou indisponível. Confira o cardápio, remova os itens indisponíveis e revise novamente.'
              : 'Não foi possível revisar o carrinho. Seus itens foram mantidos; confira os dados e tente novamente.',
          );
        },
      });
  }
}
