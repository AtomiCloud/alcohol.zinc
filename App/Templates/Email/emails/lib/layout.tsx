import { Html, Head, Preview, Body, Container, Section, Tailwind } from '@react-email/components';
import { pixelBasedPreset } from '@react-email/tailwind';
import { Header } from './header';
import { Footer } from './footer';
import { ReactNode } from 'react';

/**
 * Friendly email palette, based on the LazyTax navy and orange branding:
 * - navy   #0e2a47  logo outline — headings & receipt amounts (Georgia serif, matching argon's rendered heading font)
 * - brand  #9e451b  burnt orange — white button text meets WCAG AA contrast
 * - violet #7c3aed  premium accent / links
 * - emerald #059669 money-to-charity semantics
 * - amber  #f59e0b  grace/urgency
 * - danger #dc2626  payment failure only
 */
export const brandColors = {
  navy: '#0e2a47',
  brand: '#9e451b',
  paper: '#fffdf9',
  warm: '#fff5e8',
  cream: '#fff8ec',
  line: '#eadfce',
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
        sans: ['Arial', 'Helvetica', 'sans-serif'],
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
        <Body className="bg-[#f6f3ee] font-sans m-0 py-6 px-2">
          <Container className="mx-auto max-w-[600px] w-full table-fixed bg-paper border border-solid border-line rounded-xl">
            <Header baseUrl={baseUrl} />
            <Section className="table-fixed px-6 pt-6 pb-2">{children}</Section>
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
