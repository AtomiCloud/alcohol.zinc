import { Html, Head, Preview, Body, Container, Tailwind } from '@react-email/components';
import { Header } from './header';
import { Footer } from './footer';
import { ReactNode } from 'react';

interface EmailLayoutProps {
  children: ReactNode;
  baseUrl: string;
  supportEmail: string;
  whatsappUrl?: string;
  telegramUrl?: string;
  subject: string;
  previewText: string;
  userEmail?: string;
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
}: EmailLayoutProps) => {
  return (
    <Html>
      <Preview>{previewText}</Preview>
      <Tailwind
        config={{
          theme: {
            extend: {
              colors: {
                primary: '#1a365d',
                secondary: '#2d3748',
                accent: '#744210',
                gray: {
                  50: '#ffffff',
                  100: '#f7fafc',
                  200: '#edf2f7',
                  300: '#e2e8f0',
                  400: '#cbd5e0',
                  500: '#a0aec0',
                  600: '#718096',
                  700: '#4a5568',
                  800: '#2d3748',
                  900: '#1a202c',
                },
              },
            },
          },
        }}
      >
        <Head />
        <Body className="bg-gray-100 font-sans">
          <Container className="mx-auto py-4 px-2 sm:py-8 sm:px-4 max-w-2xl w-full">
            <div className="bg-white border-2 border-gray-300 rounded-lg shadow-lg overflow-hidden w-full">
              <Header baseUrl={baseUrl} />

              <div className="px-4 py-4 sm:px-8 sm:py-6">{children}</div>

              <Footer
                baseUrl={baseUrl}
                supportEmail={supportEmail}
                whatsappUrl={whatsappUrl}
                telegramUrl={telegramUrl}
                userEmail={userEmail}
              />
            </div>
          </Container>
        </Body>
      </Tailwind>
    </Html>
  );
};
