import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { timeout } from 'rxjs';
import { environment } from '../../../environments/environment';
import { OrderStatus } from './order-api.service';

export type PaymentMethod = 'Cash' | 'Pix' | 'CreditCard' | 'DebitCard';
export type PaymentStatus = 'Pending' | 'Received' | 'Cancelled' | 'Refunded';
export interface Payment {
  id: string;
  orderId: string;
  method: PaymentMethod;
  status: PaymentStatus;
  version: number;
  amount: number;
  cashTendered: number | null;
  changeAmount: number | null;
  createdAt: string;
  updatedAt: string;
  history: {
    version: number;
    fromStatus: PaymentStatus | null;
    toStatus: PaymentStatus;
    actorId: string;
    actorName: string;
    reason: string | null;
    occurredAt: string;
  }[];
}
export interface PaymentPage {
  orderId: string;
  orderNumber: number;
  orderStatus: OrderStatus;
  orderVersion: number;
  orderTotal: number;
  receivedAmount: number;
  balance: number;
  activePayment: Payment | null;
  items: Payment[];
  page: number;
  pageSize: number;
  totalCount: number;
}
export interface CreatePaymentInput {
  requestId: string;
  method: PaymentMethod;
  expectedOrderVersion: number;
}
export interface ReceivePaymentInput {
  expectedVersion: number;
  receivedConfirmed: boolean;
  cashTendered: number | null;
}
export interface CancelPaymentInput {
  expectedVersion: number;
  reason: string;
}
export interface RefundPaymentInput extends CancelPaymentInput {
  refundedConfirmed: boolean;
}
export type PaymentCommand =
  | { action: 'create'; input: CreatePaymentInput }
  | { action: 'receive'; id: string; input: ReceivePaymentInput }
  | { action: 'cancel'; id: string; input: CancelPaymentInput }
  | { action: 'refund'; id: string; input: RefundPaymentInput };

export function paymentMethodLabel(method: PaymentMethod): string {
  return {
    Cash: 'Dinheiro',
    Pix: 'Pix',
    CreditCard: 'Cartão de crédito',
    DebitCard: 'Cartão de débito',
  }[method];
}
export function paymentStatusLabel(status: PaymentStatus): string {
  return {
    Pending: 'Pendente',
    Received: 'Recebido',
    Cancelled: 'Cancelado',
    Refunded: 'Devolvido',
  }[status];
}

@Injectable({ providedIn: 'root' })
export class PaymentApi {
  private readonly http = inject(HttpClient);
  private url(orderId: string): string {
    return environment.apiBaseUrl + '/orders/' + orderId + '/payments';
  }
  list(orderId: string, page: number) {
    const params = new HttpParams().set('page', page).set('pageSize', 20);
    return this.http.get<PaymentPage>(this.url(orderId), { params }).pipe(timeout(20000));
  }
  execute(orderId: string, command: PaymentCommand) {
    const request =
      command.action === 'create'
        ? this.http.post<Payment>(this.url(orderId), command.input)
        : this.http.put<Payment>(
            this.url(orderId) + '/' + command.id + '/' + command.action,
            command.input,
          );
    return request.pipe(timeout(20000));
  }
}
