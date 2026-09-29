import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { finalize, Subscription } from 'rxjs';
import { apiError } from '../../core/api-error';
import { Category, CategoryApi } from '../../core/services/category-api.service';
import { Product, ProductApi, ProductPage, formatProductPrice } from '../../core/services/product-api.service';

@Component({
  selector: 'app-products', imports: [FormsModule, RouterLink],
  templateUrl: './products.page.html', changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProductsPage {
  private readonly api = inject(ProductApi);
  private readonly categoryApi = inject(CategoryApi);
  private readonly destroyRef = inject(DestroyRef);
  private request?: Subscription;
  readonly result = signal<ProductPage | null>(null);
  readonly categories = signal<Category[]>([]);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = signal('');
  readonly categoryError = signal('');
  readonly notice = signal('');
  readonly pending = signal<{ product: Product; kind: 'status' | 'availability'; value: boolean } | null>(null);
  readonly formatPrice = formatProductPrice;
  search = '';
  categoryId = '';
  active = '';
  available = '';
  page = 1;

  constructor() { this.loadCategories(); this.load(); }

  loadCategories(): void {
    this.categoryError.set('');
    this.categoryApi.allForSelection().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: categories => this.categories.set(categories), error: error => this.categoryError.set(apiError(error)),
    });
  }

  load(page = 1): void {
    this.request?.unsubscribe();
    this.page = page;
    this.loading.set(true);
    this.error.set('');
    this.result.set(null);
    this.request = this.api.list(page, this.search, this.categoryId, this.active, this.available).pipe(
      takeUntilDestroyed(this.destroyRef), finalize(() => this.loading.set(false)),
    ).subscribe({
      next: result => this.result.set(result), error: error => this.error.set(apiError(error)),
    });
  }

  requestChange(product: Product, kind: 'status' | 'availability'): void {
    this.pending.set({ product, kind, value: kind === 'status' ? !product.isActive : !product.isAvailable });
    this.error.set('');
    this.notice.set('');
  }

  confirm(): void {
    const change = this.pending();
    if (!change || this.saving()) return;
    this.saving.set(true);
    const request = change.kind === 'status' ? this.api.status(change.product.id, change.value)
      : this.api.availability(change.product.id, change.value);
    request.pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.saving.set(false))).subscribe({
      next: product => {
        this.pending.set(null);
        this.notice.set(product.isAvailableForSale ? 'Produto disponível para venda.' : 'Alteração salva. Produto indisponível para venda.');
        this.load(this.page);
      },
      error: error => { this.pending.set(null); this.error.set(apiError(error)); },
    });
  }
}

