import { Injectable, signal } from '@angular/core';
import { PublicOrderReceipt } from './public-checkout-api.service';

const storageKey = 'made-in-minas.public-order-history.v1';
const limit = 20;

@Injectable({ providedIn: 'root' })
export class PublicOrderHistory {
  readonly entries = signal<PublicOrderReceipt[]>([]);
  readonly remembered = signal(false);
  readonly warning = signal('');
  readonly selectedNumber = signal<number | null>(null);

  constructor() {
    let tab: PublicOrderReceipt[] = [];
    let device: PublicOrderReceipt[] = [];
    let incomplete = false;
    try {
      tab = this.read(sessionStorage.getItem(storageKey));
    } catch {
      incomplete = true;
    }
    try {
      const saved = localStorage.getItem(storageKey);
      device = this.read(saved);
      this.remembered.set(saved !== null);
    } catch {
      incomplete = true;
    }
    this.entries.set(this.merge(tab, device));
    if (incomplete) {
      this.warning.set('Não foi possível recuperar todos os acompanhamentos neste navegador.');
    } else {
      this.persist();
    }
  }

  add(receipt: PublicOrderReceipt): void {
    this.entries.set(this.merge([receipt], this.entries()));
    this.selectedNumber.set(receipt.number);
    this.persist();
  }

  remember(value: boolean): void {
    try {
      if (value) {
        localStorage.setItem(storageKey, JSON.stringify(this.entries()));
      } else {
        localStorage.removeItem(storageKey);
      }
      this.remembered.set(value);
      this.warning.set('');
    } catch {
      this.warning.set(
        'Não foi possível alterar o armazenamento neste dispositivo. Tente novamente.',
      );
    }
  }

  forget(number: number): void {
    this.entries.set(this.entries().filter((entry) => entry.number !== number));
    if (this.selectedNumber() === number) {
      this.selectedNumber.set(null);
    }
    this.persist();
  }

  private read(stored: string | null): PublicOrderReceipt[] {
    if (!stored) {
      return [];
    }
    if (stored.length > 65536) {
      throw new Error('Invalid tracking history');
    }
    const value: unknown = JSON.parse(stored);
    if (!Array.isArray(value)) {
      throw new Error('Invalid tracking history');
    }
    return value;
  }

  private merge(...sources: PublicOrderReceipt[][]): PublicOrderReceipt[] {
    const entries = new Map<number, PublicOrderReceipt>();
    for (const entry of sources.flat()) {
      if (
        !entry ||
        !Number.isSafeInteger(entry.number) ||
        entry.number <= 0 ||
        !Number.isFinite(entry.total) ||
        entry.total < 0 ||
        !['Pickup', 'Delivery'].includes(entry.fulfillment) ||
        typeof entry.createdAt !== 'string' ||
        !Number.isFinite(Date.parse(entry.createdAt)) ||
        typeof entry.tracking?.token !== 'string' ||
        !entry.tracking.token.length ||
        entry.tracking.token.length > 2048 ||
        typeof entry.tracking.expiresAt !== 'string' ||
        !(Date.parse(entry.tracking.expiresAt) > Date.now()) ||
        entries.has(entry.number)
      ) {
        continue;
      }
      // Guardar apenas o comprovante mínimo, nunca contato, endereço ou itens do checkout.
      entries.set(entry.number, {
        number: entry.number,
        fulfillment: entry.fulfillment,
        total: entry.total,
        createdAt: entry.createdAt,
        tracking: { token: entry.tracking.token, expiresAt: entry.tracking.expiresAt },
      });
    }
    return [...entries.values()]
      .sort((a, b) => Date.parse(b.createdAt) - Date.parse(a.createdAt))
      .slice(0, limit);
  }

  private persist(): void {
    try {
      const stored = JSON.stringify(this.entries());
      sessionStorage.setItem(storageKey, stored);
      if (this.remembered()) {
        localStorage.setItem(storageKey, stored);
      }
      this.warning.set('');
    } catch {
      this.warning.set(
        'O histórico está disponível nesta página, mas não foi salvo. Anote os números dos pedidos.',
      );
    }
  }
}
