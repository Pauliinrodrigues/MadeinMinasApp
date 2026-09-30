import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { catchError, finalize, forkJoin, of, throwError } from 'rxjs';
import { apiError } from '../../core/api-error';
import { Ingredient, IngredientApi, unitLabel } from '../../core/services/ingredient-api.service';
import { Product, ProductApi } from '../../core/services/product-api.service';
import { Recipe, RecipeApi } from '../../core/services/recipe-api.service';

interface RecipeRow {
  key: number;
  ingredientId: string;
  quantity: string;
}

@Component({
  selector: 'app-recipe',
  imports: [FormsModule, RouterLink],
  templateUrl: './recipe.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RecipePage {
  private readonly api = inject(RecipeApi);
  private readonly ingredientApi = inject(IngredientApi);
  private readonly productApi = inject(ProductApi);
  private readonly destroyRef = inject(DestroyRef);
  readonly productId = inject(ActivatedRoute).snapshot.paramMap.get('id')!;
  readonly product = signal<Product | null>(null);
  readonly ingredients = signal<Ingredient[]>([]);
  readonly loading = signal(false);
  readonly ready = signal(false);
  readonly busy = signal(false);
  readonly exists = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  readonly unitLabel = unitLabel;
  private originalIds = new Set<string>();
  private nextKey = 0;
  yieldQuantity: number | null = 1;
  instructions = '';
  rows: RecipeRow[] = [];

  constructor() {
    this.load();
  }

  load(): void {
    if (this.loading() || this.busy()) {
      return;
    }
    this.loading.set(true);
    this.ready.set(false);
    this.error.set('');
    forkJoin({
      product: this.productApi.get(this.productId),
      ingredients: this.ingredientApi.allForSelection(),
      recipe: this.api
        .get(this.productId)
        .pipe(
          catchError((error) =>
            error instanceof HttpErrorResponse &&
            error.status === 404 &&
            error.error?.code === 'RecipeNotFound'
              ? of(null)
              : throwError(() => error),
          ),
        ),
    })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: ({ product, ingredients, recipe }) => {
          this.product.set(product);
          this.ingredients.set(ingredients);
          this.assign(recipe);
          this.ready.set(true);
        },
        error: (error) => this.error.set(apiError(error)),
      });
  }

  private assign(recipe: Recipe | null): void {
    this.exists.set(!!recipe);
    this.originalIds = new Set(recipe?.items.map((item) => item.ingredientId) ?? []);
    this.yieldQuantity = recipe?.yieldQuantity ?? 1;
    this.instructions = recipe?.instructions ?? '';
    // Exibe os vínculos com os dados retornados junto da ficha, sem mutar o catálogo carregado.
    if (recipe) {
      const savedItems = new Map(recipe.items.map((item) => [item.ingredientId, item]));
      this.ingredients.update((ingredients) =>
        ingredients.map((ingredient) => {
          const saved = savedItems.get(ingredient.id);
          return saved
            ? {
                ...ingredient,
                name: saved.ingredientName,
                unit: saved.unit,
                isActive: saved.ingredientIsActive,
              }
            : ingredient;
        }),
      );
    }
    this.rows =
      recipe?.items.map((item) => ({
        key: this.nextKey++,
        ingredientId: item.ingredientId,
        quantity: String(item.quantity).replace('.', ','),
      })) ?? [];
    if (!this.rows.length) {
      this.addRow();
    }
  }

  addRow(): void {
    if (!this.busy() && this.rows.length < 100) {
      this.rows.push({ key: this.nextKey++, ingredientId: '', quantity: '' });
    }
  }
  removeRow(row: RecipeRow): void {
    if (!this.busy()) {
      this.rows = this.rows.filter((item) => item !== row);
    }
  }
  selected(row: RecipeRow): Ingredient | undefined {
    return this.ingredients().find((item) => item.id === row.ingredientId);
  }
  canSelect(ingredient: Ingredient): boolean {
    return ingredient.isActive || this.originalIds.has(ingredient.id);
  }
  duplicate(row: RecipeRow): boolean {
    return (
      !!row.ingredientId &&
      this.rows.filter((item) => item.ingredientId === row.ingredientId).length > 1
    );
  }
  quantityValue(row: RecipeRow): number | null {
    const value = row.quantity.trim().replace(',', '.');
    if (!/^\d{1,6}(\.\d{1,3})?$/.test(value)) {
      return null;
    }
    const number = Number(value);
    return number >= 0.001 && number <= 999999.999 ? number : null;
  }
  validYield(): boolean {
    return (
      this.yieldQuantity !== null &&
      Number.isInteger(this.yieldQuantity) &&
      this.yieldQuantity >= 1 &&
      this.yieldQuantity <= 10000
    );
  }
  valid(): boolean {
    return (
      this.ready() &&
      this.validYield() &&
      this.instructions.length <= 2000 &&
      this.rows.length >= 1 &&
      this.rows.length <= 100 &&
      this.rows.every((row) => {
        const item = this.selected(row);
        return (
          !!item && this.canSelect(item) && !this.duplicate(row) && this.quantityValue(row) !== null
        );
      })
    );
  }
  save(): void {
    if (this.busy() || !this.valid()) {
      return;
    }
    this.busy.set(true);
    this.error.set('');
    this.notice.set('');
    this.api
      .save(this.productId, {
        yieldQuantity: this.yieldQuantity!,
        instructions: this.instructions.trim() || null,
        items: this.rows.map((row) => ({
          ingredientId: row.ingredientId,
          quantity: this.quantityValue(row)!,
        })),
      })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: (recipe) => {
          this.assign(recipe);
          this.notice.set('Ficha técnica salva.');
        },
        error: (error) => this.error.set(apiError(error)),
      });
  }
}
