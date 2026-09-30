import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';

export interface CustomerInput {
  name: string;
  phone: string;
  isActive: boolean;
}
export interface Customer extends CustomerInput {
  id: string;
  createdAt: string;
  updatedAt: string;
}
export interface CustomerPage {
  items: Customer[];
  page: number;
  pageSize: number;
  totalCount: number;
}
export interface AddressInput {
  street: string;
  number: string;
  neighborhood: string;
  city: string;
  state: string;
  complement: string | null;
  postalCode: string | null;
  reference: string | null;
  isActive: boolean;
}
export interface Address extends AddressInput {
  id: string;
  customerId: string;
  createdAt: string;
  updatedAt: string;
}
export interface AddressPage {
  items: Address[];
  page: number;
  pageSize: number;
  totalCount: number;
}
export const brazilianStates = [
  'AC',
  'AL',
  'AP',
  'AM',
  'BA',
  'CE',
  'DF',
  'ES',
  'GO',
  'MA',
  'MT',
  'MS',
  'MG',
  'PA',
  'PB',
  'PR',
  'PE',
  'PI',
  'RJ',
  'RN',
  'RS',
  'RO',
  'RR',
  'SC',
  'SP',
  'SE',
  'TO',
];

export function validBrazilianPhone(value: string): boolean {
  const text = value.trim();
  if (value.length > 30 || !/^\+?[0-9 ().-]+$/.test(text)) {
    return false;
  }
  let digits = text.replace(/[^0-9]/g, '');
  if ((digits.length === 12 || digits.length === 13) && digits.startsWith('55')) {
    digits = digits.slice(2);
  } else if (text.startsWith('+')) {
    return false;
  }
  return /^[1-9][0-9](?:[2-5][0-9]{7}|9[0-9]{8})$/.test(digits);
}

@Injectable({ providedIn: 'root' })
export class CustomerApi {
  private readonly http = inject(HttpClient);
  private readonly url = environment.apiBaseUrl + '/customers';

  list(page: number, search: string, active: string) {
    let params = this.pageParams(page, active);
    if (search.trim()) {
      params = params.set('search', search.trim());
    }
    return this.http.get<CustomerPage>(this.url, { params });
  }
  get(id: string) {
    return this.http.get<Customer>(this.customerUrl(id));
  }
  create(input: CustomerInput) {
    return this.http.post<Customer>(this.url, input);
  }
  update(id: string, input: CustomerInput) {
    return this.http.put<Customer>(this.customerUrl(id), input);
  }
  status(id: string, isActive: boolean) {
    return this.http.put<Customer>(this.customerUrl(id) + '/status', { isActive });
  }
  addresses(customerId: string, page: number, active: string) {
    return this.http.get<AddressPage>(this.customerUrl(customerId) + '/addresses', {
      params: this.pageParams(page, active),
    });
  }
  createAddress(customerId: string, input: AddressInput) {
    return this.http.post<Address>(this.customerUrl(customerId) + '/addresses', input);
  }
  updateAddress(customerId: string, addressId: string, input: AddressInput) {
    return this.http.put<Address>(this.addressUrl(customerId, addressId), input);
  }
  addressStatus(customerId: string, addressId: string, isActive: boolean) {
    return this.http.put<Address>(this.addressUrl(customerId, addressId) + '/status', { isActive });
  }
  private customerUrl(id: string): string {
    return this.url + '/' + encodeURIComponent(id);
  }
  private addressUrl(customerId: string, addressId: string): string {
    return this.customerUrl(customerId) + '/addresses/' + encodeURIComponent(addressId);
  }
  private pageParams(page: number, active: string): HttpParams {
    let params = new HttpParams().set('page', page).set('pageSize', 20);
    if (active) {
      params = params.set('isActive', active);
    }
    return params;
  }
}
