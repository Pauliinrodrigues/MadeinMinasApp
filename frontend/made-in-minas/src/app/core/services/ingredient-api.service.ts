import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';

export type IngredientUnit = 'kg' | 'l' | 'un';
export const ingredientUnits: { value: IngredientUnit; label: string }[] = [
  { value: 'kg', label: 'Quilograma (kg)' }, { value: 'l', label: 'Litro (L)' }, { value: 'un', label: 'Unidade (un)' },
];
export function unitLabel(unit: IngredientUnit): string { return unit === 'l' ? 'L' : unit; }
export interface IngredientInput {
  name: string; unit: IngredientUnit; unitCost: number; minimumStock: number; supplier: string | null; isActive: boolean;
}
export interface Ingredient extends IngredientInput { id: string; createdAt: string; updatedAt: string; }
export interface IngredientPage { items: Ingredient[]; page: number; pageSize: number; totalCount: number; }

@Injectable({ providedIn: 'root' })
export class IngredientApi {
  private readonly http = inject(HttpClient);
  private readonly url = environment.apiBaseUrl + '/ingredients';

  list(page: number, search: string, active: string) {
    let params = new HttpParams().set('page', page).set('pageSize', 20);
    if (search.trim()) params = params.set('search', search.trim());
    if (active) params = params.set('isActive', active);
    return this.http.get<IngredientPage>(this.url, { params });
  }
  get(id: string) { return this.http.get<Ingredient>(this.url + '/' + encodeURIComponent(id)); }
  create(input: IngredientInput) { return this.http.post<Ingredient>(this.url, input); }
  update(id: string, input: IngredientInput) { return this.http.put<Ingredient>(this.url + '/' + encodeURIComponent(id), input); }
  status(id: string, isActive: boolean) {
    return this.http.put<Ingredient>(this.url + '/' + encodeURIComponent(id) + '/status', { isActive });
  }
}
