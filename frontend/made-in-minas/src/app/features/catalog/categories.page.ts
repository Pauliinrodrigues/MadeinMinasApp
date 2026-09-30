import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { finalize, Subscription } from 'rxjs';
import { apiError } from '../../core/api-error';
import { Category, CategoryApi, CategoryPage } from '../../core/services/category-api.service';

@Component({
  selector: 'app-categories',
  imports: [FormsModule, RouterLink],
  templateUrl: './categories.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CategoriesPage {
  private readonly api = inject(CategoryApi);
  private readonly destroyRef = inject(DestroyRef);
  private request?: Subscription;
  readonly result = signal<CategoryPage | null>(null);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  readonly pending = signal<Category | null>(null);
  search = '';
  active = '';
  page = 1;

  constructor() {
    this.load();
  }

  load(page = 1): void {
    this.request?.unsubscribe();
    this.page = page;
    this.loading.set(true);
    this.error.set('');
    this.result.set(null);
    this.request = this.api
      .list(page, this.search, this.active)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: (result) => this.result.set(result),
        error: (error) => this.error.set(apiError(error)),
      });
  }

  requestStatus(category: Category): void {
    this.pending.set(category);
    this.error.set('');
    this.notice.set('');
  }

  changeStatus(): void {
    const category = this.pending();
    if (!category || this.saving()) {
      return;
    }
    this.saving.set(true);
    this.api
      .status(category.id, !category.isActive)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.saving.set(false)),
      )
      .subscribe({
        next: () => {
          this.pending.set(null);
          this.notice.set(category.isActive ? 'Categoria inativada.' : 'Categoria ativada.');
          this.load(this.page);
        },
        error: (error) => {
          this.pending.set(null);
          this.error.set(apiError(error));
        },
      });
  }
}
