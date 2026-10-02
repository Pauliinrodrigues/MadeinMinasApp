import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { apiError } from '../../core/api-error';
import { unitLabel } from '../../core/services/ingredient-api.service';
import { ProductCost, ProductCostApi } from '../../core/services/product-cost-api.service';
import { formatProductPrice } from '../../core/services/product-api.service';

@Component({
  selector: 'app-product-cost',
  imports: [RouterLink, DatePipe],
  templateUrl: './product-cost.page.html',
  styles: '.panel { overflow-wrap: anywhere; } dd { margin: 0 0 12px; font-weight: 600; }',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProductCostPage {
  private readonly api = inject(ProductCostApi);
  private readonly destroyRef = inject(DestroyRef);
  readonly productId = inject(ActivatedRoute).snapshot.paramMap.get('id')!;
  readonly data = signal<ProductCost | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly price = formatProductPrice;
  readonly unitLabel = unitLabel;
  readonly quantity = new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 3 });
  readonly currency = new Intl.NumberFormat('pt-BR', {
    style: 'currency',
    currency: 'BRL',
    maximumFractionDigits: 6,
  });
  readonly percentage = new Intl.NumberFormat('pt-BR', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  });

  constructor() {
    this.load();
  }

  formatCost(value: number): string {
    if (value !== 0 && Math.abs(value) < 0.000001) {
      return value > 0 ? '< R$ 0,000001' : '> -R$ 0,000001';
    }
    return this.currency.format(value);
  }

  load(): void {
    if (this.loading()) {
      return;
    }
    this.loading.set(true);
    this.data.set(null);
    this.error.set('');
    this.api
      .get(this.productId)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: (data) => this.data.set(data),
        error: (error) => this.error.set(apiError(error)),
      });
  }
}
