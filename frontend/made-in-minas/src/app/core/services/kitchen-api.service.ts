import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { timeout } from 'rxjs';
import { environment } from '../../../environments/environment';

export type KitchenStatus = 'Confirmed' | 'InPreparation' | 'Ready';
export interface KitchenOrder {
  id: string;
  number: number;
  fulfillment: 'Pickup' | 'Delivery';
  status: KitchenStatus;
  version: number;
  notes: string | null;
  createdAt: string;
  confirmedAt: string | null;
  preparationStartedAt: string | null;
  readyAt: string | null;
  items: { position: number; name: string; quantity: number; notes: string | null }[];
}
export interface KitchenColumn {
  status: KitchenStatus;
  items: KitchenOrder[];
  page: number;
  pageSize: number;
  totalCount: number;
}
export interface KitchenBoard {
  serverTime: string;
  columns: KitchenColumn[];
}
export interface KitchenPages {
  confirmedPage: number;
  preparingPage: number;
  readyPage: number;
}
export interface KitchenStatusInput {
  status: 'InPreparation' | 'Ready';
  expectedVersion: number;
}

@Injectable({ providedIn: 'root' })
export class KitchenApi {
  private readonly http = inject(HttpClient);
  private readonly url = environment.apiBaseUrl + '/kitchen/orders';
  board(pages: KitchenPages) {
    const params = new HttpParams()
      .set('confirmedPage', pages.confirmedPage)
      .set('preparingPage', pages.preparingPage)
      .set('readyPage', pages.readyPage)
      .set('pageSize', 20);
    return this.http.get<KitchenBoard>(this.url, { params }).pipe(timeout(20000));
  }
  status(id: string, input: KitchenStatusInput) {
    return this.http.put<KitchenOrder>(this.url + '/' + id + '/status', input).pipe(timeout(20000));
  }
}
