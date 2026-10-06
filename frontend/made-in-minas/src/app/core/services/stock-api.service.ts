import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';
import { IngredientUnit } from './ingredient-api.service';

export type StockMovementType = 'Entry' | 'Exit' | 'Count';
export type StockReplenishmentStatus =
  'Attention' | 'OutOfStock' | 'LowStock' | 'Unrecorded' | 'All';
export interface StockReplenishmentFilters {
  search: string;
  status: StockReplenishmentStatus;
  includeInactive: boolean;
}
export interface StockReplenishmentItem {
  ingredientId: string;
  name: string;
  unit: IngredientUnit;
  supplier: string | null;
  isActive: boolean;
  currentStock: number;
  minimumStock: number;
  quantityToMinimum: number;
  isLowStock: boolean;
  hasMovements: boolean;
}
export interface StockReplenishment {
  summary: {
    totalCount: number;
    attentionCount: number;
    outOfStockCount: number;
    lowStockCount: number;
    unrecordedCount: number;
  };
  items: StockReplenishmentItem[];
  page: number;
  pageSize: number;
  totalCount: number;
}
export interface StockInput {
  requestId: string;
  expectedVersion: number;
  type: StockMovementType;
  quantity: number;
  reason: string;
}
export interface StockMovement {
  orderId: string | null;
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
  replenishment(page: number, filters: StockReplenishmentFilters) {
    const params = new HttpParams()
      .set('page', page)
      .set('pageSize', 20)
      .set('search', filters.search.trim())
      .set('status', filters.status)
      .set('includeInactive', filters.includeInactive);
    return this.http.get<StockReplenishment>(environment.apiBaseUrl + '/stock/replenishment', {
      params,
    });
  }
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
