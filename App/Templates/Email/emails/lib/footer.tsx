import { Section, Text, Link, Hr } from '@react-email/components';

interface FooterProps {
  baseUrl: string;
  supportEmail: string;
  whatsappUrl?: string;
  telegramUrl?: string;
  userEmail?: string;
}

export const Footer = ({ baseUrl, supportEmail, whatsappUrl, telegramUrl, userEmail }: FooterProps) => {
  return (
    <Section className="bg-gray-200 px-4 py-4 sm:px-8 sm:py-6 border-t-4 border-gray-800">
      <Text className="text-sm text-gray-900 font-semibold mb-4 text-center sm:text-left">
        Need help? We're here to support you!
      </Text>

      <div className="flex flex-col gap-2 mb-4 text-center sm:text-left">
        <Link href={`mailto:${supportEmail}`} className="text-sm text-primary font-semibold underline">
          📧 Email Support: {supportEmail}
        </Link>

        {whatsappUrl && (
          <Link href={whatsappUrl} className="text-sm text-primary font-semibold underline">
            💬 WhatsApp Support
          </Link>
        )}

        {telegramUrl && (
          <Link href={telegramUrl} className="text-sm text-primary font-semibold underline">
            📱 Telegram Support
          </Link>
        )}
      </div>

      <Hr className="border-gray-800 border-2 my-4" />

      <Text className="text-xs text-gray-900 font-bold mb-2 text-center sm:text-left">
        © 2024 LazyTax.Club. All rights reserved.
      </Text>

      <div className="flex flex-col sm:flex-row gap-1 sm:gap-4 text-center sm:text-left">
        <Link href={`${baseUrl}/privacy`} className="text-xs text-gray-800 font-semibold underline">
          Privacy Policy
        </Link>
        <Link href={`${baseUrl}/terms`} className="text-xs text-gray-800 font-semibold underline">
          Terms of Service
        </Link>
        {userEmail && (
          <Link
            href={`${baseUrl}/unsubscribe?email=${encodeURIComponent(userEmail)}`}
            className="text-xs text-gray-800 font-semibold underline"
          >
            Unsubscribe
          </Link>
        )}
      </div>
    </Section>
  );
};
