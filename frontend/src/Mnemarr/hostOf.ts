/** The host of an address (with its port), for showing where something goes; the address itself when it cannot be read. */
export function hostOf(address: string): string {
  try {
    return new URL(address).host;
  } catch {
    return address;
  }
}
