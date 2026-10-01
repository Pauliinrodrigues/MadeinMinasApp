import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { timeout } from 'rxjs';
import { environment } from '../../../environments/environment';
import { Order } from './order-api.service';

export type DispatchStatus = 'Ready' | 'AwaitingDelivery' | 'OutForDelivery' | 'Delivered';
export interface DispatchOrder extends Omit<Order, 'customer' | 'history' | 'origin'> {
  customerName: string;
  customerPhone: string;
  readyAt: string | null;
  payment: {
    method: string;
    status: string;
    amount: number;
    cashTendered: number | null;
    changeAmount: number | null;
  } | null;
}
export interface DispatchPageResult {
  serverTime: string;
  items: DispatchOrder[];
  page: number;
  pageSize: number;
  totalCount: number;
}
export interface DispatchStatusInput {
  status: 'AwaitingDelivery' | 'OutForDelivery' | 'Delivered' | 'Finalized';
  expectedVersion: number;
}
@Injectable({ providedIn: 'root' })
export class DispatchApi {
  private readonly http = inject(HttpClient);
  private readonly url = environment.apiBaseUrl + '/dispatch/orders';
  list(status: DispatchStatus, page: number) {
    const params = new HttpParams().set('status', status).set('page', page).set('pageSize', 20);
    return this.http.get<DispatchPageResult>(this.url, { params }).pipe(timeout(20000));
  }
  status(id: string, input: DispatchStatusInput) {
    return this.http
      .put<DispatchOrder>(this.url + '/' + id + '/status', input)
      .pipe(timeout(20000));
  }
}
