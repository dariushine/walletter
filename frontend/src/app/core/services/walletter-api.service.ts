import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  Wallet,
  Transaction,
  TransactionDetail,
  TransactionList,
  TransactionCreated,
  TransactionType,
  Category,
  Exchange,
  ExchangeDetail,
  ExchangeList,
  ExchangeCreated,
  RecurringPayment,
  DailyRate,
  EffectiveRate,
  Settings,
  Stats,
  CategoryStat,
  SessionInfo,
  ApiToken,
  WalletReport,
} from '../../models/walletter.models';

/**
 * Cliente central de la API de Walletter.
 * Todas las llamadas pasan por HttpClient, cuyo HttpInterceptor se encarga de
 * adjuntar credenciales (cookies httpOnly), refrescar el token y mapear errores.
 */
@Injectable({ providedIn: 'root' })
export class WalletterApiService {
  private readonly base = environment.apiUrl;

  constructor(private readonly http: HttpClient) {}

  // ===== Auth =====
  authStatus(): Observable<{ enabled: boolean }> {
    return this.http.get<{ enabled: boolean }>(`${this.base}/auth/status`);
  }

  login(username: string, password: string, remember: boolean): Observable<any> {
    return this.http.post(`${this.base}/auth/login`, { username, password, remember }, { withCredentials: true });
  }

  logout(): Observable<{ success: boolean }> {
    return this.http.post<{ success: boolean }>(`${this.base}/auth/logout`, {}, { withCredentials: true });
  }

  sessions(): Observable<SessionInfo[]> {
    return this.http.get<SessionInfo[]>(`${this.base}/auth/sessions`);
  }

  revokeSession(jti: string): Observable<{ success: boolean }> {
    return this.http.delete<{ success: boolean }>(`${this.base}/auth/sessions/${encodeURIComponent(jti)}`);
  }

  apiTokens(): Observable<ApiToken[]> {
    return this.http.get<ApiToken[]>(`${this.base}/auth/tokens`);
  }

  createApiToken(name: string): Observable<{ id: number; token: string }> {
    return this.http.post<{ id: number; token: string }>(`${this.base}/auth/tokens`, { name });
  }

  revokeApiToken(id: number): Observable<{ success: boolean }> {
    return this.http.delete<{ success: boolean }>(`${this.base}/auth/tokens/${id}`);
  }

  // ===== Wallets =====
  wallets(): Observable<Wallet[]> {
    return this.http.get<Wallet[]>(`${this.base}/wallets`);
  }

  deletedWallets(): Observable<Wallet[]> {
    return this.http.get<Wallet[]>(`${this.base}/wallets/deleted`);
  }

  wallet(id: number): Observable<Wallet> {
    return this.http.get<Wallet>(`${this.base}/wallets/${id}`);
  }

  walletReport(id: number): Observable<WalletReport> {
    return this.http.get<WalletReport>(`${this.base}/wallets/${id}/report`);
  }

  createWallet(data: Partial<Wallet>): Observable<Wallet> {
    return this.http.post<Wallet>(`${this.base}/wallets`, data);
  }

  updateWallet(id: number, data: Partial<Wallet>): Observable<Wallet> {
    return this.http.put<Wallet>(`${this.base}/wallets/${id}`, data);
  }

  deleteWallet(id: number): Observable<Wallet> {
    return this.http.delete<Wallet>(`${this.base}/wallets/${id}`);
  }

  reactivateWallet(id: number): Observable<Wallet> {
    return this.http.put<Wallet>(`${this.base}/wallets/${id}/reactivate`, {});
  }

  // ===== Transactions =====
  transactions(params: {
    page?: number;
    limit?: number;
    from?: string;
    to?: string;
    walletId?: number;
  } = {}): Observable<TransactionList> {
    return this.http.get<TransactionList>(`${this.base}/transactions`, { params: this.toParams(params) });
  }

  transaction(id: number): Observable<TransactionDetail> {
    return this.http.get<TransactionDetail>(`${this.base}/transactions/${id}`);
  }

  createTransaction(data: {
    walletId: number;
    categoryName: string;
    type: TransactionType;
    amount: number;
    description?: string;
    fee?: number;
    date?: string;
    time?: string;
    tz?: string;
  }): Observable<TransactionCreated> {
    return this.http.post<TransactionCreated>(`${this.base}/transactions`, data);
  }

  updateTransaction(
    id: number,
    data: { description?: string; amount?: number; date?: string; time?: string; categoryName?: string; tz?: string }
  ): Observable<{ success: boolean; id: number }> {
    return this.http.put<{ success: boolean; id: number }>(`${this.base}/transactions/${id}`, data);
  }

  deleteTransaction(id: number): Observable<{ success: boolean }> {
    return this.http.delete<{ success: boolean }>(`${this.base}/transactions/${id}`);
  }

  addTransactionFee(id: number, data: { amount: number; date?: string; time?: string; tz?: string }): Observable<{ success: boolean; feeId: number }> {
    return this.http.post<{ success: boolean; feeId: number }>(`${this.base}/transactions/${id}/fee`, data);
  }

  associateTransaction(
    id: number,
    data: { amount: number; type: TransactionType; categoryName: string; description?: string; date?: string; time?: string; tz?: string }
  ): Observable<{ success: boolean; associateId: number }> {
    return this.http.post<{ success: boolean; associateId: number }>(`${this.base}/transactions/${id}/associate`, data);
  }

  // ===== Exchanges =====
  exchanges(params: { page?: number; limit?: number } = {}): Observable<ExchangeList> {
    return this.http.get<ExchangeList>(`${this.base}/exchanges`, { params: this.toParams(params) });
  }

