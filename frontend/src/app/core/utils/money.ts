// Utilidades de formato de dinero.
// La app maneja montos en unidades decimales (lo que devuelve la API).

/** Formatea un número como moneda con el símbolo dado. */
export function formatMoney(amount: number, currency = 'USD'): string {
  const symbol = currencySymbol(currency);
  const formatted = new Intl.NumberFormat('es-VE', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(amount);
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

/** Formatea un número sin símbolo de moneda. */
export function formatNumber(value: number, digits = 2): string {
  return new Intl.NumberFormat('es-VE', {
    minimumFractionDigits: digits,
    maximumFractionDigits: digits,
  }).format(value);
}
