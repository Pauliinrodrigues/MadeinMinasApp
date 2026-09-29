import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';

export interface Product {
  id: string; categoryId: string; categoryName: string; categoryIsActive: boolean;
  name: string; description: string | null; price: number; imageUrl: string | null;
  isActive: boolean; isAvailable: boolean; isAvailableForSale: boolean; createdAt: string; updatedAt: string;
}
export interface ProductInput {
  name: string; categoryId: string; description: string | null; price: number; imageUrl: string | null;
  isActive: boolean; isAvailable: boolean;
}
export interface ProductPage { items: Product[]; page: number; pageSize: number; totalCount: number; }

const currency = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' });
export const formatProductPrice = (price: number): string => currency.format(price);

export function parseProductPrice(value: string): number | null {
  const text = value.trim();
  if (!/^[0-9]{1,6}([.,][0-9]{1,2})?$/.test(text)) return null;
  const price = Number(text.replace(',', '.'));
  return price >= 0.01 && price <= 999999.99 ? price : null;
}

export function validProductImage(value: string): boolean {
  if (!value.trim()) return true;
  try {
    const url = new URL(value.trim());
    return url.protocol === 'https:' && !!url.hostname && !url.username && !url.password;
  } catch { return false; }
}

@Injectable({ providedIn: 'root' })
export class ProductApi {
  private readonly http = inject(HttpClient);
  private readonly url = environment.apiBaseUrl + '/products';

  list(page: number, search: string, categoryId: string, active: string, available: string) {
    let params = new HttpParams().set('page', page).set('pageSize', 20);
    if (search.trim()) params = params.set('search', search.trim());
    if (categoryId) params = params.set('categoryId', categoryId);
    if (active) params = params.set('isActive', active);
    if (available) params = params.set('isAvailableForSale', available);
    return this.http.get<ProductPage>(this.url, { params });
  }
  get(id: string) { return this.http.get<Product>(this.url + '/' + encodeURIComponent(id)); }
  create(input: ProductInput) { return this.http.post<Product>(this.url, input); }
  update(id: string, input: ProductInput) { return this.http.put<Product>(this.url + '/' + encodeURIComponent(id), input); }
  status(id: string, isActive: boolean) {
    return this.http.put<Product>(this.url + '/' + encodeURIComponent(id) + '/status', { isActive });
  }
  availability(id: string, isAvailable: boolean) {
    return this.http.put<Product>(this.url + '/' + encodeURIComponent(id) + '/availability', { isAvailable });
  }
}

