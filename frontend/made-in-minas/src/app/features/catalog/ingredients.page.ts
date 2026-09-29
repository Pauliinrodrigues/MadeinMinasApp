import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { finalize, Subscription } from 'rxjs';
import { apiError } from '../../core/api-error';
import { Ingredient, IngredientApi, IngredientPage, unitLabel } from '../../core/services/ingredient-api.service';

@Component({
  selector: 'app-ingredients', imports: [FormsModule, RouterLink],
  templateUrl: './ingredients.page.html', changeDetection: ChangeDetectionStrategy.OnPush,
})
export class IngredientsPage {
  private readonly api = inject(IngredientApi);
  private readonly destroyRef = inject(DestroyRef);
  private request?: Subscription;
  readonly unitLabel = unitLabel;
  readonly decimal = new Intl.NumberFormat("pt-BR", { maximumFractionDigits: 4 });
  readonly result = signal<IngredientPage | null>(null);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  readonly pending = signal<Ingredient | null>(null);
  search = '';
  active = '';
  page = 1;

  constructor() { this.load(); }

  load(page = 1): void {
    this.request?.unsubscribe();
    this.page = page;
    this.loading.set(true);
    this.error.set('');
    this.result.set(null);
    this.request = this.api.list(page, this.search, this.active).pipe(
      takeUntilDestroyed(this.destroyRef), finalize(() => this.loading.set(false)),
    ).subscribe({
      next: result => this.result.set(result), error: error => this.error.set(apiError(error)),
    });
  }

  requestStatus(ingredient: Ingredient): void {
    this.pending.set(ingredient);
    this.error.set('');
    this.notice.set('');
  }

  changeStatus(): void {
    const ingredient = this.pending();
    if (!ingredient || this.saving()) return;
    this.saving.set(true);
    this.api.status(ingredient.id, !ingredient.isActive).pipe(
      takeUntilDestroyed(this.destroyRef), finalize(() => this.saving.set(false)),
    ).subscribe({
      next: () => {
        this.pending.set(null);
        this.notice.set(ingredient.isActive ? 'Ingrediente inativado.' : 'Ingrediente ativado.');
        this.load(this.page);
      },
      error: error => { this.pending.set(null); this.error.set(apiError(error)); },
    });
  }
}
