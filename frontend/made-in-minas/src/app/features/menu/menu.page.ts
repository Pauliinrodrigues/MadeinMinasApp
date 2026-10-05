import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { IonContent } from '@ionic/angular/ion-content';
import { finalize } from 'rxjs';
import { MenuApi, MenuProduct, PublicMenu } from '../../core/services/menu-api.service';
import { PublicCartState } from '../../core/services/public-cart-state.service';
import { formatProductPrice } from '../../core/services/product-api.service';

@Component({
  selector: 'app-menu',
  host: { class: 'ion-page' },
  imports: [FormsModule, RouterLink, IonContent],
  templateUrl: './menu.page.html',
  styleUrl: './menu.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MenuPage {
  readonly cart = inject(PublicCartState);
  readonly cartNotice = signal('');
  private readonly api = inject(MenuApi);
  private readonly destroyRef = inject(DestroyRef);
  readonly data = signal<PublicMenu | null>(null);
  readonly categories = signal<PublicMenu['categories']>([]);
  readonly loading = signal(false);
  readonly error = signal(false);
  readonly failedImages = signal<Set<string>>(new Set());
  readonly loadedImages = signal<Set<string>>(new Set());
  readonly price = formatProductPrice;
  categoryId = '';
  page = 1;

  constructor() {
    this.load();
  }

  addToCart(product: MenuProduct): void {
    this.cartNotice.set(this.cart.add(product) ?? `${product.name} adicionado ao carrinho.`);
  }

  categoryMissing(): boolean {
    return (
      !!this.categoryId && !this.categories().some((category) => category.id === this.categoryId)
    );
  }

  categoryName(id: string): string {
    return this.categories().find((category) => category.id === id)?.name ?? '';
  }

  selectCategory(): void {
    this.page = 1;
    this.load();
  }

  allCategories(): void {
    this.categoryId = '';
    this.selectCategory();
  }

  changePage(page: number): void {
    if (this.loading()) {
      return;
    }
    this.page = page;
    this.load();
  }

  imageFailed(id: string): void {
    this.failedImages.update((current) => new Set([...current, id]));
  }

  imageLoaded(id: string): void {
    this.loadedImages.update((current) => new Set([...current, id]));
  }

  load(): void {
    if (this.loading()) {
      return;
    }
    this.loading.set(true);
    this.error.set(false);
    this.data.set(null);
    this.failedImages.set(new Set());
    this.loadedImages.set(new Set());
    this.api
      .get(this.categoryId, this.page)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: (menu) => {
          this.categories.set(menu.categories);
          this.data.set(menu);
        },
        error: () => this.error.set(true),
      });
  }
}
