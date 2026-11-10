import { Html, Head, Preview, Body, Container, Section, Tailwind } from '@react-email/components';
import { pixelBasedPreset } from '@react-email/tailwind';
import { Header } from './header';
import { Footer } from './footer';
import { ReactNode } from 'react';

/**
 * LazyTax brand tokens, extracted from alcohol.argon:
 * - navy   #0e2a47  logo outline — headings & receipt amounts (Georgia serif, matching argon's rendered heading font)
 * - brand  #f97316  orange-500 — PWA theme color, gradient left pole, CTA fallback
 * - violet #7c3aed  premium accent / links
 * - emerald #059669 money-to-charity semantics
 * - amber  #f59e0b  grace/urgency
 * - danger #dc2626  payment failure only
 */
export const brandColors = {
  navy: '#0e2a47',
  brand: '#f97316',
  violet: '#7c3aed',
  fuchsia: '#d946ef',
  emerald: '#059669',
  amber: '#f59e0b',
  danger: '#dc2626',
};

export const emailTailwindConfig = {
  presets: [pixelBasedPreset],
  theme: {
    extend: {
      colors: brandColors,
      borderRadius: { lg: '8px', xl: '12px', full: '9999px' },
      fontFamily: {
        serif: ['Georgia', "'Times New Roman'", 'serif'],
        sans: ['Roboto', '-apple-system', "'Segoe UI'", 'Arial', 'sans-serif'],
      },
    },
  },
};

interface EmailLayoutProps {
  children: ReactNode;
  baseUrl: string;
  supportEmail: string;
  whatsappUrl?: string;
  telegramUrl?: string;
  subject: string;
  previewText: string;
  userEmail?: string;
  reason?: string;
}

export const EmailLayout = ({
  children,
  baseUrl,
  supportEmail,
  whatsappUrl,
  telegramUrl,
  subject,
  previewText,
  userEmail,
  reason,
}: EmailLayoutProps) => {
  return (
    <Html lang="en">
      <Preview>{previewText}</Preview>
      <Tailwind config={emailTailwindConfig}>
        <Head>
          <meta name="viewport" content="width=device-width, initial-scale=1" />
          <meta name="color-scheme" content="light" />
          <meta name="supported-color-schemes" content="light" />
        </Head>
        <Body className="bg-[#f8fafc] font-sans m-0 py-8 px-3">
          <Container className="mx-auto max-w-[600px] w-full table-fixed bg-white border border-solid border-[#e2e8f0] rounded-xl">
            <Section className="table-fixed px-9 pt-7 pb-2">
              <Header baseUrl={baseUrl} />
              {children}
            </Section>
            <Footer
              baseUrl={baseUrl}
              supportEmail={supportEmail}
              whatsappUrl={whatsappUrl}
              telegramUrl={telegramUrl}
              userEmail={userEmail}
              reason={reason}
            />
          </Container>
        </Body>
      </Tailwind>
    </Html>
  );
};
