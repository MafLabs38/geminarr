interface BrandLogoProps {
  /** Height and width in pixels. */
  size: number;
}

/** Two rings that overlap, the twins of Gemini. Decorative: the product name is always next to it, so it has no text of its own. Drawn in the colors of the theme. */
export default function BrandLogo({ size }: Readonly<BrandLogoProps>) {
  return (
    <svg
      aria-hidden="true"
      fill="none"
      height={size}
      viewBox="0 0 64 64"
      width={size}
      xmlns="http://www.w3.org/2000/svg"
    >
      <circle cx="24" cy="32" r="16" stroke="var(--accent)" strokeWidth="6" />
      <circle cx="40" cy="32" r="16" stroke="var(--secondary)" strokeWidth="6" />
    </svg>
  );
}
