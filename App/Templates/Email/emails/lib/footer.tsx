import { Section, Text, Link, Hr } from '@react-email/components';

interface FooterProps {
  baseUrl: string;
  supportEmail: string;
  whatsappUrl?: string;
  telegramUrl?: string;
  userEmail?: string;
}

/**
 * Muted footer echoing the argon site footer: support contact, legal links,
 * registered address. Transactional emails — no unsubscribe management needed.
 */
export const Footer = ({ baseUrl, supportEmail, whatsappUrl, telegramUrl }: FooterProps) => {
  const year = new Date().getFullYear();
  return (
    <Section className="px-9 pb-7">
      <Hr className="border-solid border-[#e2e8f0] border-t my-0 mb-4" />
      <Text className="text-[11.5px] leading-[1.6] text-[#94a3b8] m-0 mb-1.5">
        You&apos;re receiving this because you have a LazyTax account. Questions?{' '}
        <Link href={`mailto:${supportEmail}`} className="text-[#94a3b8] underline">
          {supportEmail}
        </Link>
      </Text>
      <Text className="text-[11.5px] leading-[1.6] text-[#94a3b8] m-0 mb-1.5">
        <Link href={`${baseUrl}/legal/privacy`} className="text-[#94a3b8] underline">
          Privacy
        </Link>
        {' · '}
        <Link href={`${baseUrl}/legal/terms`} className="text-[#94a3b8] underline">
          Terms
        </Link>
        {' · '}
        <Link href={`${baseUrl}/legal/refund`} className="text-[#94a3b8] underline">
          Refunds
        </Link>
        {whatsappUrl && (
          <>
            {' · '}
            <Link href={whatsappUrl} className="text-[#94a3b8] underline">
              WhatsApp
            </Link>
          </>
        )}
        {telegramUrl && (
          <>
            {' · '}
            <Link href={telegramUrl} className="text-[#94a3b8] underline">
              Telegram
            </Link>
          </>
        )}
      </Text>
      <Text className="text-[11.5px] leading-[1.6] text-[#94a3b8] m-0">
        © {year} LazyTax · 60 Paya Lebar Road, #06-28, Paya Lebar Square, Singapore 409051
      </Text>
    </Section>
  );
};
