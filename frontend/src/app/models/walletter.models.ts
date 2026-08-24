// Modelos de dominio que reflejan los DTOs que devuelve la API .NET (Walletter).
// Montos en unidades decimales; el backend los guarda en centavos (×100) y
// tasas ×10000, pero la API ya los proyecta a unidades.

export type TransactionType = 'income' | 'expense';

/** Billetera (GET /api/wallets) */
export interface Wallet {
  id: number;
  name: string;
  alias?: string | null;
  type: string;
  currency: string;
  balance: number;
  description?: string | null;
  icon?: string | null;
  color?: string | null;
  isActive: boolean;
  excludeFromTotal: boolean;
  hideInDashboard: boolean;
  createdAt: string;
}

/** Transacción proyectada (GET /api/transactions) */
export interface Transaction {
  id: number;
  walletId: number;
  walletName?: string;
  walletCurrency?: string;
  category: string;
  type: TransactionType;
  amount: number;
  description?: string;
  datetimeUtc: string;
  fee: number;
  parentTransactionId?: number | null;
  date: string; // YYYY-MM-DD en la zona del usuario
  time: string; // HH:MM en la zona del usuario
}

/** Detalle de transacción (GET /api/transactions/:id) */
export interface TransactionDetail extends Transaction {
  /** Saldo en vivo de la billetera tras esta transacción. */
  resultingBalance: number;
  /** true si es débito/crédito de exchange o una comisión suya. */
  isExchange: boolean;
  /** id del exchange al que pertenece (si es transacción de exchange); null si no. */
  exchangeId?: number | null;
  /** Transacciones asociadas (hijas: comisiones, asociadas). */
  associated: Transaction[];
}

/** Respuesta paginada de transacciones */
export interface TransactionList {
  data: Transaction[];
  total: number;
  page: number;
  limit: number;
  tz: string;
}

/** Resultado de crear una transacción */
export interface TransactionCreated {
  id?: number;
  feeTransactionId?: number | null;
  wallet?: string;
  currency?: string;
  amount?: number;
  type?: TransactionType;
  newBalance?: number;
  category?: string;
  fee?: number;
  datetime_utc?: string;
}

/** Categoría (GET /api/categories) */
export interface Category {
  id: number;
  name: string;
  type: TransactionType;
  color?: string | null;
  icon?: string | null;
  isActive: boolean;
  createdAt: string;
}

/** Exchange (GET /api/exchanges) */
export interface Exchange {
  id: number;
  fromWalletId: number;
  toWalletId: number;
  fromAmount: number;
  toAmount: number;
  rate: number;
  fee: number;
  creditFee: number;
  description?: string;
  createdAt: string;
  debitTransactionId: number;
  creditTransactionId: number;
  fromWalletName: string;
  toWalletName: string;
  fromCurrency: string;
  toCurrency: string;
}

/** Transacción dentro de un exchange (proyección ligera). */
export interface ExchangeTransaction {
  id: number;
  category: string;
  type: TransactionType;
  amount: number;
  description?: string;
  walletCurrency?: string;
  date: string;
  time: string;
}

/** Detalle de exchange (GET /api/exchanges/:id) */
export interface ExchangeDetail extends Exchange {
  transactions: ExchangeTransaction[];
}

/** Respuesta paginada de exchanges */
export interface ExchangeList {
  data: Exchange[];
  total: number;
  limit: number;
}

/** Resultado de crear un exchange */
export interface ExchangeCreated {
  success: boolean;
  message: string;
  exchange: {
    id: number;
    rate: number;
    fromWallet: string;
    toWallet: string;
    fromAmount: number;
    toAmount: number;
    fromCurrency: string;
    toCurrency: string;
    description: string;
  };
  transactions: {
    debit: { id: number; feeTransactionId?: number | null };
    credit: { id: number; feeTransactionId?: number | null };
  };
}

/** Pago recurrente (GET /api/recurring-payments) */
export interface RecurringPayment {
  id: number;
  name: string;
  description?: string | null;
  amount: number;
  fee: number;
  currency: string;
  type: TransactionType;
  category?: string;
  categoryId: number;
  walletId?: number | null;
  isActive: boolean;
}

/** Tasa del día */
export interface DailyRate {
  id: number;
  date: string;
  bcv: number;
  paralelo: number;
  source?: string;
}

/** Tasa efectiva (GET /api/rates/effective) */
export interface EffectiveRate {
  date: string;
  vps: { bcv: number; paralelo: number };
  note?: string;
}

/** Settings (GET /api/settings) */
export interface Settings {
  timezone: string;
  name: string;
  version: string;
}

/** Estadísticas (GET /api/stats) */
export interface Stats {
  total_income: number;
  total_expense: number;
  net_balance: number;
  total_balance: number;
  transaction_count: number;
  summary: {
    totalTransactions: number;
    totalIncome: number;
    totalExpenses: number;
    net: number;
    totalBalance: number;
  };
}

/** Stats por categoría (GET /api/stats/by-category) */
export interface CategoryStat {
  name: string;
  type: TransactionType;
  total: number;
}

/** Sesión de acceso (GET /api/auth/sessions) */
export interface SessionInfo {
  jti: string;
  createdAt: number;
  lastUsedAt?: number | null;
  expiresAt?: number | null;
  deviceName?: string;
  ip?: string | null;
  userAgent?: string;
  current: boolean;
}

/** API token (GET /api/auth/tokens) */
export interface ApiToken {
  id: number;
  name: string;
  createdAt: number;
  lastUsedAt?: number | null;
  isActive: boolean;
}

/** Reporte de una billetera (GET /api/wallets/:id/report) */
export interface WalletReport {
  wallet: Wallet;
  transactions: WalletReportTransaction[];
}

export interface WalletReportTransaction {
  id: number;
  type: TransactionType;
  amount: number;
  fee: number;
  category?: string;
  description?: string;
  datetimeUtc: string;
  parentTransactionId?: number | null;
}
