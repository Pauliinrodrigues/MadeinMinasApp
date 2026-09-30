import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';
import { IngredientUnit } from './ingredient-api.service';

export interface RecipeItemInput {
  ingredientId: string;
  quantity: number;
}
export interface RecipeInput {
  yieldQuantity: number;
  instructions: string | null;
  items: RecipeItemInput[];
}
export interface RecipeItem extends RecipeItemInput {
  ingredientName: string;
  unit: IngredientUnit;
  ingredientIsActive: boolean;
}
export interface Recipe {
  id: string;
  productId: string;
  yieldQuantity: number;
  instructions: string | null;
  createdAt: string;
  updatedAt: string;
  hasInactiveIngredients: boolean;
  items: RecipeItem[];
}

@Injectable({ providedIn: 'root' })
export class RecipeApi {
  private readonly http = inject(HttpClient);
  private url(productId: string): string {
    return environment.apiBaseUrl + '/products/' + encodeURIComponent(productId) + '/recipe';
  }
  get(productId: string) {
    return this.http.get<Recipe>(this.url(productId));
  }
  save(productId: string, input: RecipeInput) {
    return this.http.put<Recipe>(this.url(productId), input);
  }
}
