import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { apiError } from '../../core/api-error';
import { unitLabel } from '../../core/services/ingredient-api.service';
import {
  IngredientStock,
  StockApi,
  StockInput,
  StockMovementType,
} from '../../core/services/stock-api.service';

@Component({
  selector: 'app-stock',
  imports: [FormsModule, RouterLink],
  templateUrl: './stock.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StockPage {
  private readonly api = inject(StockApi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id')!;
  readonly data = signal<IngredientStock | null>(null);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly needsRefresh = signal(false);
  readonly pending = signal<StockInput | null>(null);
  readonly uncertain = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  readonly decimal = new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 3 });
  readonly date = new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeStyle: 'medium' });
  readonly unitLabel = unitLabel;
  readonly labels: Record<StockMovementType, string> = {
    Entry: 'Entrada',
    Exit: 'Saída',
    Count: 'Contagem física',
  };
  type: StockMovementType = 'Entry';
  quantity = '';
  reason = '';

  constructor() {
    this.load();
  }

  load(page = 1): void {
    if (this.loading() || this.saving() || this.pending()) {
      return;
    }
    this.loading.set(true);
    this.needsRefresh.set(true);
    this.error.set('');
    this.api
      .get(this.id, page)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: (data) => {
          this.data.set(data);
          this.needsRefresh.set(false);
        },
        error: (error) => this.error.set(apiError(error)),
      });
  }

  value(): number | null {
    const text = this.quantity.trim().replace(',', '.');
    if (!/^\d{1,6}(\.\d{1,3})?$/.test(text)) {
      return null;
    }
    const value = Number(text);
    return value <= 999999.999 && (value > 0 || this.type === 'Count') ? value : null;
  }

  projected(input: Pick<StockInput, 'type' | 'quantity'>): number {
    const current = this.data()?.currentStock ?? 0;
    const result =
      input.type === 'Count'
        ? input.quantity
        : current + (input.type === 'Entry' ? input.quantity : -input.quantity);
    return Math.round(result * 1000) / 1000;
  }

  canReview(): boolean {
    const data = this.data();
    const value = this.value();
    if (
      !data?.isActive ||
      this.loading() ||
      this.saving() ||
      this.pending() ||
      this.needsRefresh() ||
      value === null ||
      !this.reason.trim() ||
      this.reason.length > 500
    ) {
      return false;
    }
    const balance = this.projected({ type: this.type, quantity: value });
    return balance >= 0 && balance <= 999999.999 && balance !== data.currentStock;
  }

  review(): void {
    if (!this.canReview()) {
      return;
    }
    this.error.set('');
    this.notice.set('');
    this.pending.set({
      requestId: crypto.randomUUID(),
      expectedVersion: this.data()!.version,
      type: this.type,
      quantity: this.value()!,
      reason: this.reason.trim(),
    });
  }

  cancel(): void {
    if (!this.saving() && !this.uncertain()) {
      this.pending.set(null);
    }
  }

  save(): void {
    const input = this.pending();
    if (!input || this.saving()) {
      return;
    }
    this.saving.set(true);
    this.error.set('');
    this.api
      .create(this.id, input)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.saving.set(false)),
      )
      .subscribe({
        next: () => {
          this.pending.set(null);
          this.uncertain.set(false);
          this.quantity = '';
          this.reason = '';
          this.notice.set('Movimentação registrada.');
          this.saving.set(false);
          this.load();
        },
        error: (error: unknown) => {
          this.error.set(apiError(error));
          if (error instanceof HttpErrorResponse && error.status >= 400 && error.status < 500) {
            this.pending.set(null);
            this.uncertain.set(false);
            this.needsRefresh.set(true);
          } else {
            // Repete a mesma intenção após falha de rede, sem criar outro lançamento.
            this.uncertain.set(true);
          }
        },
      });
  }

  formatDate(value: string): string {
    return this.date.format(new Date(value));
  }
}
