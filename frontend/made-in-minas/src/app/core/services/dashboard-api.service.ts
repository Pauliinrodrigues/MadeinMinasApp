import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { timeout } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface DailyDashboard {
  date: string;
  timeZone: string;
  startsAt: string;
  endsAt: string;
  calculatedAt: string;
  orders: {
    created: number;
    createdAndCancelled: number;
    confirmed: number;
    confirmedValue: number;
    averageTicket: number | null;
  };
  receipts: { received: number; refunded: number; netReceived: number };
  queues: { status: string; count: number }[];
  production: { completed: number; averageMinutes: number | null };
  topProducts: { productId: string; productName: string; quantity: number; itemValue: number }[];
}

@Injectable({ providedIn: 'root' })
export class DashboardApi {
  private readonly http = inject(HttpClient);

  today() {
    return this.http
      .get<DailyDashboard>(environment.apiBaseUrl + '/dashboard/today')
      .pipe(timeout(15000));
  }
}
