import { Injectable, computed, inject, signal } from '@angular/core';
import type { MenuProduct } from './menu-api.service';
import type { PublicCartInput, PublicCartQuote } from './public-cart-api.service';
import { PublicCheckoutState } from './public-checkout-state.service';

interface PublicCartLine {
  key: number;
  productId: string;
  name: string;
  quantity: number | null;
  notes: string;
}

@Injectable({ providedIn: 'root' })
export class PublicCartState {
  readonly checkout = inject(PublicCheckoutState);
  private nextKey = 1;
  private readonly entries = signal<PublicCartLine[]>([]);
  private readonly generalNotes = signal('');
  readonly lines = this.entries.asReadonly();
  readonly notes = this.generalNotes.asReadonly();
  readonly itemCount = computed(() =>
    this.lines().reduce(
      (total, line) =>
        total + (Number.isInteger(line.quantity) && (line.quantity ?? 0) > 0 ? line.quantity! : 0),
      0,
    ),
  );

  add(product: MenuProduct): string | null {
    if (this.checkout.locked()) {
      return 'Confira o envio anterior antes de montar outro pedido.';
    }
    if (!product.isAvailable) {
      return 'Este produto está indisponível.';
    }
    if (this.lines().length && this.validationError()) {
      return 'Confira as quantidades e observações no carrinho antes de adicionar mais produtos.';
    }
    const quantity = this.lines()
      .filter((line) => line.productId === product.id)
      .reduce((total, line) => total + (line.quantity ?? 0), 0);
    if (quantity >= 99) {
      return 'O limite é de 99 unidades por produto.';
    }
    const existing = this.lines().find(
      (line) => line.productId === product.id && !line.notes.trim(),
    );
    if (existing) {
      this.setQuantity(existing.key, (existing.quantity ?? 0) + 1);
    } else {
      if (this.lines().length >= 50) {
        return 'O limite é de 50 itens no carrinho.';
      }
      this.entries.update((lines) => [
        ...lines,
        { key: this.nextKey++, productId: product.id, name: product.name, quantity: 1, notes: '' },
      ]);
    }
    return null;
  }

  setQuantity(key: number, quantity: number | null): void {
    if (this.checkout.locked()) {
      return;
    }
    this.entries.update((lines) =>
      lines.map((line) => (line.key === key ? { ...line, quantity } : line)),
    );
  }

  setLineNotes(key: number, notes: string): void {
    if (this.checkout.locked()) {
      return;
    }
    this.entries.update((lines) =>
      lines.map((line) => (line.key === key ? { ...line, notes } : line)),
    );
  }

  setNotes(notes: string): void {
    if (this.checkout.locked()) {
      return;
    }
    this.generalNotes.set(notes);
  }

  remove(key: number): void {
    if (this.checkout.locked()) {
      return;
    }
    this.entries.update((lines) => lines.filter((line) => line.key !== key));
    if (!this.lines().length) {
      this.generalNotes.set('');
    }
  }

  clear(): void {
    if (this.checkout.locked()) {
      return;
    }
    this.resetAfterCheckout();
  }

  resetAfterCheckout(): void {
    this.entries.set([]);
    this.generalNotes.set('');
  }

  refreshNames(quote: PublicCartQuote): void {
    this.entries.update((lines) =>
      lines.map((line) => ({
        ...line,
        name: quote.items.find((item) => item.productId === line.productId)?.name ?? line.name,
      })),
    );
  }

  restoreRejectedCheckout(
    input: PublicCartInput,
    names: { productId: string; name: string }[],
  ): void {
    this.entries.set(
      input.items.map((item) => ({
        key: this.nextKey++,
        productId: item.productId,
        quantity: item.quantity,
        notes: item.notes ?? '',
        name:
          names.find((entry) => entry.productId === item.productId)?.name ?? 'Produto selecionado',
      })),
    );
    this.generalNotes.set(input.notes ?? '');
  }

  validationError(): string | null {
    if (!this.lines().length) {
      return 'Adicione pelo menos um produto.';
    }
    if (this.lines().length > 50) {
      return 'O limite é de 50 itens no carrinho.';
    }
    if (
      this.lines().some(
        (line) =>
          !Number.isInteger(line.quantity) || (line.quantity ?? 0) < 1 || (line.quantity ?? 0) > 99,
      )
    ) {
      return 'Informe quantidades inteiras de 1 a 99.';
    }
    if (this.notes().length > 500 || this.lines().some((line) => line.notes.length > 250)) {
      return 'Use até 250 caracteres por item e 500 nas observações gerais.';
    }
    const totals = new Map<string, number>();
    for (const line of this.lines()) {
      totals.set(line.productId, (totals.get(line.productId) ?? 0) + line.quantity!);
    }
    return [...totals.values()].some((quantity) => quantity > 99)
      ? 'O limite é de 99 unidades por produto, somando todos os itens.'
      : null;
  }

  input(): PublicCartInput {
    return {
      items: this.lines().map((line) => ({
        productId: line.productId,
        quantity: line.quantity!,
        notes: line.notes || null,
      })),
      notes: this.notes() || null,
    };
  }
}
