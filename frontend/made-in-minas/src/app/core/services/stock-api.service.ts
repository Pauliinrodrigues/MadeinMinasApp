import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';
import { IngredientUnit } from './ingredient-api.service';

export type StockMovementType = 'Entry' | 'Exit' | 'Count';
export interface StockInput {
  requestId: string;
  expectedVersion: number;
  type: StockMovementType;
  quantity: number;
  reason: string;
}
export interface StockMovement {
  id: string;
  requestId: string;
  version: number;
  type: StockMovementType;
  quantity: number;
  delta: number;
  previousBalance: number;
  balance: number;
  reason: string;
  actorId: string;
  actorName: string;
  ingredientName: string;
  unit: IngredientUnit;
  createdAt: string;
}
export interface IngredientStock {
  ingredientId: string;
  name: string;
  unit: IngredientUnit;
  isActive: boolean;
  currentStock: number;
  minimumStock: number;
  isLowStock: boolean;
  version: number;
  movements: StockMovement[];
  page: number;
  pageSize: number;
  totalCount: number;
}

@Injectable({ providedIn: 'root' })
export class StockApi {
  private readonly http = inject(HttpClient);
  private url(id: string) {
    return environment.apiBaseUrl + '/ingredients/' + encodeURIComponent(id) + '/stock';
  }
  get(id: string, page = 1) {
    const params = new HttpParams().set('page', page).set('pageSize', 20);
    return this.http.get<IngredientStock>(this.url(id), { params });
  }
  create(id: string, input: StockInput) {
    return this.http.post<StockMovement>(this.url(id) + '/movements', input);
  }
}
