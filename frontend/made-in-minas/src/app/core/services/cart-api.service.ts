import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';

export interface CartProduct {
  id: string;
  categoryId: string;
  categoryName: string;
  name: string;
  description: string | null;
  price: number;
}
export interface CartProductPage {
  items: CartProduct[];
  page: number;
  pageSize: number;
  totalCount: number;
}
export interface CartQuoteInput {
  customerId: string;
  fulfillment: 'Pickup' | 'Delivery';
  addressId: string | null;
  deliveryFee: number;
  items: { productId: string; quantity: number; notes: string | null }[];
  notes: string | null;
}
export interface CartQuote {
  customer: { id: string; name: string; phone: string };
  fulfillment: 'Pickup' | 'Delivery';
  address: {
    id: string;
    street: string;
    number: string;
    neighborhood: string;
    city: string;
    state: string;
    complement: string | null;
    postalCode: string | null;
    reference: string | null;
  } | null;
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
  deliveryFee: number;
  total: number;
  reviewToken: string;
  calculatedAt: string;
}

@Injectable({ providedIn: 'root' })
export class CartApi {
  private readonly http = inject(HttpClient);
  private readonly url = environment.apiBaseUrl + '/cart';

  products(page: number, search: string) {
    const params = new HttpParams()
      .set('page', page)
      .set('pageSize', 20)
      .set('search', search.trim());
    return this.http.get<CartProductPage>(this.url + '/products', { params });
  }
  quote(input: CartQuoteInput) {
    return this.http.post<CartQuote>(this.url + '/quote', input);
  }
}
