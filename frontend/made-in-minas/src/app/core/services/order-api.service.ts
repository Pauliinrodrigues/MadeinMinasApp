import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { timeout } from 'rxjs';
import { environment } from '../../../environments/environment';
import { CartQuote, CartQuoteInput } from './cart-api.service';

export type OrderStatus =
  | 'New'
  | 'Confirmed'
  | 'InPreparation'
  | 'Ready'
  | 'AwaitingDelivery'
  | 'OutForDelivery'
  | 'Delivered'
  | 'Finalized'
  | 'Cancelled';
export type OrderPaymentStatus = 'NotRegistered' | 'Pending' | 'Received' | 'Refunded' | 'NotDue';
export interface OrderSummary {
  id: string;
  number: number;
  customerName: string;
  origin: 'Manual' | 'DirectLink';
  paymentStatus: OrderPaymentStatus;
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
  origin: 'Manual' | 'DirectLink';
  status: OrderStatus;
  version: number;
  stockStatus: 'Pending' | 'Consumed' | 'Returned' | 'Retained' | 'Legacy' | 'NotRequired';
  stockComponents: {
    productId: string;
    productName: string;
    ingredientId: string;
    ingredientName: string;
    unit: string;
    productQuantity: number;
    recipeYield: number;
    recipeQuantity: number;
    consumedQuantity: number;
  }[];
  createdAt: string;
  updatedAt: string;
  history: {
    version: number;
    fromStatus: OrderStatus | null;
    toStatus: OrderStatus;
    actorId: string | null;
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
    AwaitingDelivery: 'Aguardando entrega',
    OutForDelivery: 'Saiu para entrega',
    Delivered: 'Entregue',
    Finalized: 'Finalizado',
    Cancelled: 'Cancelado',
  }[status];
}

export function orderPaymentStatusLabel(status: OrderPaymentStatus): string {
  return (
    {
      NotRegistered: 'Não definido',
      Pending: 'Aguardando recebimento',
      Received: 'Recebido',
      Refunded: 'Devolvido',
      NotDue: 'Sem cobrança',
    }[status] ?? 'Não informado'
  );
}

@Injectable({ providedIn: 'root' })
export class OrderApi {
  private readonly http = inject(HttpClient);
  private readonly url = environment.apiBaseUrl + '/orders';

  list(page: number, search: string, status: string, origin = '', paymentStatus = '') {
    let params = new HttpParams()
      .set('page', page)
      .set('pageSize', 20)
      .set('search', search.trim());
    if (status) {
      params = params.set('status', status);
    }
    if (origin) {
      params = params.set('origin', origin);
    }
    if (paymentStatus) {
      params = params.set('paymentStatus', paymentStatus);
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
