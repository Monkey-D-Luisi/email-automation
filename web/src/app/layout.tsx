import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "Smart Inbox Review",
  description: "Local human review for classified support emails.",
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return <html lang="en"><body>{children}</body></html>;
}
