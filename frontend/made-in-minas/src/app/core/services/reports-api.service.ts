import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { timeout } from 'rxjs';
import { environment } from '../../../environments/environment';
import { PaymentMethod } from './payment-api.service';

export interface SalesMetrics {
  created: number;
  createdAndCancelled: number;
  confirmed: number;
  confirmedValue: number;
  averageTicket: number | null;
  received: number;
  refunded: number;
  netReceived: number;
}

export interface SalesReport {
  startDate: string;
  endDate: string;
  timeZone: string;
  startsAt: string;
  endsAt: string;
  calculatedAt: string;
  summary: SalesMetrics;
  days: { date: string; metrics: SalesMetrics }[];
  paymentMethods: {
    method: PaymentMethod;
    received: number;
    refunded: number;
    netReceived: number;
  }[];
  topProducts: { productId: string; productName: string; quantity: number; itemValue: number }[];
}

@Injectable({ providedIn: 'root' })
export class ReportsApi {
  private readonly http = inject(HttpClient);

  sales(startDate?: string, endDate?: string) {
    let params = new HttpParams();
    if (startDate && endDate) {
      params = params.set('startDate', startDate).set('endDate', endDate);
    }
    return this.http
      .get<SalesReport>(environment.apiBaseUrl + '/reports/sales', { params })
      .pipe(timeout(15000));
  }
}
