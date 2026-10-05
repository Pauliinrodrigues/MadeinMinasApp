import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { timeout } from 'rxjs';
import { environment } from '../../../environments/environment';
import { OrderStatus } from './order-api.service';

export interface PublicOrderTracking {
  number: number;
  fulfillment: 'Pickup' | 'Delivery';
  total: number;
  status: OrderStatus;
  createdAt: string;
  updatedAt: string;
  history: { status: OrderStatus; occurredAt: string }[];
}

@Injectable({ providedIn: 'root' })
export class PublicOrderTrackingApi {
  private readonly http = inject(HttpClient);

  get(token: string) {
    return this.http
      .get<PublicOrderTracking>(environment.apiBaseUrl + '/public-orders/tracking', {
        headers: { 'X-Order-Access': token },
      })
      .pipe(timeout(15000));
  }
}
