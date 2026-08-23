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