  exchange(id: number): Observable<ExchangeDetail> {
    return this.http.get<ExchangeDetail>(`${this.base}/exchanges/${id}`);
  }

  createExchange(data: {
    fromWalletId: number;
    toWalletId: number;
    fromAmount: number;
    toAmount: number;
    description?: string;
    fee?: number;
    creditFee?: number;
    date: string;
    time: string;
    tz?: string;
  }): Observable<ExchangeCreated> {
    return this.http.post<ExchangeCreated>(`${this.base}/exchanges`, data);
  }

  updateExchange(
    id: number,
    data: { fromAmount?: number; toAmount?: number; fee?: number; creditFee?: number; description?: string; date?: string; time?: string; tz?: string }
  ): Observable<{ success: boolean; message: string }> {
    return this.http.put<{ success: boolean; message: string }>(`${this.base}/exchanges/${id}`, data);
  }

  deleteExchange(id: number): Observable<{ success: boolean }> {
    return this.http.delete<{ success: boolean }>(`${this.base}/exchanges/${id}`);
  }

  // ===== Categories =====
  categories(type?: TransactionType): Observable<Category[]> {
    const params = type ? this.toParams({ type }) : undefined;
    return this.http.get<Category[]>(`${this.base}/categories`, { params });
  }

  createCategory(data: { name: string; type: TransactionType; color?: string }): Observable<Category> {
    return this.http.post<Category>(`${this.base}/categories`, data);
  }

  updateCategory(id: number, data: { name?: string; color?: string }): Observable<Category> {
    return this.http.put<Category>(`${this.base}/categories/${id}`, data);
  }

  deleteCategory(id: number): Observable<Category> {
    return this.http.delete<Category>(`${this.base}/categories/${id}`);
  }

  reactivateCategory(id: number): Observable<Category> {
    return this.http.put<Category>(`${this.base}/categories/${id}/reactivate`, {});
  }

  // ===== Recurring payments =====
  recurringPayments(): Observable<RecurringPayment[]> {
    return this.http.get<RecurringPayment[]>(`${this.base}/recurring-payments`);
  }

  recurringPayment(id: number): Observable<RecurringPayment> {
    return this.http.get<RecurringPayment>(`${this.base}/recurring-payments/${id}`);
  }

  createRecurringPayment(data: Partial<RecurringPayment>): Observable<RecurringPayment> {
    return this.http.post<RecurringPayment>(`${this.base}/recurring-payments`, data);
  }

  updateRecurringPayment(id: number, data: Partial<RecurringPayment>): Observable<RecurringPayment> {
    return this.http.put<RecurringPayment>(`${this.base}/recurring-payments/${id}`, data);
  }

  deleteRecurringPayment(id: number): Observable<RecurringPayment> {
    return this.http.delete<RecurringPayment>(`${this.base}/recurring-payments/${id}`);
  }

  executeRecurringPayment(
    id: number,
    data: { date?: string; time?: string; tz?: string; walletId?: number; overrideAmount?: number; overrideFee?: number; overrideCategoryName?: string; overrideWalletId?: number; description?: string }
  ): Observable<{ success: boolean; transactionId: number; feeTransactionId?: number | null }> {
    return this.http.post<{ success: boolean; transactionId: number; feeTransactionId?: number | null }>(
      `${this.base}/recurring-payments/${id}/execute`,
      data
    );
  }

  // ===== Rates =====
  effectiveRate(date?: string): Observable<EffectiveRate> {
    const params = date ? this.toParams({ date }) : undefined;
    return this.http.get<EffectiveRate>(`${this.base}/rates/effective`, { params });
  }

  todayRates(): Observable<{ data: { bcv: number; paralelo: number; date: string; source: string } }> {
    return this.http.get<{ data: { bcv: number; paralelo: number; date: string; source: string } }>(`${this.base}/daily-rates/today`);
  }

  dailyRates(): Observable<{ data: DailyRate[]; total: number }> {
    return this.http.get<{ data: DailyRate[]; total: number }>(`${this.base}/daily-rates`);
  }

  upsertDailyRate(data: { date: string; bcv: number; paralelo: number; source?: string }): Observable<DailyRate> {
    return this.http.post<DailyRate>(`${this.base}/daily-rates`, data);
  }

  updateDailyRate(id: number, data: { bcv?: number; paralelo?: number; source?: string }): Observable<DailyRate> {
    return this.http.put<DailyRate>(`${this.base}/daily-rates/${id}`, data);
  }

  deleteDailyRate(id: number): Observable<{ success: boolean }> {
    return this.http.delete<{ success: boolean }>(`${this.base}/daily-rates/${id}`);
  }

  // ===== Settings =====
  settings(): Observable<Settings> {
    return this.http.get<Settings>(`${this.base}/settings`);
  }

  setTimeZone(timezone: string): Observable<{ success: boolean; timezone: string }> {
    return this.http.put<{ success: boolean; timezone: string }>(`${this.base}/settings/user_timezone`, { timezone });
  }

  // ===== Stats =====
  stats(): Observable<Stats> {
    return this.http.get<Stats>(`${this.base}/stats`);
  }

  statsByCategory(): Observable<CategoryStat[]> {
    return this.http.get<CategoryStat[]>(`${this.base}/stats/by-category`);
  }

  // ===== Health =====
  health(): Observable<{ status: string; timestamp: string; service: string; version: string }> {
    return this.http.get<{ status: string; timestamp: string; service: string; version: string }>(`${this.base}/health`);
  }

  private toParams(obj: Record<string, any>): Record<string, string> {
    const out: Record<string, string> = {};
    for (const [k, v] of Object.entries(obj)) {
      if (v !== undefined && v !== null && v !== '') out[k] = String(v);
    }
    return out;
  }
}
