import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { timeout } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface MenuProduct {
  id: string;
  categoryId: string;
  name: string;
  description: string | null;
  price: number;
  imageUrl: string | null;
  isAvailable: boolean;
}

export interface PublicMenu {
  categories: { id: string; name: string }[];
  items: MenuProduct[];
  page: number;
  pageSize: number;
  totalCount: number;
}

@Injectable({ providedIn: 'root' })
export class MenuApi {
  private readonly http = inject(HttpClient);

  get(categoryId: string, page: number, search = '') {
    let params = new HttpParams().set('page', page);
    if (search.trim()) {
      params = params.set('search', search.trim());
    }
    if (categoryId) {
      params = params.set('categoryId', categoryId);
    }
    return this.http
      .get<PublicMenu>(environment.apiBaseUrl + '/menu', { params })
      .pipe(timeout(15000));
  }
}
