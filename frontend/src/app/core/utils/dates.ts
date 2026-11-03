// Utilidades de fechas.
// El backend guarda instantes UTC y el front proyecta a la zona del usuario.

/** Devuelve la fecha de hoy (YYYY-MM-DD) en la zona horaria del usuario. */
export function todayInTimeZone(tz: string): string {
  return formatInTimeZone(new Date(), tz, 'yyyy-MM-dd');
}

/**
 * Formatea un instante (Date o string ISO) a un patrón, en la zona horaria
 * indicada. Patrones soportados: 'yyyy-MM-dd' y 'HH:mm'.
 */
export function formatInTimeZone(input: Date | string, tz: string, pattern: string): string {
  const d = typeof input === 'string' ? new Date(input) : input;
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: tz,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hour12: false,
  }).formatToParts(d);

  const get = (type: string) => parts.find((p) => p.type === type)?.value ?? '';
  const dateStr = `${get('year')}-${get('month')}-${get('day')}`;
  const timeStr = `${get('hour').padStart(2, '0')}:${get('minute').padStart(2, '0')}`;

  switch (pattern) {
    case 'yyyy-MM-dd':
      return dateStr;
    case 'HH:mm':
      return timeStr;
    default:
      return `${dateStr} ${timeStr}`;
  }
}

/**
 * Normaliza un valor de fecha de facturación (billing date) a yyyy-MM-dd.
 * - Si el valor es un string con formato yyyy-MM-dd, lo devuelve tal cual.
 * - Si es un Date (del datepicker), extrae sus componentes locales y los
 *   convierte a yyyy-MM-dd (no usa el instante reformateado en la tz).
 * - Si es null o undefined, devuelve null.
 */
export function normalizeBillingDate(value: string | Date | null | undefined, tz: string): string | null {
  if (!value) return null;
  
  // Si ya es un string con formato yyyy-MM-dd, devolverlo tal cual
  if (typeof value === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(value)) {
    return value;
  }
  
  // Si es un string sin formato o un Date, procesarlo
  const d = typeof value === 'string' ? new Date(value) : value;
  if (Number.isNaN(d.getTime())) return null;
  
  // Extraer los componentes locales del Date (no del instante UTC)
  // El datepicker de Angular Material entrega un Date con la fecha local
  const year = d.getFullYear();
  const month = (d.getMonth() + 1).toString().padStart(2, '0');
  const day = d.getDate().toString().padStart(2, '0');
  
  return `${year}-${month}-${day}`;
}
