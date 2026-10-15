/** Public page URL of a share token (served by the SPA route t/:token). */
export function shareUrl(origin: string, token: string): string {
  return `${origin}/t/${encodeURIComponent(token)}`;
}

export function whatsAppUrl(text: string, url: string): string {
  return `https://wa.me/?text=${encodeURIComponent(`${text}\n${url}`)}`;
}

export function telegramUrl(text: string, url: string): string {
  return `https://t.me/share/url?url=${encodeURIComponent(url)}&text=${encodeURIComponent(text)}`;
}
