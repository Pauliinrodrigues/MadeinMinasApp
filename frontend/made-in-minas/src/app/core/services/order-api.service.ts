import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { timeout } from 'rxjs';
import { environment } from '../../../environments/environment';
import { CartQuote, CartQuoteInput } from './cart-api.service';

export type OrderStatus = 'New' | 'Confirmed' | 'InPreparation' | 'Ready' | 'Cancelled';
export interface OrderSummary {
  id: string;
  number: number;
  customerName: string;
  fulfillment: 'Pickup' | 'Delivery';
  status: OrderStatus;
  total: number;
  createdAt: string;
}
export interface OrderPage {
  items: OrderSummary[];
  page: number;
  pageSize: number;
  totalCount: number;
}
export interface Order extends Omit<CartQuote, 'calculatedAt' | 'reviewToken'> {
  id: string;
  number: number;
  origin: 'Manual';
  status: OrderStatus;
  version: number;
  createdAt: string;
  updatedAt: string;
  history: {
    version: number;
    fromStatus: OrderStatus | null;
    toStatus: OrderStatus;
    actorId: string;
    actorName: string;
    reason: string | null;
    occurredAt: string;
  }[];
}
export interface CreateOrderInput {
  requestId: string;
  reviewToken: string;
  cart: CartQuoteInput;
}
export interface OrderStatusInput {
  status: 'Confirmed' | 'Cancelled';
  expectedVersion: number;
  reason: string | null;
}
export function orderStatusLabel(status: OrderStatus): string {
  return {
    New: 'Novo',
    Confirmed: 'Confirmado',
    InPreparation: 'Em preparação',
    Ready: 'Pronto',
    Cancelled: 'Cancelado',
  }[status];
}

@Injectable({ providedIn: 'root' })
export class OrderApi {
  private readonly http = inject(HttpClient);
  private readonly url = environment.apiBaseUrl + '/orders';

  list(page: number, search: string, status: string) {
    let params = new HttpParams()
      .set('page', page)
      .set('pageSize', 20)
      .set('search', search.trim());
    if (status) {
      params = params.set('status', status);
    }
    return this.http.get<OrderPage>(this.url, { params });
  }
  get(id: string) {
    return this.http.get<Order>(this.url + '/' + id);
  }
  create(input: CreateOrderInput) {
    return this.http.post<Order>(this.url, input).pipe(timeout(20000));
  }
  status(id: string, input: OrderStatusInput) {
    return this.http.put<Order>(this.url + '/' + id + '/status', input).pipe(timeout(20000));
  }
}
