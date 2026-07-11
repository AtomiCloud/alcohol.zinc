import { Html, Head, Preview, Body, Container, Section, Tailwind } from '@react-email/components';
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
  theme: {
    extend: {
      colors: brandColors,
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
  /** top accent strip: emerald = money-to-charity, amber = grace, danger = failure, navy = neutral, gradient = brand */
  strip?: 'emerald' | 'amber' | 'danger' | 'neutral' | 'gradient';
}

const stripStyles: Record<NonNullable<EmailLayoutProps['strip']>, string> = {
  emerald: 'bg-emerald',
  amber: 'bg-amber',
  danger: 'bg-danger',
  neutral: 'bg-[#64748b]',
  gradient: 'bg-brand [background-image:linear-gradient(to_right,#f97316,#d946ef,#7c3aed)]',
};

export const EmailLayout = ({
  children,
  baseUrl,
  supportEmail,
  whatsappUrl,
  telegramUrl,
  subject,
  previewText,
  userEmail,
  strip = 'neutral',
}: EmailLayoutProps) => {
  return (
    <Html>
      <Preview>{previewText}</Preview>
      <Tailwind config={emailTailwindConfig}>
        <Head>
          <meta name="color-scheme" content="light" />
          <meta name="supported-color-schemes" content="light" />
        </Head>
        <Body className="bg-[#f8fafc] font-sans m-0 py-8 px-3">
          <Container className="mx-auto max-w-[600px] w-full bg-white border border-solid border-[#e2e8f0] rounded-xl overflow-hidden">
            <Section className={`h-[6px] leading-[6px] ${stripStyles[strip]}`}>&nbsp;</Section>
            <Section className="px-9 pt-7 pb-2">
              <Header baseUrl={baseUrl} />
              {children}
            </Section>
            <Footer
              baseUrl={baseUrl}
              supportEmail={supportEmail}
              whatsappUrl={whatsappUrl}
              telegramUrl={telegramUrl}
              userEmail={userEmail}
            />
          </Container>
        </Body>
      </Tailwind>
    </Html>
  );
};
