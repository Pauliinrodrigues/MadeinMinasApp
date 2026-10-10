import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';

export interface StoreOpeningHours {
  day: number;
  opensAt: string;
  closesAt: string;
  isClosed: boolean;
}
export interface StoreAttendanceData {
  hours: StoreOpeningHours[];
  paymentMethods: string[];
  deliveryEstimateMessage: string;
  isPaused: boolean;
  receiveOrdersOutsideHours: boolean;
}
export interface StoreAttendanceResponse {
  settings: StoreAttendanceData;
  revision: string;
  updatedAt: string | null;
  updatedBy: string | null;
}
export interface PublicStoreAttendance {
  hours: StoreOpeningHours[];
  paymentMethods: string[];
  deliveryEstimateMessage: string;
  timeZone: string;
  isOpen: boolean;
  isPaused: boolean;
  acceptingOrders: boolean;
  checkedAt: string;
  nextChangeAt: string | null;
}
export const storeDayNames = [
  'Segunda-feira',
  'Terça-feira',
  'Quarta-feira',
  'Quinta-feira',
  'Sexta-feira',
  'Sábado',
  'Domingo',
];
export const storePaymentLabels: Record<string, string> = {
  Card: 'Cartão',
  Cash: 'Dinheiro',
  Pix: 'Pix',
};

@Injectable({ providedIn: 'root' })
export class StoreAttendanceApi {
  private readonly http = inject(HttpClient);
  private readonly url = environment.apiBaseUrl + '/store-attendance';
  get() {
    return this.http.get<StoreAttendanceResponse>(this.url);
  }
  save(expectedRevision: string, settings: StoreAttendanceData) {
    return this.http.put<StoreAttendanceResponse>(this.url, { expectedRevision, settings });
  }
  publicInfo() {
    return this.http.get<PublicStoreAttendance>(this.url + '/public');
  }
}
