import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { timeout } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface PublicCartInput {
  items: { productId: string; quantity: number; notes: string | null }[];
  notes: string | null;
}

export interface PublicCartQuote {
  items: {
    productId: string;
    name: string;
    quantity: number;
    unitPrice: number;
    lineTotal: number;
    notes: string | null;
  }[];
  notes: string | null;
  subtotal: number;
  calculatedAt: string;
}

@Injectable({ providedIn: 'root' })
export class PublicCartApi {
  private readonly http = inject(HttpClient);

  quote(input: PublicCartInput) {
    return this.http
      .post<PublicCartQuote>(environment.apiBaseUrl + '/public-cart/quote', input)
      .pipe(timeout(15000));
  }
}
