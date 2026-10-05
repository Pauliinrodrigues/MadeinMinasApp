import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { timeout } from 'rxjs';
import { environment } from '../../../environments/environment';
import { PublicCartInput, PublicCartQuote } from './public-cart-api.service';

export interface PublicCheckoutInput {
  name: string;
  phone: string;
  cart: PublicCartInput;
  fulfillment?: 'Pickup' | 'Delivery';
  address?: PublicDeliveryAddress;
}
export interface PublicDeliveryAddress {
  areaId: string;
  street: string;
  number: string;
  complement: string | null;
  postalCode: string | null;
  reference: string | null;
}
export interface PublicDeliveryArea {
  id: string;
  neighborhood: string;
  city: string;
  state: string;
  fee: number;
}
export interface PublicCheckoutReview extends PublicCartQuote {
  name: string;
  phone: string;
  fulfillment: 'Pickup' | 'Delivery';
  address?: Omit<PublicDeliveryAddress, 'areaId'> & {
    neighborhood: string;
    city: string;
    state: string;
  };
  deliveryFee: number;
  total: number;
  reviewToken: string;
}
export interface PublicOrderInput {
  requestId: string;
  reviewToken: string;
  checkout: PublicCheckoutInput;
}
export interface PublicOrderReceipt {
  number: number;
  fulfillment: 'Pickup' | 'Delivery';
  total: number;
  createdAt: string;
  tracking?: { token: string; expiresAt: string } | null;
}

@Injectable({ providedIn: 'root' })
export class PublicCheckoutApi {
  private readonly http = inject(HttpClient);
  private readonly url = environment.apiBaseUrl + '/public-checkout';

  deliveryAreas() {
    return this.http.get<PublicDeliveryArea[]>(this.url + '/delivery-areas').pipe(timeout(15000));
  }

  review(input: PublicCheckoutInput) {
    return this.http.post<PublicCheckoutReview>(this.url + '/review', input).pipe(timeout(15000));
  }
  send(input: PublicOrderInput) {
    return this.http.post<PublicOrderReceipt>(this.url + '/orders', input).pipe(timeout(15000));
  }
}
