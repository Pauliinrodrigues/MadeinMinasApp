import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { timeout } from 'rxjs';
import { environment } from '../../../environments/environment';
import { IngredientUnit } from './ingredient-api.service';

export interface ProductCost {
  productId: string;
  productName: string;
  productIsActive: boolean;
  isAvailableForSale: boolean;
  salePrice: number;
  status: 'Ready' | 'MissingRecipe' | 'MissingCosts';
  recipeYield: number | null;
  recipeUpdatedAt: string | null;
  hasInactiveIngredients: boolean;
  knownRecipeCost: number;
  recipeCost: number | null;
  unitCost: number | null;
  cmvPercentage: number | null;
  grossMargin: number | null;
  grossMarginPercentage: number | null;
  calculatedAt: string;
  items: {
    ingredientId: string;
    ingredientName: string;
    unit: IngredientUnit;
    ingredientIsActive: boolean;
    quantity: number;
    unitCost: number;
    recipeCost: number;
  }[];
}

@Injectable({ providedIn: 'root' })
export class ProductCostApi {
  private readonly http = inject(HttpClient);
  get(productId: string) {
    return this.http
      .get<ProductCost>(
        environment.apiBaseUrl + '/products/' + encodeURIComponent(productId) + '/costing',
      )
      .pipe(timeout(15000));
  }
}
