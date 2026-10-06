import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';

export interface DeliveryArea {
  id: string;
  neighborhood: string;
  city: string;
  state: string;
  fee: number;
  isActive: boolean;
}
export interface DeliverySettings {
  areas: DeliveryArea[];
  revision: string;
  updatedAt: string | null;
  updatedBy: string | null;
}
export interface SaveDeliverySettings {
  expectedRevision: string;
  areas: DeliveryArea[];
}

@Injectable({ providedIn: 'root' })
export class DeliverySettingsApi {
  private readonly http = inject(HttpClient);
  private readonly url = environment.apiBaseUrl + '/delivery-settings';
  get() {
    return this.http.get<DeliverySettings>(this.url);
  }
  save(input: SaveDeliverySettings) {
    return this.http.put<DeliverySettings>(this.url, input);
  }
}
