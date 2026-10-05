import { Injectable, computed, signal } from '@angular/core';
import { PublicOrderInput, PublicOrderReceipt } from './public-checkout-api.service';

const storageKey = 'made-in-minas.public-checkout.v1';

@Injectable({ providedIn: 'root' })
export class PublicCheckoutState {
  readonly pending = signal<PublicOrderInput | null>(null);
  readonly names = signal<{ productId: string; name: string }[]>([]);
  readonly receipt = signal<PublicOrderReceipt | null>(null);
  readonly recoveryError = signal(false);
  readonly locked = computed(() => !!this.pending() || !!this.receipt() || this.recoveryError());

  constructor() {
    try {
      const stored = sessionStorage.getItem(storageKey);
      if (!stored) {
        return;
      }
      if (stored.length > 65536) {
        throw new Error('Invalid checkout recovery');
      }
      const value = JSON.parse(stored);
      if (value.version !== 1) {
        throw new Error('Invalid checkout recovery');
      }
      if (
        value.pending &&
        typeof value.pending.requestId === 'string' &&
        value.pending.checkout?.cart?.items?.length
      ) {
        this.pending.set(value.pending);
        this.names.set(Array.isArray(value.names) ? value.names : []);
      } else if (
        value.receipt &&
        Number.isInteger(value.receipt.number) &&
        typeof value.receipt.total === 'number'
      ) {
        this.receipt.set(value.receipt);
      } else {
        throw new Error('Invalid checkout recovery');
      }
    } catch {
      this.recoveryError.set(true);
    }
  }

  begin(input: PublicOrderInput): void {
    // Persistir antes do HTTP permite repetir a mesma tentativa após recarregar a aba.
    sessionStorage.setItem(
      storageKey,
      JSON.stringify({ version: 1, pending: input, names: this.names() }),
    );
    this.pending.set(input);
  }

  complete(receipt: PublicOrderReceipt): void {
    this.receipt.set(receipt);
    this.pending.set(null);
    this.names.set([]);
    try {
      // O comprovante substitui os dados pessoais do envio assim que a resposta chega.
      sessionStorage.setItem(storageKey, JSON.stringify({ version: 1, receipt }));
    } catch {
      // Se a gravação falhar, a tentativa anterior ainda pode ser repetida sem duplicar.
    }
  }

  clear(): void {
    sessionStorage.removeItem(storageKey);
    this.pending.set(null);
    this.receipt.set(null);
    this.names.set([]);
    this.recoveryError.set(false);
  }
}
