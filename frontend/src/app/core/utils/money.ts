// Utilidades de formato de dinero.
// La app maneja montos en unidades decimales (lo que devuelve la API).
import { DecimalSeparator } from '../services/ui-preference.store';

/** Formatea un número como moneda con el símbolo dado. */
export function formatMoney(amount: number, currency = 'USD', separator: DecimalSeparator = ','): string {
  const symbol = currencySymbol(currency);
  const formatted = formatNumber(amount, 2, separator);
  return `${symbol}${formatted}`;
}

export function currencySymbol(currency: string): string {
  switch ((currency || '').toUpperCase()) {
    case 'USD':
      return '$';
    case 'VES':
      return 'Bs ';
    case 'EUR':
      return '€';
    case 'BRL':
    case 'BR':
      return 'R$ ';
    default:
      return '';
  }
}

/** Formatea un número sin símbolo de moneda, respetando el separador decimal. */
export function formatNumber(value: number, digits = 2, separator: DecimalSeparator = ','): string {
  const withComma = new Intl.NumberFormat('es-VE', {
    minimumFractionDigits: digits,
    maximumFractionDigits: digits,
  }).format(value);
  if (separator === '.') {
    // Convierte el formato español (1.234,56) a inglés (1,234.56).
    return withComma.replace(/\./g, '_TMP_').replace(/,/g, '.').replace(/_TMP_/g, ',');
  }
  return withComma;
}

/** Nombre legible de la denominación de una moneda, p.ej. 'Dólares estadounidenses'. */
export function currencyName(currency: string): string {
  switch ((currency || '').toUpperCase()) {
    case 'USD':
      return 'Dólares estadounidenses';
    case 'VES':
      return 'Bolívares';
    case 'EUR':
      return 'Euros';
    case 'BRL':
    case 'BR':
      return 'Reales brasileños';
    default:
      return currency || '';
  }
}
